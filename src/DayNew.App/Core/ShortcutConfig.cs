using System.Windows.Input;

namespace DayNew.Core;

public enum ShortcutScope
{
    Global,
    InApp
}

public sealed record ShortcutDefinition(
    string Id,
    ShortcutScope Scope,
    string Title,
    string Description,
    string DefaultGesture);

public static class ShortcutIds
{
    public const string GlobalToggleWindow = "global.toggle_window";
    public const string GlobalOpenSearch = "global.open_search";
    public const string GlobalNewSchedule = "global.new_schedule";
    public const string GlobalToggleFocusMini = "global.toggle_focus_mini";

    public const string AppSearch = "app.search";
    public const string AppUndo = "app.undo";
    public const string AppRedo = "app.redo";
    public const string AppNewSchedule = "app.new_schedule";
    public const string AppSettings = "app.settings";
    public const string AppNav1 = "app.nav_1";
    public const string AppNav2 = "app.nav_2";
    public const string AppNav3 = "app.nav_3";
    public const string AppNav4 = "app.nav_4";
    public const string AppNav5 = "app.nav_5";
    public const string AppNav6 = "app.nav_6";
    public const string AppNav7 = "app.nav_7";
    public const string AppNav8 = "app.nav_8";
    public const string AppNav9 = "app.nav_9";
}

public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutDefinition> All { get; } =
    [
        new(ShortcutIds.GlobalToggleWindow, ShortcutScope.Global,
            "唤起 / 隐藏主窗口", "任意窗口或托盘后台常驻时，一键呼出或收起日新", "Ctrl+Alt+Space"),
        new(ShortcutIds.GlobalOpenSearch, ShortcutScope.Global,
            "唤起并打开搜索", "切到前台并立即弹出全局搜索面板", "Ctrl+Alt+K"),
        new(ShortcutIds.GlobalNewSchedule, ShortcutScope.Global,
            "唤起并新建日程", "切到前台并立即打开新建日程弹窗", "Ctrl+Alt+N"),
        new(ShortcutIds.GlobalToggleFocusMini, ShortcutScope.Global,
            "呼出 / 隐藏专注迷你窗", "随时切换桌面置顶专注悬浮胶囊的显隐", "Ctrl+Alt+F"),

        new(ShortcutIds.AppSearch, ShortcutScope.InApp,
            "打开搜索面板", "搜索日程、目标、标签、人物与地点", "Ctrl+K"),
        new(ShortcutIds.AppUndo, ShortcutScope.InApp,
            "撤销操作", "撤销上一步日程、目标或标签改动（文本编辑框内保留文字撤销）", "Ctrl+Z"),
        new(ShortcutIds.AppRedo, ShortcutScope.InApp,
            "重做操作", "恢复刚撤销的一步改动（同时兼容 Ctrl+Shift+Z）", "Ctrl+Y"),
        new(ShortcutIds.AppNewSchedule, ShortcutScope.InApp,
            "新建日程", "在主界面直接弹出新建日程窗口", "Ctrl+N"),
        new(ShortcutIds.AppSettings, ShortcutScope.InApp,
            "打开设置", "打开设置弹窗", "Ctrl+,"),
        new(ShortcutIds.AppNav1, ShortcutScope.InApp, "切换到「专注」", "导航至专注计时中心页", "Ctrl+1"),
        new(ShortcutIds.AppNav2, ShortcutScope.InApp, "切换到「概览」", "导航至概览页", "Ctrl+2"),
        new(ShortcutIds.AppNav3, ShortcutScope.InApp, "切换到「时间轴」", "导航至时间轴页", "Ctrl+3"),
        new(ShortcutIds.AppNav4, ShortcutScope.InApp, "切换到「标签」", "导航至标签 / 人物 / 地点页", "Ctrl+4"),
        new(ShortcutIds.AppNav5, ShortcutScope.InApp, "切换到「目标」", "导航至目标页", "Ctrl+5"),
        new(ShortcutIds.AppNav6, ShortcutScope.InApp, "切换到「四象限」", "导航至艾森豪威尔看板", "Ctrl+6"),
        new(ShortcutIds.AppNav7, ShortcutScope.InApp, "切换到「截止日期」", "导航至截止日期页", "Ctrl+7"),
        new(ShortcutIds.AppNav8, ShortcutScope.InApp, "切换到「未来事项」", "导航至未来事项页", "Ctrl+8"),
        new(ShortcutIds.AppNav9, ShortcutScope.InApp, "切换到「逾期」", "导航至逾期事项页", "Ctrl+9"),
    ];

    public static ShortcutDefinition? Find(string id)
        => All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));
}

public static class ShortcutGesture
{
    public static bool IsModifierOnly(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;

    public static bool IsFunctionKey(Key key) => key >= Key.F1 && key <= Key.F12;

    public static bool IsValidForScope(ShortcutScope scope, ModifierKeys modifiers, Key key)
    {
        if (key == Key.None || IsModifierOnly(key)) return false;
        if (key == Key.Escape) return false;

        if (scope == ShortcutScope.Global)
            return modifiers != ModifierKeys.None || IsFunctionKey(key);

        // 应用内快捷键至少需要 Ctrl/Alt/Win 或 F1..F12，避免拦截日常输入
        var hasCommandModifier = (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;
        return hasCommandModifier || IsFunctionKey(key);
    }

    public static string Normalize(string? gesture)
        => TryParse(gesture, out var mods, out var key) ? Format(mods, key) : "";

    public static bool Matches(string? gesture, ModifierKeys modifiers, Key key)
    {
        if (!TryParse(gesture, out var expectedMods, out var expectedKey)) return false;
        return modifiers == expectedMods && NormalizeKey(key) == expectedKey;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var normalizedKey = NormalizeKey(key);
        if (normalizedKey == Key.None || IsModifierOnly(normalizedKey)) return "";

        var parts = new List<string>(5);
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(FormatKeyName(normalizedKey));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;

        var raw = (text ?? "").Trim();
        if (raw.Length == 0) return false;

        var tokens = raw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return false;

        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win" or "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }

        return TryParseKeyName(tokens[^1], out key);
    }

    public static bool ToWin32(string? text, out uint fsModifiers, out uint vk)
    {
        fsModifiers = 0;
        vk = 0;
        if (!TryParse(text, out var mods, out var key)) return false;

        const uint modAlt = 0x0001;
        const uint modControl = 0x0002;
        const uint modShift = 0x0004;
        const uint modWin = 0x0008;
        const uint modNoRepeat = 0x4000;

        if ((mods & ModifierKeys.Alt) != 0) fsModifiers |= modAlt;
        if ((mods & ModifierKeys.Control) != 0) fsModifiers |= modControl;
        if ((mods & ModifierKeys.Shift) != 0) fsModifiers |= modShift;
        if ((mods & ModifierKeys.Windows) != 0) fsModifiers |= modWin;
        fsModifiers |= modNoRepeat;

        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    private static Key NormalizeKey(Key key)
        => key >= Key.NumPad0 && key <= Key.NumPad9
            ? Key.D0 + (key - Key.NumPad0)
            : key;

    private static string FormatKeyName(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        return key switch
        {
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.Oem3 => "`",
            Key.Oem5 => "\\",
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Next => "PageDown",
            _ => key.ToString()
        };
    }

    private static bool TryParseKeyName(string token, out Key key)
    {
        if (token.Length == 1 && token[0] is >= '0' and <= '9')
        {
            key = Key.D0 + (token[0] - '0');
            return true;
        }

        switch (token.ToLowerInvariant())
        {
            case ",": key = Key.OemComma; return true;
            case ".": key = Key.OemPeriod; return true;
            case "/": key = Key.OemQuestion; return true;
            case ";": key = Key.OemSemicolon; return true;
            case "'": key = Key.OemQuotes; return true;
            case "[": key = Key.OemOpenBrackets; return true;
            case "]": key = Key.OemCloseBrackets; return true;
            case "-": key = Key.OemMinus; return true;
            case "=": key = Key.OemPlus; return true;
            case "`": key = Key.Oem3; return true;
            case "\\": key = Key.Oem5; return true;
            case "enter" or "return": key = Key.Return; return true;
            case "backspace" or "back": key = Key.Back; return true;
            case "space": key = Key.Space; return true;
            case "pgdn" or "pagedown": key = Key.PageDown; return true;
            case "pgup" or "pageup": key = Key.PageUp; return true;
        }

        if (Enum.TryParse<Key>(token, ignoreCase: true, out var parsed) && !IsModifierOnly(parsed))
        {
            key = NormalizeKey(parsed);
            return true;
        }

        key = Key.None;
        return false;
    }
}
