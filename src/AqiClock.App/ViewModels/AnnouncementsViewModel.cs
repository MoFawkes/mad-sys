using System.Collections.ObjectModel;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Time;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.App.ViewModels;

public sealed record AnnouncementDisplay(Guid Id, string Title, string Body, string RelativeTime, string Poster, bool IsUnread);

public partial class AnnouncementsViewModel : ObservableObject, IRecipient<DataChanged>
{
    private readonly IAnnouncementRepository _repository;
    private readonly IAnnouncementReadStore _readStore;
    private readonly IProfileRepository _profiles;
    private readonly IClock _clock;
    private readonly IDeviceAudienceContext _audience;
    [ObservableProperty] private int _unreadCount;
    public ObservableCollection<AnnouncementDisplay> Items { get; } = [];

    public AnnouncementsViewModel(IAnnouncementRepository repository, IAnnouncementReadStore readStore, IProfileRepository profiles, IClock clock, IMessenger messenger)
        : this(repository, readStore, profiles, clock, messenger, new DeviceAudienceContext())
    {
    }

    public AnnouncementsViewModel(IAnnouncementRepository repository, IAnnouncementReadStore readStore, IProfileRepository profiles, IClock clock, IMessenger messenger, IDeviceAudienceContext audience)
    { _repository = repository; _readStore = readStore; _profiles = profiles; _clock = clock; _audience = audience; messenger.Register(this); }

    public async Task LoadAsync(bool markRead, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = new(_clock.Now);
        IReadOnlyList<Announcement> announcements = await _repository.GetCurrentAsync(now, cancellationToken);
        IReadOnlyList<Profile> profiles = await _profiles.GetAllAsync(cancellationToken);
        Dictionary<Guid, string> names = profiles.ToDictionary(x => x.Id, x => x.DisplayName);
        Items.Clear(); int unread = 0;
        foreach (Announcement item in announcements.Where(_audience.Matches).OrderByDescending(x => x.PublishAt ?? x.CreatedAt))
        {
            bool isRead = await _readStore.IsReadAsync(item.Id, cancellationToken);
            if (!isRead && markRead) { await _readStore.MarkReadAsync(item.Id, now, cancellationToken); isRead = true; }
            if (!isRead) unread++;
            Items.Add(new(item.Id, item.Title, item.Body, Relative(item.CreatedAt, now), names.GetValueOrDefault(item.CreatedBy, "Unknown"), !isRead));
        }
        UnreadCount = unread;
    }

    public void Receive(DataChanged message) { if (message.Table is CacheTable.Announcements or CacheTable.Profiles) _ = LoadAsync(false); }
    private static string Relative(DateTimeOffset value, DateTimeOffset now)
    { TimeSpan age = now - value; return age.TotalMinutes < 1 ? "just now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago" : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago" : $"{(int)age.TotalDays} d ago"; }
}
