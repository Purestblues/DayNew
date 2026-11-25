using System.Runtime.InteropServices;
using DayNew.Core;

namespace DayNew.Services;

public enum HotKeyStatus
{
    Disabled,
    Registered,
    ConflictOrFailed
}

/// <summary>
/// 系统级全局热键管理（绑定在常驻隐藏消息窗 Win32MessageWindow 上，窗口最小化或缩托盘仍生效）。
/// </summary>
public sealed class GlobalHotKeyService : IDisposable
{
    public const uint WmHotKey = 0x0312;

    private readonly IntPtr _hwnd;
    private readonly Action<string> _onTriggered;
    private readonly Dictionary<int, string> _idToAction = new();
    private readonly Dictionary<string, HotKeyStatus> _statuses = new(StringComparer.Ordinal);
    private bool _disposed;

    public event Action? StatusChanged;

    public GlobalHotKeyService(IntPtr hwnd, Action<string> onTriggered)
    {
        _hwnd = hwnd;
        _onTriggered = onTriggered;
    }

    public HotKeyStatus GetStatus(string actionId)
        => _statuses.TryGetValue(actionId, out var s) ? s : HotKeyStatus.Disabled;

    public void ApplySettings(AppSettings settings)
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;

        UnregisterAll();

        var atomId = 100;
        foreach (var def in ShortcutCatalog.All)
        {
            if (def.Scope != ShortcutScope.Global) continue;

            var gesture = settings.GetShortcut(def.Id);
            if (string.IsNullOrWhiteSpace(gesture))
            {
                _statuses[def.Id] = HotKeyStatus.Disabled;
                continue;
            }

            if (!ShortcutGesture.ToWin32(gesture, out var mods, out var vk))
            {
                _statuses[def.Id] = HotKeyStatus.ConflictOrFailed;
                continue;
            }

            var id = atomId++;
            if (RegisterHotKey(_hwnd, id, mods, vk))
            {
                _idToAction[id] = def.Id;
                _statuses[def.Id] = HotKeyStatus.Registered;
            }
            else
            {
                _statuses[def.Id] = HotKeyStatus.ConflictOrFailed;
            }
        }

        StatusChanged?.Invoke();
    }

    public bool HandleWindowMessage(uint msg, IntPtr wParam)
    {
        if (msg != WmHotKey) return false;

        var id = wParam.ToInt32();
        if (_idToAction.TryGetValue(id, out var actionId))
        {
            _onTriggered(actionId);
            return true;
        }

        return false;
    }

    public void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        foreach (var id in _idToAction.Keys)
            UnregisterHotKey(_hwnd, id);

        _idToAction.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
