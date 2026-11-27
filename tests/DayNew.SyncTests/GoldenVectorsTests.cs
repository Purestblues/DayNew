using System.Globalization;
using System.IO;
using System.Text.Json;
using DayNew.Core;
using DayNew.Services;
using Xunit;

namespace DayNew.SyncTests;

public sealed class GoldenVectorsTests
{
    private sealed class GoldenVectors
    {
        public List<HlcCompareCase> HlcCompareCases { get; set; } = new();
        public List<MergeCase> MergeCases { get; set; } = new();
        public List<RemindNormalizeCase> RemindNormalizeCases { get; set; } = new();
        public List<RemindDeadlineDefaultCase> RemindDeadlineDefaultCases { get; set; } = new();
        public List<RemindTriggerCase> RemindTriggerCases { get; set; } = new();
        public List<DeadlineTriggerCase> DeadlineTriggerCases { get; set; } = new();
        public List<DeadlineParseCase> DeadlineParseCases { get; set; } = new();
        public List<DeadlineOverdueCase> DeadlineOverdueCases { get; set; } = new();
        public List<RepeatCase> RepeatCases { get; set; } = new();
        public List<CompletionMergeCase> CompletionMergeCases { get; set; } = new();
    }

    private sealed class HlcCompareCase
    {
        public string Name { get; set; } = "";
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public int ExpectedSign { get; set; }
    }

    private sealed class MergeCase
    {
        public string Name { get; set; } = "";
        public string LastSyncedRemoteHlc { get; set; } = "";
        public StoreDto Local { get; set; } = new();
        public StoreDto Remote { get; set; } = new();
        public MergeExpected Expected { get; set; } = new();
    }

    private sealed class MergeExpected
    {
        public int UpdatedCount { get; set; }
        public int ConflictCopiesCount { get; set; }
        public string MaxRemoteHlc { get; set; } = "";
        public bool PresetsIsNull { get; set; }
        public string? GoalTitle { get; set; }
        public int ScheduleCount { get; set; }
        public string ScheduleTitle { get; set; } = "";
        public string? ScheduleRemind { get; set; }
        public List<string>? ScheduleTagIds { get; set; }
        public List<string>? SchedulePersonIds { get; set; }
        public List<string>? ScheduleLocationIds { get; set; }
        public string? ConflictCopyTitle { get; set; }
        public string? ConflictCopyStart { get; set; }
        public bool ScheduleDel { get; set; }
    }

    private sealed class RemindNormalizeCase
    {
        public string Raw { get; set; } = "";
        public string LegacyFallback { get; set; } = "";
        public string Expected { get; set; } = "";
    }

    private sealed class RemindDeadlineDefaultCase
    {
        public string Raw { get; set; } = "";
        public string Expected { get; set; } = "";
    }

    private sealed class ExpectedTrigger
    {
        public string Node { get; set; } = "";
        public string TargetTime { get; set; } = "";
        public string ToastTitle { get; set; } = "";
        public bool IsEnd { get; set; }
    }

    private sealed class RemindTriggerCase
    {
        public string Remind { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public string Title { get; set; } = "";
        public List<ExpectedTrigger> Expected { get; set; } = new();
    }

    private sealed class DeadlineTriggerCase
    {
        public string Remind { get; set; } = "";
        public string DueTime { get; set; } = "";
        public string Title { get; set; } = "";
        public List<ExpectedTrigger> Expected { get; set; } = new();
    }

    private sealed class DeadlineParseCase
    {
        public string? Raw { get; set; }
        public string? ExpectedDate { get; set; }
        public string? ExpectedTime { get; set; }
    }

    private sealed class DeadlineOverdueCase
    {
        public string Due { get; set; } = "";
        public string Now { get; set; } = "";
        public bool ExpectedOverdue { get; set; }
    }

    private sealed class RepeatCase
    {
        public string? CurrentDue { get; set; }
        public string Repeat { get; set; } = "";
        public string Today { get; set; } = "";
        public string? ExpectedNext { get; set; }
    }

    private sealed class CompletionMergeCase
    {
        public string Name { get; set; } = "";
        public List<CompletionLog.CompletionEntry> Current { get; set; } = new();
        public List<CompletionLog.CompletionEntry> Incoming { get; set; } = new();
        public int ExpectedAdded { get; set; }
        public List<CompletionLog.CompletionEntry> ExpectedEntries { get; set; } = new();
    }

    private static GoldenVectors LoadVectors()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "golden-sync-vectors.json");
            if (File.Exists(candidate))
            {
                var json = File.ReadAllText(candidate);
                return JsonSerializer.Deserialize<GoldenVectors>(json, StoreFile.JsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize golden-sync-vectors.json");
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate tests/golden-sync-vectors.json");
    }

    [Fact]
    public void HlcCompareVectorsMatch()
    {
        var vectors = LoadVectors();
        foreach (var c in vectors.HlcCompareCases)
        {
            Assert.Equal(c.ExpectedSign, Math.Sign(HlcClock.Compare(c.A, c.B)));
        }
    }

    [Fact]
    public void MergeVectorsMatchAcrossAllSixEntitiesAndConflicts()
    {
        var vectors = LoadVectors();
        foreach (var c in vectors.MergeCases)
        {
            var report = SyncMergeEngine.MergeStore(c.Local, c.Remote, c.LastSyncedRemoteHlc);
            var exp = c.Expected;

            Assert.Equal(exp.UpdatedCount, report.UpdatedCount);
            Assert.Equal(exp.ConflictCopiesCount, report.ConflictCopiesCount);
            Assert.Equal(exp.MaxRemoteHlc, report.MaxRemoteHlc);

            if (exp.PresetsIsNull)
                Assert.Null(report.MergedDto.Presets);
            else
                Assert.NotNull(report.MergedDto.Presets);

            if (exp.GoalTitle is not null)
                Assert.Equal(exp.GoalTitle, report.MergedDto.Goals[0].Title);

            Assert.Equal(exp.ScheduleCount, report.MergedDto.Schedules.Count);
            var primary = report.MergedDto.Schedules[0];
            Assert.Equal(exp.ScheduleTitle, primary.Title);
            Assert.Equal(exp.ScheduleDel, primary.Del);
            if (exp.ScheduleRemind is not null) Assert.Equal(exp.ScheduleRemind, primary.Remind);
            if (exp.ScheduleTagIds is not null) Assert.Equal(exp.ScheduleTagIds, primary.TagIds);
            if (exp.SchedulePersonIds is not null) Assert.Equal(exp.SchedulePersonIds, primary.PersonIds);
            if (exp.ScheduleLocationIds is not null) Assert.Equal(exp.ScheduleLocationIds, primary.LocationIds);

            if (exp.ConflictCopyTitle is not null)
            {
                var copy = report.MergedDto.Schedules[1];
                Assert.False(string.IsNullOrWhiteSpace(copy.Id));
                Assert.Equal(exp.ConflictCopyTitle, copy.Title);
                Assert.Equal(exp.ConflictCopyStart, copy.Start);
                Assert.False(copy.Del);
            }

            var gz = WebDavService.SerializeSnapshotGzip(new DeviceSnapshotDto
            {
                Version = 1,
                DeviceId = "win-test",
                SnapshotHlc = "019a0b1c9999-0000-win-test",
                Store = report.MergedDto,
                Completions = new()
            });
            var roundTripped = WebDavService.DeserializeSnapshotGzip(gz);
            Assert.Equal(report.MergedDto.Schedules.Count, roundTripped.Store.Schedules.Count);
            Assert.Equal(primary.Title, roundTripped.Store.Schedules[0].Title);
        }
    }

    [Fact]
    public void RemindDeadlineRepeatAndCompletionVectorsMatch()
    {
        var vectors = LoadVectors();

        foreach (var c in vectors.RemindNormalizeCases)
            Assert.Equal(c.Expected, RemindRules.Normalize(c.Raw, c.LegacyFallback));

        foreach (var c in vectors.RemindDeadlineDefaultCases)
            Assert.Equal(c.Expected, RemindRules.NormalizeForDeadlineDefault(c.Raw));

        foreach (var c in vectors.RemindTriggerCases)
        {
            var triggers = RemindRules.GetTriggers(
                c.Remind,
                TimeSpan.Parse(c.Start, CultureInfo.InvariantCulture),
                TimeSpan.Parse(c.End, CultureInfo.InvariantCulture),
                c.Title);
            Assert.Equal(c.Expected.Count, triggers.Count);
            for (var i = 0; i < c.Expected.Count; i++)
            {
                Assert.Equal(c.Expected[i].Node, triggers[i].Node);
                Assert.Equal(c.Expected[i].TargetTime, DeadlineRules.FormatClock(triggers[i].TargetTime));
                Assert.Equal(c.Expected[i].ToastTitle, triggers[i].ToastTitle);
                Assert.Equal(c.Expected[i].IsEnd, triggers[i].IsEnd);
            }
        }

        foreach (var c in vectors.DeadlineTriggerCases)
        {
            var triggers = RemindRules.GetDeadlineTriggers(
                c.Remind,
                TimeSpan.Parse(c.DueTime, CultureInfo.InvariantCulture),
                c.Title);
            Assert.Equal(c.Expected.Count, triggers.Count);
            for (var i = 0; i < c.Expected.Count; i++)
            {
                Assert.Equal(c.Expected[i].Node, triggers[i].Node);
                Assert.Equal(c.Expected[i].TargetTime, DeadlineRules.FormatClock(triggers[i].TargetTime));
                Assert.Equal(c.Expected[i].ToastTitle, triggers[i].ToastTitle);
                Assert.Equal(c.Expected[i].IsEnd, triggers[i].IsEnd);
            }
        }

        foreach (var c in vectors.DeadlineParseCases)
        {
            var (date, dueTime) = DeadlineRules.ParseDue(c.Raw);
            Assert.Equal(c.ExpectedDate, date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Assert.Equal(c.ExpectedTime, dueTime is { } t ? DeadlineRules.FormatClock(t) : null);
        }

        foreach (var c in vectors.DeadlineOverdueCases)
        {
            var (date, dueTime) = DeadlineRules.ParseDue(c.Due);
            var now = DateTime.Parse(c.Now, CultureInfo.InvariantCulture);
            Assert.Equal(c.ExpectedOverdue, DeadlineRules.IsOverdue(date, dueTime, now));
        }

        foreach (var c in vectors.RepeatCases)
        {
            DateTime? cur = c.CurrentDue is null ? null : DateTime.Parse(c.CurrentDue, CultureInfo.InvariantCulture);
            var today = DateTime.Parse(c.Today, CultureInfo.InvariantCulture);
            var next = RepeatRules.NextOccurrence(cur, c.Repeat, today);
            Assert.Equal(c.ExpectedNext, next?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var prevDataDir = Environment.GetEnvironmentVariable("DAYNEW_DATA_DIR");
        var tempDir = Path.Combine(Path.GetTempPath(), "DayNewSyncTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            Environment.SetEnvironmentVariable("DAYNEW_DATA_DIR", tempDir);

            foreach (var c in vectors.CompletionMergeCases)
            {
                var log = new CompletionLog();
                log.ReplaceAll(c.Current);
                var added = log.MergeEntries(c.Incoming);
                var actual = log.ReadAll();

                Assert.Equal(c.ExpectedAdded, added);
                Assert.Equal(c.ExpectedEntries.Count, actual.Count);
                for (var i = 0; i < c.ExpectedEntries.Count; i++)
                {
                    Assert.Equal(c.ExpectedEntries[i].Id, actual[i].Id);
                    Assert.Equal(c.ExpectedEntries[i].For, actual[i].For);
                    Assert.Equal(c.ExpectedEntries[i].At, actual[i].At);
                    Assert.Equal(c.ExpectedEntries[i].Done, actual[i].Done);
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DAYNEW_DATA_DIR", prevDataDir);
        }
    }
}
