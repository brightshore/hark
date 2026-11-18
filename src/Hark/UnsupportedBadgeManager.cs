namespace Hark;

/// <summary>Stands in where there is no badge, carrying the reason.</summary>
internal sealed class UnsupportedBadgeManager(string reason) : IBadgeManager
{
    public bool IsSupported => false;
    public string? UnsupportedReason { get; } = reason;
    public BadgeCapabilities Capabilities => BadgeCapabilities.None;

    public Task SetCountAsync(int count, CancellationToken cancellationToken = default) => throw Unsupported();

    public Task SetTextAsync(string? text, CancellationToken cancellationToken = default) => throw Unsupported();

    public Task SetProgressAsync(double? progress, CancellationToken cancellationToken = default) => throw Unsupported();

    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private InvalidOperationException Unsupported() => new($"The app badge is not supported: {UnsupportedReason}");
}
