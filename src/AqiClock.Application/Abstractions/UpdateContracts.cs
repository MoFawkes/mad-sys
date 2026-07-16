namespace AqiClock.Application.Abstractions;

public enum UpdateStatus
{
    Disabled,
    Checking,
    UpToDate,
    Downloaded,
    Failed,
}

public sealed record UpdateState(UpdateStatus Status, string? TargetVersion = null)
{
    public string DisplayText => Status switch
    {
        UpdateStatus.Disabled => "Updates unavailable in this build",
        UpdateStatus.Checking => "Checking for updates…",
        UpdateStatus.UpToDate => "Up to date",
        UpdateStatus.Downloaded when !string.IsNullOrWhiteSpace(TargetVersion) => $"Update downloaded — restarts into v{TargetVersion}",
        UpdateStatus.Downloaded => "Update downloaded — applies on restart",
        _ => "Update check unavailable",
    };
}

public interface IUpdateService : IDisposable
{
    UpdateState Current { get; }

    event EventHandler<UpdateState>? StateChanged;

    void Start();

    void PrepareUpdateOnExit();
}
