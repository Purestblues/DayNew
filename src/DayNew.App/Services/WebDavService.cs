using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DayNew.Core;
using DayNew.ViewModels;

namespace DayNew.Services;

/// <summary>
/// 轻量 WebDAV 客户端（兼容坚果云）：
///   - 连接测试（PROPFIND Depth: 0，遇到 404 自动 MKCOL）
///   - 手动上传 .daynew.zip 备份（先写 .tmp 再 MOVE 原子替换，不支持 MOVE 时回退直写）
///   - 从云端拉取最新备份包至本地 backups/ 目录，交由 P1 导入确认弹窗执行合并或覆盖
///   - P2-B 每设备独立快照（device-&lt;deviceId&gt;.json.gz）列目录、ETag 增量拉取、HLC 字段级合并与按需上传
/// </summary>
public static class WebDavService
{
    public const string CloudBackupFileName = "backup-latest.daynew.zip";

    private static readonly HttpMethod PropfindMethod = new("PROPFIND");
    private static readonly HttpMethod MkcolMethod = new("MKCOL");
    private static readonly HttpMethod MoveMethod = new("MOVE");
    private static readonly int[] RetryDelaysMs = { 500, 1200 };

    private const string PropfindBodyXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getetag/><d:resourcetype/></d:prop></d:propfind>";

    public sealed record RemotePeerSnapshot(string FileName, string ETag, DeviceSnapshotDto Snapshot);

    public sealed record RemotePullBundle(List<RemotePeerSnapshot> Peers, bool OwnFileExistsOnServer, int RemoteDeviceFilesCount);

    public sealed record SyncResult(
        int RemoteFilesChecked,
        int RemoteFilesDownloaded,
        int UpdatedCount,
        int ConflictCopiesCount,
        int AddedCompletions,
        bool UploadedOwn,
        bool StoreChanged,
        string SyncedAt);

    public static string GetDeviceSnapshotFileName(string deviceId)
    {
        var safe = string.IsNullOrWhiteSpace(deviceId) ? "win-local" : deviceId.Trim();
        return $"device-{safe}.json.gz";
    }

    public static byte[] SerializeSnapshotGzip(DeviceSnapshotDto snapshot)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, StoreFile.JsonOptions);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(jsonBytes, 0, jsonBytes.Length);
        return ms.ToArray();
    }

    public static DeviceSnapshotDto DeserializeSnapshotGzip(byte[] gzBytes)
    {
        using var input = new MemoryStream(gzBytes);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        var dto = JsonSerializer.Deserialize<DeviceSnapshotDto>(output.ToArray(), StoreFile.JsonOptions)
            ?? throw new InvalidDataException("远端设备快照内容为空");
        dto.Store ??= new StoreDto();
        dto.Store.Goals ??= new();
        dto.Store.Tags ??= new();
        dto.Store.Persons ??= new();
        dto.Store.Locations ??= new();
        dto.Store.Schedules ??= new();
        dto.Completions ??= new();
        return dto;
    }

    public static string NormalizeFolderUrl(string? rawUrl)
    {
        var trimmed = (rawUrl ?? "").Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("WebDAV 地址格式无效，需以 http:// 或 https:// 开头", nameof(rawUrl));
        }

        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }

    public static async Task<(bool Ok, string Message)> TestConnectionAsync(
        string folderUrl,
        string user,
        string password,
        CancellationToken ct = default)
    {
        var normalized = NormalizeFolderUrl(folderUrl);
        using var client = CreateClient(user, password);

        using var propResp = await SendWithRetryAsync(client, () =>
        {
            var req = new HttpRequestMessage(PropfindMethod, normalized);
            req.Headers.Add("Depth", "0");
            return req;
        }, ct).ConfigureAwait(false);

        if ((int)propResp.StatusCode is 200 or 207)
            return (true, "连接成功");

        if (propResp.StatusCode == HttpStatusCode.NotFound)
        {
            using var mkcolResp = await SendWithRetryAsync(client,
                () => new HttpRequestMessage(MkcolMethod, normalized), ct).ConfigureAwait(false);

            if (mkcolResp.IsSuccessStatusCode || mkcolResp.StatusCode == HttpStatusCode.MethodNotAllowed)
                return (true, "连接成功（已自动创建远端目录）");

            if (mkcolResp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (false, "鉴权失败：请检查账号与坚果云应用专属密码");

            if (mkcolResp.StatusCode == HttpStatusCode.Conflict)
                return (false, "远端上级目录不存在，请检查 WebDAV 路径");

            return (false, $"创建目录失败：服务器返回 {(int)mkcolResp.StatusCode} {mkcolResp.ReasonPhrase}");
        }

        if (propResp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return (false, "鉴权失败：请检查账号与坚果云应用专属密码");

        return (false, $"服务器返回 {(int)propResp.StatusCode} {propResp.ReasonPhrase}");
    }

    public static async Task<string> UploadBackupZipAsync(
        DayStore store,
        StoreFile storeFile,
        CompletionLog completionLog,
        string folderUrl,
        string user,
        string password,
        CancellationToken ct = default)
    {
        var normalized = NormalizeFolderUrl(folderUrl);
        using var client = CreateClient(user, password);

        await EnsureDirectoryAsync(client, normalized, ct).ConfigureAwait(false);

        Directory.CreateDirectory(DataTransferService.BackupsFolder);
        var tempZipPath = Path.Combine(DataTransferService.BackupsFolder, "webdav-upload.daynew.zip");
        DataTransferService.ExportToZip(store, storeFile, completionLog, tempZipPath);
        var payload = await File.ReadAllBytesAsync(tempZipPath, ct).ConfigureAwait(false);

        var targetUrl = normalized + CloudBackupFileName;
        var tmpRemoteUrl = targetUrl + ".tmp";

        using (var putTmpResp = await SendWithRetryAsync(client, () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Put, tmpRemoteUrl)
            {
                Content = new ByteArrayContent(payload)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return req;
        }, ct).ConfigureAwait(false))
        {
            ThrowIfAuthOrError(putTmpResp, "上传临时备份失败");
        }

        using var moveResp = await SendWithRetryAsync(client, () =>
        {
            var req = new HttpRequestMessage(MoveMethod, tmpRemoteUrl);
            req.Headers.Add("Destination", targetUrl);
            req.Headers.Add("Overwrite", "T");
            return req;
        }, ct).ConfigureAwait(false);

        if (!moveResp.IsSuccessStatusCode)
        {
            // 部分 WebDAV 网关不支持 MOVE，回退直接 PUT 到目标文件名
            using var directPutResp = await SendWithRetryAsync(client, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Put, targetUrl)
                {
                    Content = new ByteArrayContent(payload)
                };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                return req;
            }, ct).ConfigureAwait(false);
            ThrowIfAuthOrError(directPutResp, "上传备份文件失败");
        }

        return DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    public static async Task<string> DownloadBackupZipAsync(
        string folderUrl,
        string user,
        string password,
        CancellationToken ct = default)
    {
        var normalized = NormalizeFolderUrl(folderUrl);
        using var client = CreateClient(user, password);

        var targetUrl = normalized + CloudBackupFileName;
        using var resp = await SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Get, targetUrl), ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new FileNotFoundException($"云端尚无备份文件（{CloudBackupFileName}）");

        ThrowIfAuthOrError(resp, "下载云端备份失败");

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        Directory.CreateDirectory(DataTransferService.BackupsFolder);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var localPath = Path.Combine(DataTransferService.BackupsFolder, $"webdav-restore-{stamp}.daynew.zip");
        var seq = 2;
        while (File.Exists(localPath))
        {
            localPath = Path.Combine(DataTransferService.BackupsFolder, $"webdav-restore-{stamp}-{seq}.daynew.zip");
            seq++;
        }

        var tmpLocalPath = localPath + ".tmp";
        await File.WriteAllBytesAsync(tmpLocalPath, bytes, ct).ConfigureAwait(false);
        File.Move(tmpLocalPath, localPath, overwrite: true);
        return localPath;
    }

    /// <summary>
    /// 多设备快照双向同步（必须由 UI 线程发起，两次 await 之间自动回到调用上下文读写 DayStore）：
    ///   1. PROPFIND Depth: 1 列目录（1 次请求）
    ///   2. 仅下载 ETag 发生变化的其他设备快照（0..N 次请求）
    ///   3. 回到调用线程取最新 DayStore 执行 HLC 字段级合并与完成记录去重合并
    ///   4. 仅当本地数据有变动或远端尚无本机快照时上传本机 .json.gz（0..1 次请求）
    /// </summary>
    public static async Task<SyncResult> SyncMultiDeviceAsync(
        DayStore store,
        StoreFile storeFile,
        CompletionLog completionLog,
        AppSettings settings,
        CancellationToken ct = default)
    {
        var password = settings.GetWebDavPassword();
        var folderUrl = NormalizeFolderUrl(settings.WebDavUrl);

        var pull = await PullRemoteSnapshotsAsync(folderUrl, settings.WebDavUser, password, settings, ct);

        var localDto = StoreFile.ToDto(store);
        var totalUpdated = 0;
        var totalConflicts = 0;
        var addedCompletions = 0;

        foreach (var peer in pull.Peers)
        {
            var peerId = string.IsNullOrWhiteSpace(peer.Snapshot.DeviceId)
                ? peer.FileName
                : peer.Snapshot.DeviceId;
            settings.PeerWatermarks.TryGetValue(peerId, out var watermark);

            var report = SyncMergeEngine.MergeStore(localDto, peer.Snapshot.Store, watermark);
            localDto = report.MergedDto;
            totalUpdated += report.UpdatedCount;
            totalConflicts += report.ConflictCopiesCount;

            if (peer.Snapshot.Completions.Count > 0)
                addedCompletions += completionLog.MergeEntries(peer.Snapshot.Completions);

            var nextWatermark = HlcClock.Max(report.MaxRemoteHlc, peer.Snapshot.SnapshotHlc);
            if (!string.IsNullOrEmpty(nextWatermark))
            {
                HlcClock.Observe(nextWatermark);
                settings.PeerWatermarks[peerId] = nextWatermark;
            }

            if (!string.IsNullOrEmpty(peer.ETag))
                settings.PeerEtags[peer.FileName] = peer.ETag;
        }

        var storeChanged = totalUpdated > 0 || totalConflicts > 0;
        if (storeChanged)
        {
            StoreFile.Apply(localDto, store);
            storeFile.Flush(store);
        }

        var allCompletions = completionLog.ReadAll();
        var fingerprint = ComputeStoreFingerprint(localDto, allCompletions.Count);
        var needUpload = !pull.OwnFileExistsOnServer ||
                         !string.Equals(settings.LastUploadedFingerprint, fingerprint, StringComparison.Ordinal);

        if (needUpload)
        {
            var snapshot = new DeviceSnapshotDto
            {
                Version = 1,
                DeviceId = settings.DeviceId,
                SnapshotHlc = HlcClock.Tick(settings.DeviceId),
                Store = localDto,
                Completions = allCompletions.ToList()
            };
            await PushDeviceSnapshotAsync(folderUrl, settings.WebDavUser, password, snapshot, ct);
            settings.LastUploadedFingerprint = fingerprint;
        }

        var syncedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        settings.LastWebDavSyncAt = syncedAt;
        settings.Save();

        return new SyncResult(
            RemoteFilesChecked: pull.RemoteDeviceFilesCount,
            RemoteFilesDownloaded: pull.Peers.Count,
            UpdatedCount: totalUpdated,
            ConflictCopiesCount: totalConflicts,
            AddedCompletions: addedCompletions,
            UploadedOwn: needUpload,
            StoreChanged: storeChanged,
            SyncedAt: syncedAt);
    }

    private static async Task<RemotePullBundle> PullRemoteSnapshotsAsync(
        string folderUrl,
        string user,
        string password,
        AppSettings settings,
        CancellationToken ct)
    {
        using var client = CreateClient(user, password);
        await EnsureDirectoryAsync(client, folderUrl, ct).ConfigureAwait(false);

        using var listResp = await SendWithRetryAsync(client, () =>
        {
            var req = new HttpRequestMessage(PropfindMethod, folderUrl);
            req.Headers.Add("Depth", "1");
            req.Content = new StringContent(PropfindBodyXml, Encoding.UTF8, "application/xml");
            return req;
        }, ct).ConfigureAwait(false);

        ThrowIfAuthOrError(listResp, "列举远端同步目录失败");

        var xmlText = await listResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var entries = ParsePropfindDirectoryEntries(xmlText);
        var ownFileName = GetDeviceSnapshotFileName(settings.DeviceId);
        var ownExists = false;
        var remoteDeviceFiles = 0;
        var peers = new List<RemotePeerSnapshot>();

        foreach (var (fileName, etag) in entries)
        {
            if (!fileName.StartsWith("device-", StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(fileName, ownFileName, StringComparison.OrdinalIgnoreCase))
            {
                ownExists = true;
                continue;
            }

            remoteDeviceFiles++;
            if (!string.IsNullOrEmpty(etag) &&
                settings.PeerEtags.TryGetValue(fileName, out var cachedEtag) &&
                string.Equals(cachedEtag, etag, StringComparison.Ordinal))
            {
                continue;
            }

            var fileUrl = folderUrl + Uri.EscapeDataString(fileName);
            using var getResp = await SendWithRetryAsync(client,
                () => new HttpRequestMessage(HttpMethod.Get, fileUrl), ct).ConfigureAwait(false);
            if (getResp.StatusCode == HttpStatusCode.NotFound) continue;
            ThrowIfAuthOrError(getResp, $"下载远端快照 {fileName} 失败");

            var gzBytes = await getResp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var resolvedEtag = getResp.Headers.ETag?.Tag ?? etag;
            var snapshot = DeserializeSnapshotGzip(gzBytes);
            peers.Add(new RemotePeerSnapshot(fileName, resolvedEtag, snapshot));
        }

        return new RemotePullBundle(peers, ownExists, remoteDeviceFiles);
    }

    private static async Task PushDeviceSnapshotAsync(
        string folderUrl,
        string user,
        string password,
        DeviceSnapshotDto snapshot,
        CancellationToken ct)
    {
        using var client = CreateClient(user, password);
        var gzBytes = SerializeSnapshotGzip(snapshot);
        var targetName = GetDeviceSnapshotFileName(snapshot.DeviceId);
        var targetUrl = folderUrl + Uri.EscapeDataString(targetName);
        var tmpRemoteUrl = targetUrl + ".tmp";

        using (var putTmpResp = await SendWithRetryAsync(client, () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Put, tmpRemoteUrl)
            {
                Content = new ByteArrayContent(gzBytes)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            return req;
        }, ct).ConfigureAwait(false))
        {
            ThrowIfAuthOrError(putTmpResp, "上传本机快照失败");
        }

        using var moveResp = await SendWithRetryAsync(client, () =>
        {
            var req = new HttpRequestMessage(MoveMethod, tmpRemoteUrl);
            req.Headers.Add("Destination", targetUrl);
            req.Headers.Add("Overwrite", "T");
            return req;
        }, ct).ConfigureAwait(false);

        if (!moveResp.IsSuccessStatusCode)
        {
            using var directPutResp = await SendWithRetryAsync(client, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Put, targetUrl)
                {
                    Content = new ByteArrayContent(gzBytes)
                };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
                return req;
            }, ct).ConfigureAwait(false);
            ThrowIfAuthOrError(directPutResp, "上传本机快照失败");
        }
    }

    private static List<(string FileName, string ETag)> ParsePropfindDirectoryEntries(string xmlText)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(xmlText)) return list;

        try
        {
            var doc = XDocument.Parse(xmlText);
            foreach (var resp in doc.Descendants().Where(e => e.Name.LocalName == "response"))
            {
                var hrefRaw = resp.Descendants().FirstOrDefault(e => e.Name.LocalName == "href")?.Value;
                if (string.IsNullOrWhiteSpace(hrefRaw)) continue;

                var pathPart = Uri.TryCreate(hrefRaw, UriKind.Absolute, out var absUri)
                    ? absUri.AbsolutePath
                    : hrefRaw;
                var trimmed = pathPart.TrimEnd('/');
                var slash = trimmed.LastIndexOf('/');
                var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
                name = Uri.UnescapeDataString(name);
                if (string.IsNullOrWhiteSpace(name)) continue;

                var etag = resp.Descendants().FirstOrDefault(e => e.Name.LocalName == "getetag")?.Value?.Trim() ?? "";
                list.Add((name, etag));
            }
        }
        catch
        {
            // XML 解析异常时返回已解析部分
        }

        return list;
    }

    private static string ComputeStoreFingerprint(StoreDto dto, int completionsCount)
    {
        var maxHlc = "";
        foreach (var g in dto.Goals) maxHlc = HlcClock.Max(maxHlc, g.Hlc);
        foreach (var t in dto.Tags) maxHlc = HlcClock.Max(maxHlc, t.Hlc);
        foreach (var p in dto.Persons ?? Enumerable.Empty<PersonDto>()) maxHlc = HlcClock.Max(maxHlc, p.Hlc);
        foreach (var l in dto.Locations ?? Enumerable.Empty<LocationDto>()) maxHlc = HlcClock.Max(maxHlc, l.Hlc);
        foreach (var s in dto.Schedules) maxHlc = HlcClock.Max(maxHlc, s.Hlc);
        if (dto.Presets is not null)
            foreach (var p in dto.Presets) maxHlc = HlcClock.Max(maxHlc, p.Hlc);

        return $"{maxHlc}|{dto.Goals.Count}|{dto.Tags.Count}|{dto.Persons?.Count ?? 0}|{dto.Locations?.Count ?? 0}|{dto.Schedules.Count}|{dto.Presets?.Count ?? 0}|{completionsCount}";
    }

    private static async Task EnsureDirectoryAsync(HttpClient client, string folderUrl, CancellationToken ct)
    {
        using var propResp = await SendWithRetryAsync(client, () =>
        {
            var req = new HttpRequestMessage(PropfindMethod, folderUrl);
            req.Headers.Add("Depth", "0");
            return req;
        }, ct).ConfigureAwait(false);

        if ((int)propResp.StatusCode is 200 or 207) return;

        if (propResp.StatusCode == HttpStatusCode.NotFound)
        {
            using var mkcolResp = await SendWithRetryAsync(client,
                () => new HttpRequestMessage(MkcolMethod, folderUrl), ct).ConfigureAwait(false);

            if (mkcolResp.IsSuccessStatusCode || mkcolResp.StatusCode == HttpStatusCode.MethodNotAllowed)
                return;

            if (mkcolResp.StatusCode == HttpStatusCode.Conflict)
                throw new InvalidOperationException("远端上级目录不存在，请检查 WebDAV 路径");

            ThrowIfAuthOrError(mkcolResp, "创建远端目录失败");
            return;
        }

        ThrowIfAuthOrError(propResp, "访问远端目录失败");
    }

    private static HttpClient CreateClient(string user, string password)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DayNew/0.6");

        if (!string.IsNullOrWhiteSpace(user) || !string.IsNullOrEmpty(password))
        {
            var raw = Encoding.UTF8.GetBytes($"{user}:{password}");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        }

        return client;
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = requestFactory();
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            var status = (int)response.StatusCode;
            if ((status == 503 || status == 429) && attempt < RetryDelaysMs.Length)
            {
                response.Dispose();
                await Task.Delay(RetryDelaysMs[attempt], ct).ConfigureAwait(false);
                continue;
            }

            return response;
        }
    }

    private static void ThrowIfAuthOrError(HttpResponseMessage resp, string action)
    {
        if (resp.IsSuccessStatusCode) return;

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("鉴权失败：请检查账号与坚果云应用专属密码");

        throw new HttpRequestException($"{action}：服务器返回 {(int)resp.StatusCode} {resp.ReasonPhrase}");
    }
}
