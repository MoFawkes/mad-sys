using System.Globalization;
using System.Reflection;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AqiClock.Infrastructure.Cache;

public sealed class SqliteCacheDatabase : ILocalCache, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private readonly IReadOnlyList<string> _migrationScripts;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteCacheDatabase(IOptions<AqiClockOptions> options)
        : this(ResolvePath(options?.Value ?? throw new ArgumentNullException(nameof(options))), null)
    {
    }

    public SqliteCacheDatabase(string databasePath, IReadOnlyList<string>? migrationScripts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _migrationScripts = migrationScripts ?? LoadMigrations();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            try
            {
                await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
            {
                SqliteConnection.ClearAllPools();
                DeleteDatabaseFiles();
                await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WipeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles();
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReplaceSnapshotAsync(CacheSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string tableName = GetTableName(snapshot.Table);
            await ExecuteAsync(connection, transaction, $"DELETE FROM {tableName};", cancellationToken).ConfigureAwait(false);
            foreach (object row in snapshot.Rows)
            {
                await InsertRowAsync(connection, transaction, snapshot.Table, row, cancellationToken).ConfigureAwait(false);
            }

            await ExecuteAsync(connection, transaction,
                "INSERT INTO sync_state(table_name,last_synced_at) VALUES($table,$at) ON CONFLICT(table_name) DO UPDATE SET last_synced_at=excluded.last_synced_at;",
                cancellationToken, ("$table", tableName), ("$at", Format(snapshot.SyncedAt))).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<string?> GetMetaAsync(string key, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    public async Task SetMetaAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null,
            "INSERT INTO meta(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;",
            cancellationToken, ("$key", key), ("$value", value)).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetLastSyncedAtAsync(CacheTable table, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT last_synced_at FROM sync_state WHERE table_name=$table;";
        command.Parameters.AddWithValue("$table", GetTableName(table));
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null;
    }

    internal async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            string? result = await integrity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQLite integrity check failed.");
            }
        }

        await ExecuteAsync(connection, null, "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);", cancellationToken).ConfigureAwait(false);
        int version = await ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        for (int index = version; index < _migrationScripts.Count; index++)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, _migrationScripts[index], cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction,
                "INSERT INTO meta(key,value) VALUES('schema_version',$version) ON CONFLICT(key) DO UPDATE SET value=excluded.value;",
                cancellationToken, ("$version", (index + 1).ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_migrationScripts.Count != CurrentSchemaVersion)
        {
            throw new InvalidOperationException("Embedded cache migration count does not match the current schema version.");
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key='schema_version';";
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text ? int.Parse(text, CultureInfo.InvariantCulture) : 0;
    }

    private static async Task InsertRowAsync(SqliteConnection connection, SqliteTransaction transaction, CacheTable table, object row, CancellationToken cancellationToken)
    {
        string sql;
        (string, object?)[] values;
        switch (table, row)
        {
            case (CacheTable.Organizations, OrganizationRow x):
                sql = "INSERT INTO organizations VALUES($id,$name,$timezone);"; values = [("$id", x.Id), ("$name", x.Name), ("$timezone", x.Timezone)]; break;
            case (CacheTable.Profiles, ProfileRow x):
                sql = "INSERT INTO profiles VALUES($id,$name,$role,$active);"; values = [("$id", x.Id), ("$name", x.DisplayName), ("$role", x.Role), ("$active", x.IsActive ? 1 : 0)]; break;
            case (CacheTable.Timetables, TimetableRow x):
                sql = "INSERT INTO timetables VALUES($id,$name,$archived);"; values = [("$id", x.Id), ("$name", x.Name), ("$archived", x.IsArchived ? 1 : 0)]; break;
            case (CacheTable.Periods, PeriodRow x):
                sql = "INSERT INTO periods VALUES($id,$timetable,$name,$start,$end,$sort,$lesson);"; values = [("$id", x.Id), ("$timetable", x.TimetableId), ("$name", x.Name), ("$start", x.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)), ("$end", x.EndTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)), ("$sort", x.SortOrder), ("$lesson", x.IsLesson ? 1 : 0)]; break;
            case (CacheTable.WeekSchedule, WeekScheduleRow x):
                sql = "INSERT INTO week_schedule VALUES($weekday,$timetable);"; values = [("$weekday", x.Weekday), ("$timetable", x.TimetableId)]; break;
            case (CacheTable.DateOverrides, DateOverrideRow x):
                sql = "INSERT INTO date_overrides VALUES($id,$date,$timetable,$note);"; values = [("$id", x.Id), ("$date", x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$timetable", x.TimetableId), ("$note", x.Note)]; break;
            case (CacheTable.Announcements, AnnouncementRow x):
                sql = "INSERT INTO announcements VALUES($id,$title,$body,$expires,$creator,$created);"; values = [("$id", x.Id), ("$title", x.Title), ("$body", x.Body), ("$expires", x.ExpiresAt is null ? null : Format(x.ExpiresAt.Value)), ("$creator", x.CreatedBy), ("$created", Format(x.CreatedAt))]; break;
            default: throw new ArgumentException($"Row type {row.GetType().Name} is invalid for {table}.", nameof(row));
        }

        await ExecuteAsync(connection, transaction, sql, cancellationToken, values).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string GetTableName(CacheTable table) => table switch
    {
        CacheTable.Organizations => "organizations",
        CacheTable.Profiles => "profiles",
        CacheTable.Timetables => "timetables",
        CacheTable.Periods => "periods",
        CacheTable.WeekSchedule => "week_schedule",
        CacheTable.DateOverrides => "date_overrides",
        CacheTable.Announcements => "announcements",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    private static string[] LoadMigrations()
    {
        Assembly assembly = typeof(SqliteCacheDatabase).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(static name => name.Contains(".Cache.Migrations.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => { using Stream stream = assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); })
            .ToArray();
    }

    private void DeleteDatabaseFiles()
    {
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            string path = DatabasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string ResolvePath(AqiClockOptions options)
    {
        string directory = string.IsNullOrWhiteSpace(options.DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqiClock")
            : options.DataDirectory;
        return Path.Combine(directory, "cache.db");
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public void Dispose() => _gate.Dispose();
}
