using DayNew.Core;
using DayNew.Services;

namespace DayNew.ViewModels;

public enum SettingsTab
{
    General,
    Shortcuts,
    SyncAndAi
}

/// <summary>设置页「快捷键」管理中的单条可编辑项。</summary>
public sealed class ShortcutItemVM : ObservableObject
{
    private string _gesture = "";
    private bool _isRecording;
    private HotKeyStatus _hotKeyStatus = HotKeyStatus.Disabled;

    public ShortcutItemVM(ShortcutDefinition def, string currentGesture, HotKeyStatus hotKeyStatus)
    {
        Id = def.Id;
        Scope = def.Scope;
        Title = def.Title;
        Description = def.Description;
        DefaultGesture = def.DefaultGesture;
        _gesture = currentGesture;
        _hotKeyStatus = hotKeyStatus;
    }

    public string Id { get; }
    public ShortcutScope Scope { get; }
    public string Title { get; }
    public string Description { get; }
    public string DefaultGesture { get; }

    public bool IsGlobal => Scope == ShortcutScope.Global;

    public string Gesture
    {
        get => _gesture;
        set
        {
            if (!Set(ref _gesture, value)) return;
            OnPropertyChanged(nameof(DisplayGesture));
            OnPropertyChanged(nameof(IsDisabled));
            OnPropertyChanged(nameof(IsCustomized));
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (!Set(ref _isRecording, value)) return;
            OnPropertyChanged(nameof(DisplayGesture));
        }
    }

    public HotKeyStatus HotKeyStatus
    {
        get => _hotKeyStatus;
        set
        {
            if (!Set(ref _hotKeyStatus, value)) return;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsStatusWarning));
            OnPropertyChanged(nameof(IsStatusOk));
        }
    }

    public string DisplayGesture
        => IsRecording
            ? "请按下组合键…"
            : string.IsNullOrWhiteSpace(Gesture)
                ? "未设置"
                : Gesture;

    public bool IsDisabled => string.IsNullOrWhiteSpace(Gesture);

    public bool IsCustomized
        => !string.Equals(Gesture, DefaultGesture, StringComparison.OrdinalIgnoreCase);

    public bool IsStatusOk => IsGlobal && HotKeyStatus == HotKeyStatus.Registered;

    public bool IsStatusWarning => IsGlobal && HotKeyStatus == HotKeyStatus.ConflictOrFailed;

    public string StatusText => !IsGlobal
        ? ""
        : HotKeyStatus switch
        {
            HotKeyStatus.Registered => "已注册生效",
            HotKeyStatus.ConflictOrFailed => "注册失败（可能被其他程序占用）",
            _ => "已禁用"
        };
}
