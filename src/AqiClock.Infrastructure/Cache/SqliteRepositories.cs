using System.Globalization;
using AqiClock.Application.Abstractions;
using AqiClock.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace AqiClock.Infrastructure.Cache;

public sealed class SqliteTimetableRepository(SqliteCacheDatabase database) : ITimetableRepository
{
    public async Task<IReadOnlyList<Timetable>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var periods = new Dictionary<Guid, List<Period>>();
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,timetable_id,name,start_time,end_time,sort_order,is_lesson FROM periods ORDER BY sort_order;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid timetableId = Guid.Parse(reader.GetString(1));
                if (!periods.TryGetValue(timetableId, out List<Period>? list)) periods[timetableId] = list = [];
                list.Add(new Period(Guid.Parse(reader.GetString(0)), reader.GetString(2), ParseTime(reader.GetString(3)), ParseTime(reader.GetString(4)), reader.GetInt32(5), reader.GetInt64(6) != 0));
            }
        }

        var result = new List<Timetable>();
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,name,is_archived FROM timetables ORDER BY name;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid id = Guid.Parse(reader.GetString(0));
                result.Add(new Timetable(id, reader.GetString(1), reader.GetInt64(2) != 0, periods.TryGetValue(id, out List<Period>? list) ? list : []));
            }
        }

        return result;
    }

    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, "HH:mm:ss", CultureInfo.InvariantCulture);
}

public sealed class SqliteAnnouncementRepository(SqliteCacheDatabase database) : IAnnouncementRepository
{
    public async Task<IReadOnlyList<Announcement>> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var result = new List<Announcement>();
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,body,created_at,created_by,expires_at FROM announcements WHERE expires_at IS NULL OR expires_at>$now ORDER BY created_at DESC;";
        command.Parameters.AddWithValue("$now", Format(now));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new Announcement(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), ParseInstant(reader.GetString(3)), Guid.Parse(reader.GetString(4)), reader.IsDBNull(5) ? null : ParseInstant(reader.GetString(5))));
        }

        return result;
    }

    private static DateTimeOffset ParseInstant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

public sealed class SqliteWeekScheduleRepository(SqliteCacheDatabase database) : IWeekScheduleRepository
{
    public async Task<WeekSchedule> GetAsync(CancellationToken cancellationToken = default)
    {
        var assignments = new Dictionary<DayOfWeek, Guid?>();
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT weekday,timetable_id FROM week_schedule;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            int serverWeekday = reader.GetInt32(0);
            DayOfWeek day = (DayOfWeek)((serverWeekday + 1) % 7);
            assignments[day] = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1));
        }

        return new WeekSchedule(assignments);
    }

}

public sealed class SqliteDateOverrideRepository(SqliteCacheDatabase database) : IDateOverrideRepository
{
    public async Task<IReadOnlyList<DateOverride>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DateOverride>();
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,date,timetable_id,note FROM date_overrides ORDER BY date;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new DateOverride(Guid.Parse(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return result;
    }
}

public sealed class SqliteProfileRepository(SqliteCacheDatabase database) : IProfileRepository
{
    public async Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Profile>();
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,display_name,role,is_active FROM profiles ORDER BY display_name;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Map(reader));
        return result;
    }

    public async Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,display_name,role,is_active FROM profiles WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    private static Profile Map(SqliteDataReader reader) => new(Guid.Parse(reader.GetString(0)), reader.GetString(1), string.Equals(reader.GetString(2), "admin", StringComparison.OrdinalIgnoreCase) ? UserRole.Admin : UserRole.Staff, reader.GetInt64(3) != 0);
}

public sealed class SqliteNotificationLogStore(SqliteCacheDatabase database) : INotificationLogStore
{
    public async Task<bool> ContainsAsync(string eventKey, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT 1 FROM notification_log WHERE event_key=$key;"; command.Parameters.AddWithValue("$key", eventKey);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<NotificationLogEntry?> GetAsync(string eventKey, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT fired_at,skipped FROM notification_log WHERE event_key=$key;"; command.Parameters.AddWithValue("$key", eventKey);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        DateTimeOffset? firedAt = reader.IsDBNull(0) ? null : DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return new NotificationLogEntry(eventKey, firedAt, reader.GetInt64(1) != 0);
    }

    public async Task RecordAsync(string eventKey, DateTimeOffset? firedAt, bool skipped, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "INSERT OR REPLACE INTO notification_log(event_key,fired_at,skipped) VALUES($key,$at,$skipped);";
        command.Parameters.AddWithValue("$key", eventKey); command.Parameters.AddWithValue("$at", firedAt is null ? DBNull.Value : firedAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$skipped", skipped ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string eventKey, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "DELETE FROM notification_log WHERE event_key=$key;"; command.Parameters.AddWithValue("$key", eventKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "DELETE FROM notification_log WHERE fired_at IS NOT NULL AND fired_at<$cutoff;"; command.Parameters.AddWithValue("$cutoff", olderThan.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SqliteAnnouncementReadStore(SqliteCacheDatabase database) : IAnnouncementReadStore
{
    public async Task<bool> IsReadAsync(Guid announcementId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT 1 FROM announcement_read WHERE announcement_id=$id;"; command.Parameters.AddWithValue("$id", announcementId.ToString());
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task MarkReadAsync(Guid announcementId, DateTimeOffset readAt, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "INSERT OR REPLACE INTO announcement_read VALUES($id,$at);"; command.Parameters.AddWithValue("$id", announcementId.ToString()); command.Parameters.AddWithValue("$at", readAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
