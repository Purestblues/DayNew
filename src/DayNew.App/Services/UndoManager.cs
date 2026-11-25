namespace DayNew.Services;

public sealed record CompletionUndoEvent(string TaskId, DateTime? ForDate, bool Done);

/// <summary>
/// 业务操作撤销 / 重做栈：基于不可变 JSON 快照 + 完成记录增量冲抵（严格遵守 completions.ndjson 只追加原则）。
/// </summary>
public sealed class UndoManager
{
    public const int MaxEntries = 40;

    private sealed record UndoEntry(
        string Label,
        string BeforeJson,
        string AfterJson,
        IReadOnlyList<CompletionUndoEvent> Completions);

    private readonly List<UndoEntry> _undoStack = new();
    private readonly List<UndoEntry> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? NextUndoLabel => _undoStack.Count > 0 ? _undoStack[^1].Label : null;
    public string? NextRedoLabel => _redoStack.Count > 0 ? _redoStack[^1].Label : null;

    public static string CaptureJson(DayStore store)
        => StoreFile.SerializeDto(StoreFile.ToRawSnapshotDto(store));

    public void Push(
        string label,
        string beforeJson,
        string afterJson,
        IReadOnlyList<CompletionUndoEvent>? completions = null)
    {
        var events = completions ?? Array.Empty<CompletionUndoEvent>();
        if (string.Equals(beforeJson, afterJson, StringComparison.Ordinal) && events.Count == 0)
            return;

        _redoStack.Clear();
        _undoStack.Add(new UndoEntry(label, beforeJson, afterJson, events));
        if (_undoStack.Count > MaxEntries)
            _undoStack.RemoveAt(0);
    }

    public bool TryUndo(DayStore store, CompletionLog history, out string label)
    {
        label = "";
        if (_undoStack.Count == 0) return false;

        var entry = _undoStack[^1];
        var dto = StoreFile.DeserializeDto(entry.BeforeJson);
        if (dto is null) return false;

        _undoStack.RemoveAt(_undoStack.Count - 1);
        StoreFile.ApplyUndoSnapshot(dto, store);

        foreach (var ev in entry.Completions)
            history.Append(ev.TaskId, ev.ForDate, !ev.Done);

        _redoStack.Add(entry);
        if (_redoStack.Count > MaxEntries)
            _redoStack.RemoveAt(0);

        label = entry.Label;
        return true;
    }

    public bool TryRedo(DayStore store, CompletionLog history, out string label)
    {
        label = "";
        if (_redoStack.Count == 0) return false;

        var entry = _redoStack[^1];
        var dto = StoreFile.DeserializeDto(entry.AfterJson);
        if (dto is null) return false;

        _redoStack.RemoveAt(_redoStack.Count - 1);
        StoreFile.ApplyUndoSnapshot(dto, store);

        foreach (var ev in entry.Completions)
            history.Append(ev.TaskId, ev.ForDate, ev.Done);

        _undoStack.Add(entry);
        if (_undoStack.Count > MaxEntries)
            _undoStack.RemoveAt(0);

        label = entry.Label;
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
