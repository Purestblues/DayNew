using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DayNew.Services;

/// <summary>
/// `DayNew.exe --mcp` 极薄 STDIO 桥接：
///   - 在 Program.cs 中于单实例 Mutex 与 WPF 启动之前分流；
///   - 不加载 data.json、不直接写盘，仅将 stdin 逐行 JSON-RPC 转发到主进程 127.0.0.1 /mcp；
///   - 若主程序未启动或端口未开启，明确提示「请先启动日新并在设置中开启 MCP 接口」。
/// </summary>
public static class McpStdioBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Run(string[] args)
    {
        var (url, token) = ResolveEndpointAndToken(args);
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        using var reader = new StreamReader(Console.OpenStandardInput(), utf8NoBom);
        using var writer = new StreamWriter(Console.OpenStandardOutput(), utf8NoBom) { AutoFlush = true };
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(75) };

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var reply = ForwardLineAsync(client, url, token, line).GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(reply))
                writer.WriteLine(reply);
        }

        return 0;
    }

    internal static (string Url, string Token) ResolveEndpointAndToken(string[] args)
    {
        string? argUrl = null;
        string? argToken = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                argUrl = args[++i];
            else if (string.Equals(args[i], "--token", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                argToken = args[++i];
        }

        var settings = AppSettings.Load();
        var url = !string.IsNullOrWhiteSpace(argUrl)
            ? argUrl.Trim()
            : (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAYNEW_MCP_URL"))
                ? Environment.GetEnvironmentVariable("DAYNEW_MCP_URL")!.Trim()
                : $"http://127.0.0.1:{settings.McpPort}/mcp");

        var token = !string.IsNullOrWhiteSpace(argToken)
            ? argToken.Trim()
            : (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAYNEW_MCP_TOKEN"))
                ? Environment.GetEnvironmentVariable("DAYNEW_MCP_TOKEN")!.Trim()
                : settings.GetMcpToken());

        return (url, token);
    }

    internal static async Task<string?> ForwardLineAsync(
        HttpClient client,
        string url,
        string token,
        string line,
        CancellationToken ct = default)
    {
        JsonNode? idNode = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("id", out var idEl) &&
                idEl.ValueKind != JsonValueKind.Null)
            {
                idNode = JsonNode.Parse(idEl.GetRawText());
            }
        }
        catch
        {
            return BuildBridgeError(null, -32700, "STDIO 输入不是有效的 JSON-RPC 消息");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(line, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.Accepted || (resp.IsSuccessStatusCode && string.IsNullOrWhiteSpace(body)))
                return null;

            if (resp.StatusCode == HttpStatusCode.Forbidden)
                return idNode is null ? null : BuildBridgeError(idNode, -32003, "请先在日新「设置 → AI 与 MCP 接口」中开启 MCP 服务");

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return idNode is null ? null : BuildBridgeError(idNode, -32001, "MCP Bearer Token 鉴权失败，请检查或重新复制配置");

            if (!string.IsNullOrWhiteSpace(body))
                return body.Replace("\r", "").Replace("\n", "");

            return idNode is null ? null : BuildBridgeError(idNode, -32603, $"日新 MCP 服务返回状态 {(int)resp.StatusCode}");
        }
        catch (HttpRequestException)
        {
            return idNode is null
                ? null
                : BuildBridgeError(idNode, -32000, "无法连接日新主程序，请先启动日新（托盘常驻即可）并在设置中开启 MCP 接口");
        }
        catch (TaskCanceledException)
        {
            return idNode is null ? null : BuildBridgeError(idNode, -32000, "请求日新 MCP 服务超时");
        }
    }

    private static string BuildBridgeError(JsonNode? idNode, int code, string message)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return obj.ToJsonString(JsonOptions);
    }
}
