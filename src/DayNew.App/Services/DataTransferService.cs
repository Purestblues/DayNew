using System.IO;
using System.IO.Compression;
using System.Text;
using DayNew.ViewModels;

namespace DayNew.Services;

/// <summary>
/// 全部数据的一键导出（.daynew.zip）与导入（覆盖 / 按 ID 合并）。
/// 导出包内含原样 data.json 与 completions.ndjson 两个文件，复用 StoreDto 长期契约。
/// </summary>
public static class DataTransferService
{
    public const string DataEntryName = "data.json";
    public const string CompletionsEntryName = "completions.ndjson";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string BackupsFolder => Path.Combine(StoreFile.Folder, "backups");

    public sealed class ImportPackagePreview
    {
        public required string FilePath { get; init; }
        public required string FileName { get; init; }
        public required StoreDto Dto { get; init; }
        public required IReadOnlyList<CompletionLog.CompletionEntry> Completions { get; init; }
        public required bool HasCompletionsEntry { get; init; }

        public int ScheduleCount => Dto.Schedules.Count(s => !s.Del);
        public int GoalCount => Dto.Goals.Count(g => !g.Del);
        public int TagCount => Dto.Tags.Count(t => !t.Del);
        public int PresetCount => Dto.Presets?.Count(p => !p.Del) ?? 0;
        public int CompletionCount => Completions.Count;
    }

    public sealed record ImportResult(string BackupPath, int AddedCompletions);

    /// <summary>将当前全部业务数据与完成记录原子导出为单个 .daynew.zip 包。</summary>
    public static void ExportToZip(DayStore store, StoreFile storeFile, CompletionLog completionLog, string targetZipPath)
    {
        if (string.IsNullOrWhiteSpace(targetZipPath))
            throw new ArgumentException("导出路径不能为空", nameof(targetZipPath));

        storeFile.Flush(store);
        WritePackageToZip(store, completionLog, targetZipPath);
    }

    /// <summary>在 %LOCALAPPDATA%\DayNew\backups\ 生成一份带时间戳的完整备份包。</summary>
    public static string BackupCurrentToZip(DayStore store, StoreFile storeFile, CompletionLog completionLog)
    {
        storeFile.Flush(store);
        Directory.CreateDirectory(BackupsFolder);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(BackupsFolder, $"backup-{stamp}.daynew.zip");
        var seq = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(BackupsFolder, $"backup-{stamp}-{seq}.daynew.zip");
            seq++;
        }

        WritePackageToZip(store, completionLog, candidate);
        return candidate;
    }

    /// <summary>读取并校验待导入的 .daynew.zip / .zip 或单个 .json 数据文件。</summary>
    public static ImportPackagePreview InspectPackage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("找不到指定的数据文件", filePath);

        if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var rawJson = File.ReadAllText(filePath, Encoding.UTF8);
            var jsonDto = ParseStoreDto(rawJson);
            return new ImportPackagePreview
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Dto = jsonDto,
                Completions = Array.Empty<CompletionLog.CompletionEntry>(),
                HasCompletionsEntry = false
            };
        }

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read);

        var dataEntry = FindEntry(archive, DataEntryName)
            ?? throw new InvalidDataException($"备份包内缺少 {DataEntryName}");

        string dataJson;
        using (var reader = new StreamReader(dataEntry.Open(), Encoding.UTF8))
            dataJson = reader.ReadToEnd();

        var dto = ParseStoreDto(dataJson);

        var completionsEntry = FindEntry(archive, CompletionsEntryName);
        IReadOnlyList<CompletionLog.CompletionEntry> completions = Array.Empty<CompletionLog.CompletionEntry>();
        if (completionsEntry is not null)
        {
            using var compReader = new StreamReader(completionsEntry.Open(), Encoding.UTF8);
            completions = CompletionLog.ParseLines(compReader.ReadToEnd());
        }

        return new ImportPackagePreview
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Dto = dto,
            Completions = completions,
            HasCompletionsEntry = completionsEntry is not null
        };
    }

    /// <summary>执行导入：先自动备份当前全部数据，再按覆盖或按 ID 合并落盘。</summary>
    public static ImportResult ApplyImport(
        ImportPackagePreview pkg,
        bool merge,
        DayStore store,
        StoreFile storeFile,
        CompletionLog completionLog)
    {
        var backupPath = BackupCurrentToZip(store, storeFile, completionLog);
        var addedCompletions = 0;

        if (!merge)
        {
            StoreFile.Apply(pkg.Dto, store);
            completionLog.ReplaceAll(pkg.Completions);
            addedCompletions = pkg.Completions.Count;
        }
        else
        {
            var currentDto = StoreFile.ToDto(store);
            var mergedDto = StoreFile.MergeDtos(currentDto, pkg.Dto);
            StoreFile.Apply(mergedDto, store);
            if (pkg.Completions.Count > 0)
                addedCompletions = completionLog.MergeEntries(pkg.Completions);
        }

        storeFile.Flush(store);
        return new ImportResult(backupPath, addedCompletions);
    }

    private static void WritePackageToZip(DayStore store, CompletionLog completionLog, string targetZipPath)
    {
        var dir = Path.GetDirectoryName(targetZipPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var dataJson = StoreFile.SerializeDto(StoreFile.ToDto(store));
        var completionsText = File.Exists(CompletionLog.Path)
            ? File.ReadAllText(CompletionLog.Path, Encoding.UTF8)
            : CompletionLog.FormatLines(completionLog.ReadAll());

        var tmpPath = targetZipPath + ".tmp";
        using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Utf8NoBom))
        {
            WriteTextEntry(archive, DataEntryName, dataJson);
            WriteTextEntry(archive, CompletionsEntryName, completionsText);
        }

        File.Move(tmpPath, targetZipPath, overwrite: true);
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Utf8NoBom);
        writer.Write(content);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string fileName)
        => archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, fileName, StringComparison.OrdinalIgnoreCase))
           ?? archive.Entries.FirstOrDefault(e => string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase));

    private static StoreDto ParseStoreDto(string json)
    {
        StoreDto? dto;
        try
        {
            dto = StoreFile.DeserializeDto(json);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("data.json 不是有效的 JSON 格式", ex);
        }

        if (dto is null)
            throw new InvalidDataException("data.json 内容为空");

        dto.Goals ??= new List<GoalDto>();
        dto.Tags ??= new List<TagDto>();
        dto.Schedules ??= new List<ScheduleDto>();
        return dto;
    }
}
