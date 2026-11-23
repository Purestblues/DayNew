using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DayNew.Services;

/// <summary>
/// 极轻量的纯 Win32 隐藏消息窗口：不走 WPF HwndSource/MediaContext，
/// 避免后台静默驻留时拉起图形渲染管线；同时支撑单实例唤醒与托盘消息回调。
/// </summary>
public sealed class Win32MessageWindow : IDisposable
{
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly string _className;
    private readonly WndProcDelegate _wndProc;
    private readonly Func<uint, IntPtr, IntPtr, IntPtr?> _handler;
    private IntPtr _hwnd;
    private bool _disposed;

    public IntPtr Handle => _hwnd;

    public Win32MessageWindow(string windowTitle, Func<uint, IntPtr, IntPtr, IntPtr?> handler)
    {
        _handler = handler;
        _className = $"DayNew.Host.{Guid.NewGuid():N}";
        _wndProc = WndProc;

        var hInstance = GetModuleHandleW(null);
        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = _className
        };

        if (RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        const uint wsExToolWindow = 0x00000080;
        const uint wsExNoActivate = 0x08000000;
        const uint wsPopup = 0x80000000;

        _hwnd = CreateWindowExW(
            wsExToolWindow | wsExNoActivate,
            _className,
            windowTitle,
            wsPopup,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            UnregisterClassW(_className, hInstance);
            throw new Win32Exception(err);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (_handler(msg, wParam, lParam) is { } result)
                return result;
        }
        catch
        {
            // 消息钩子异常不击穿 Win32 消息循环
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        UnregisterClassW(_className, GetModuleHandleW(null));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
