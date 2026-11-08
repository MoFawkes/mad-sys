using AqiClock.Domain.Entities;

namespace AqiClock.Application.Abstractions;

public interface IRecurringReminderRepository
{
    Task<IReadOnlyList<RecurringReminder>> GetAllAsync(CancellationToken cancellationToken = default);
}
