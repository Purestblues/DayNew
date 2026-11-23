using System.Globalization;

namespace DayNew.Core;

/// <summary>
/// 混合逻辑时钟（Hybrid Logical Clock）：
/// 固定格式 "{12位十六进制物理毫秒}-{4位十六进制计数}-{设备ID}"，
/// 可直接按字典序（Ordinal）比较因果与时间先后，不依赖双端系统时钟绝对对齐。
/// </summary>
public static class HlcClock
{
    private static readonly object SyncRoot = new();
    private static long _lastPhys;
    private static int _counter;
    private static string _defaultDeviceId = "win-local";

    public static void SetDefaultDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        lock (SyncRoot)
        {
            _defaultDeviceId = NormalizeDeviceId(deviceId);
        }
    }

    public static string Tick(string? deviceId = null)
    {
        lock (SyncRoot)
        {
            var dev = NormalizeDeviceId(deviceId ?? _defaultDeviceId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (now > _lastPhys)
            {
                _lastPhys = now;
                _counter = 0;
            }
            else
            {
                _counter++;
                if (_counter > 0xFFFF)
                {
                    _lastPhys++;
                    _counter = 0;
                }
            }

            return Format(_lastPhys, _counter, dev);
        }
    }

    public static string Receive(string? remoteHlc, string? deviceId = null)
    {
        lock (SyncRoot)
        {
            var dev = NormalizeDeviceId(deviceId ?? _defaultDeviceId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var (hasRemote, rPhys, rCnt) = TryParse(remoteHlc);

            if (!hasRemote)
            {
                if (now > _lastPhys)
                {
                    _lastPhys = now;
                    _counter = 0;
                }
                else
                {
                    _counter++;
                }

                return Format(_lastPhys, _counter, dev);
            }

            var maxPhys = Math.Max(now, Math.Max(_lastPhys, rPhys));
            if (maxPhys == _lastPhys && maxPhys == rPhys)
            {
                _counter = Math.Max(_counter, rCnt) + 1;
            }
            else if (maxPhys == _lastPhys)
            {
                _counter++;
            }
            else if (maxPhys == rPhys)
            {
                _lastPhys = maxPhys;
                _counter = rCnt + 1;
            }
            else
            {
                _lastPhys = now;
                _counter = 0;
            }

            if (_counter > 0xFFFF)
            {
                _lastPhys++;
                _counter = 0;
            }

            return Format(_lastPhys, _counter, dev);
        }
    }

    /// <summary>仅在加载本地或远端状态时推进时钟上限，不生成新事件字符串。</summary>
    public static void Observe(string? hlc)
    {
        var (ok, rPhys, rCnt) = TryParse(hlc);
        if (!ok) return;

        lock (SyncRoot)
        {
            if (rPhys > _lastPhys)
            {
                _lastPhys = rPhys;
                _counter = rCnt;
            }
            else if (rPhys == _lastPhys && rCnt > _counter)
            {
                _counter = rCnt;
            }
        }
    }

    public static int Compare(string? a, string? b)
    {
        var emptyA = string.IsNullOrEmpty(a);
        var emptyB = string.IsNullOrEmpty(b);
        if (emptyA && emptyB) return 0;
        if (emptyA) return -1;
        if (emptyB) return 1;
        return string.CompareOrdinal(a, b);
    }

    public static string Max(string? a, string? b)
        => Compare(a, b) >= 0 ? (a ?? "") : (b ?? "");

    private static string NormalizeDeviceId(string? deviceId)
    {
        var raw = (deviceId ?? "").Trim();
        return raw.Length == 0 ? "win-local" : raw;
    }

    private static string Format(long phys, int counter, string deviceId)
        => $"{phys:x12}-{counter:x4}-{deviceId}";

    private static (bool Ok, long Phys, int Counter) TryParse(string? hlc)
    {
        if (string.IsNullOrWhiteSpace(hlc)) return (false, 0, 0);
        var firstDash = hlc.IndexOf('-');
        if (firstDash <= 0) return (false, 0, 0);
        var secondDash = hlc.IndexOf('-', firstDash + 1);
        if (secondDash <= firstDash + 1) return (false, 0, 0);

        var physPart = hlc[..firstDash];
        var cntPart = hlc[(firstDash + 1)..secondDash];
        if (!long.TryParse(physPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var phys))
            return (false, 0, 0);
        if (!int.TryParse(cntPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cnt))
            return (false, 0, 0);

        return (true, phys, Math.Max(0, cnt));
    }
}
