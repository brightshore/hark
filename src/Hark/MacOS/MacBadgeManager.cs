using System.Globalization;
using System.Runtime.Versioning;

namespace Hark.MacOS;

/// <summary>
/// The Dock tile's badge, <c>NSApp.dockTile.badgeLabel</c>: any short text, a count included.
/// </summary>
/// <remarks>
/// Works from an unbundled process, unlike notifications. Every change, from any thread, is queued on
/// the main run loop (see <see cref="DockTile"/>), so changes apply in the order they were made, and the
/// returned task completes when the change is queued, not when the Dock shows it. No progress: the
/// Dock draws one only for a custom tile view.
/// </remarks>
internal sealed class MacBadgeManager : IBadgeManager
{
    private const BadgeCapabilities MacCapabilities = BadgeCapabilities.Count | BadgeCapabilities.Text;

    private readonly IDockTile _tile;
    private bool _disposed;

    private MacBadgeManager(IDockTile tile) => _tile = tile;

    public bool IsSupported => true;
    public string? UnsupportedReason => null;
    public BadgeCapabilities Capabilities => MacCapabilities;

    [SupportedOSPlatform("macos")]
    public static IBadgeManager Create()
    {
        var tile = DockTile.TryCreate(out var reason);
        return tile is null ? new UnsupportedBadgeManager(reason!) : Create(tile);
    }

    /// <summary>The seam the tests use.</summary>
    internal static MacBadgeManager Create(IDockTile tile) => new(tile);

    public Task SetCountAsync(int count, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BadgeValidation.ValidateCount(count);
        cancellationToken.ThrowIfCancellationRequested();

        _tile.SetBadgeLabel(count == 0 ? null : count.ToString(CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }

    public Task SetTextAsync(string? text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _tile.SetBadgeLabel(string.IsNullOrEmpty(text) ? null : text);
        return Task.CompletedTask;
    }

    public Task SetProgressAsync(double? progress, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BadgeValidation.RequireCapability(MacCapabilities, BadgeCapabilities.Progress);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _tile.SetBadgeLabel(null);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
