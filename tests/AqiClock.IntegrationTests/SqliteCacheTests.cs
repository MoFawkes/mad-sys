using AqiClock.Application.Abstractions;
using AqiClock.Infrastructure.Cache;

namespace AqiClock.IntegrationTests;

public sealed class SqliteCacheTests : IDisposable
{
    private static readonly int[] AtomicSnapshotCounts = [25, 50];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AqiClock.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeCreatesSchemaAndIsIdempotent()
    {
        using var database = CreateDatabase();
        await database.InitializeAsync();
        await database.InitializeAsync();

        Assert.Equal("1", await database.GetMetaAsync("schema_version"));
    }

    [Fact]
    public async Task InitializeRecoversFromCorruptCache()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "cache.db");
        await File.WriteAllTextAsync(path, "not a sqlite database");
        using var database = new SqliteCacheDatabase(path);

        await database.InitializeAsync();

        Assert.Equal("1", await database.GetMetaAsync("schema_version"));
    }

    [Fact]
    public async Task SnapshotReplaceIsAtomicForReaders()
    {
        using var database = CreateDatabase();
        await database.InitializeAsync();
        var repository = new SqliteProfileRepository(database);
        Guid organizationId = Guid.NewGuid();
        ProfileRow[] oldRows = Enumerable.Range(0, 25).Select(index => new ProfileRow(Guid.NewGuid(), organizationId, $"Old {index}", "staff", true)).ToArray();
        ProfileRow[] newRows = Enumerable.Range(0, 50).Select(index => new ProfileRow(Guid.NewGuid(), organizationId, $"New {index}", "staff", true)).ToArray();
        await database.ReplaceSnapshotAsync(Snapshot(CacheTable.Profiles, oldRows));

        Task replace = database.ReplaceSnapshotAsync(Snapshot(CacheTable.Profiles, newRows));
        var counts = new List<int>();
        while (!replace.IsCompleted)
        {
            counts.Add((await repository.GetAllAsync()).Count);
        }
        await replace;
        counts.Add((await repository.GetAllAsync()).Count);

        Assert.All(counts, count => Assert.Contains(count, AtomicSnapshotCounts));
        Assert.Equal(50, counts[^1]);
    }

    [Fact]
    public async Task NotificationLogPrunesEntriesOlderThanCutoff()
    {
        using var database = CreateDatabase();
        await database.InitializeAsync();
        var store = new SqliteNotificationLogStore(database);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await store.RecordAsync("old", now.AddDays(-8), false);
        await store.RecordAsync("new", now.AddDays(-1), false);

        await store.PruneAsync(now.AddDays(-7));

        Assert.False(await store.ContainsAsync("old"));
        Assert.True(await store.ContainsAsync("new"));
    }

    [Fact]
    public async Task NotificationLogReturnsTimestampAndCanRemoveStableKey()
    {
        using var database = CreateDatabase();
        await database.InitializeAsync();
        var store = new SqliteNotificationLogStore(database);
        DateTimeOffset firedAt = DateTimeOffset.UtcNow;
        await store.RecordAsync("start:period:date", firedAt, false);

        NotificationLogEntry entry = Assert.IsType<NotificationLogEntry>(await store.GetAsync("start:period:date"));
        Assert.Equal(firedAt, entry.FiredAt);
        Assert.False(entry.Skipped);

        await store.RemoveAsync(entry.EventKey);
        Assert.Null(await store.GetAsync(entry.EventKey));
    }

    [Fact]
    public async Task RepositoriesMapIsoValuesAndWeekdayConvention()
    {
        using var database = CreateDatabase();
        await database.InitializeAsync();
        Guid organizationId = Guid.NewGuid();
        Guid timetableId = Guid.NewGuid();
        await database.ReplaceSnapshotAsync(Snapshot(CacheTable.Timetables, new[] { new TimetableRow(timetableId, organizationId, "Normal", false) }));
        await database.ReplaceSnapshotAsync(Snapshot(CacheTable.Periods, new[] { new PeriodRow(Guid.NewGuid(), timetableId, "Maths", new TimeOnly(9, 0), new TimeOnly(10, 0), 1, true) }));
        await database.ReplaceSnapshotAsync(Snapshot(CacheTable.WeekSchedule, new[] { new WeekScheduleRow(Guid.NewGuid(), organizationId, 0, timetableId) }));

        IReadOnlyList<AqiClock.Domain.Entities.Timetable> timetables = await new SqliteTimetableRepository(database).GetAllAsync();
        AqiClock.Domain.Entities.WeekSchedule schedule = await new SqliteWeekScheduleRepository(database).GetAsync();

        Assert.Equal(new TimeOnly(9, 0), Assert.Single(Assert.Single(timetables).Periods).StartTime);
        Assert.Equal(timetableId, schedule.TimetableIdFor(DayOfWeek.Monday));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private SqliteCacheDatabase CreateDatabase() => new(Path.Combine(_directory, "cache.db"));
    private static CacheSnapshot Snapshot<T>(CacheTable table, IEnumerable<T> rows) where T : notnull => new(table, rows.Cast<object>().ToArray(), DateTimeOffset.UtcNow);
}
