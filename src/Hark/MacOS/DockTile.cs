using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.MacOS;

/// <summary>
/// The Dock tile surface Hark uses. <see cref="MacBadgeManager"/> talks only to this, so it is
/// testable on any OS; <see cref="DockTile"/> is the one real implementation.
/// </summary>
internal interface IDockTile
{
    /// <summary>Sets the tile's badge; <c>null</c> removes it.</summary>
    void SetBadgeLabel(string? label);
}

/// <summary>
/// The AppKit calls behind the Dock tile's badge: the ways AppKit offers of setting it, and whether the
/// caller is on the main thread. <see cref="DockTile"/> decides which to use, so that decision is
/// testable on any OS. <see cref="AppKitDockTile"/> is the one real implementation.
/// </summary>
internal interface IAppKitDockTile
{
    /// <summary><c>[NSThread isMainThread]</c>.</summary>
    bool IsMainThread { get; }

    /// <summary><c>setBadgeLabel:</c>, on the calling thread, at once. AppKit allows it only on the main thread.</summary>
    void SetBadgeLabelNow(string? label);

    /// <summary>
    /// <c>performSelectorOnMainThread:withObject:waitUntilDone:NO</c> with <c>setBadgeLabel:</c>: the main
    /// run loop makes the call on a later turn, after every call queued before it, from whichever thread.
    /// </summary>
    void QueueBadgeLabel(string? label);
}

/// <summary>
/// <see cref="IDockTile"/> over <c>[NSApp dockTile]</c>. Unlike notifications, the Dock tile needs no
/// app bundle: any process with an <c>NSApplication</c> and a Dock icon has one.
/// </summary>
/// <remarks>
/// Every change is queued on the main run loop, even one made on the main thread. Setting it at once
/// there would let it overtake changes other threads had already queued: a clear made on the UI thread
/// would land first, and an earlier count from a pool thread would then put the badge back. Queued,
/// changes apply in the order they were made. The caller does not wait for the run loop, which would
/// deadlock if the main thread were itself waiting on the caller, so the change shows on the loop's
/// next turn.
/// </remarks>
internal sealed class DockTile(IAppKitDockTile appKit) : IDockTile
{
    [SupportedOSPlatform("macos")]
    public static DockTile? TryCreate(out string? unsupportedReason)
    {
        var appKit = AppKitDockTile.TryCreate(out unsupportedReason);
        return appKit is null ? null : new DockTile(appKit);
    }

    public void SetBadgeLabel(string? label) => appKit.QueueBadgeLabel(label);
}

/// <summary><see cref="IAppKitDockTile"/> over <c>[NSApp dockTile]</c>, through the Objective-C runtime.</summary>
[SupportedOSPlatform("macos")]
internal sealed class AppKitDockTile : IAppKitDockTile
{
    private const string AppKitPath = "/System/Library/Frameworks/AppKit.framework/AppKit";

    private readonly IntPtr _tile;

    private AppKitDockTile(IntPtr tile) => _tile = tile;

    /// <summary>
    /// Returns the tile, or null with the reason there is none. <c>NSApp</c> is read rather than
    /// <c>sharedApplication</c> called: creating an application object is the UI framework's job, and a
    /// process without one has no Dock icon to badge.
    /// </summary>
    public static AppKitDockTile? TryCreate(out string? unsupportedReason)
    {
        unsupportedReason = null;
        if (!NativeLibrary.TryLoad(AppKitPath, out var appKit) || !NativeLibrary.TryGetExport(appKit, "NSApp", out var nsAppVariable))
        {
            unsupportedReason = "AppKit could not be loaded.";
            return null;
        }

        var app = Marshal.ReadIntPtr(nsAppVariable);
        if (app == IntPtr.Zero)
        {
            unsupportedReason = "This process has no NSApplication yet, so it has no Dock icon to badge. "
                                + "Create the badge manager after the app's UI framework has started.";
            return null;
        }

        var tile = ObjC.InPool(() => ObjC.Retain(ObjC.Send(app, ObjC.Selector("dockTile"))));
        if (tile == IntPtr.Zero)
        {
            unsupportedReason = "The application has no Dock tile.";
            return null;
        }
        return new AppKitDockTile(tile);
    }

    public bool IsMainThread => ObjC.SendBool(ObjC.GetClass("NSThread"), ObjC.Selector("isMainThread"));

    public void SetBadgeLabelNow(string? label) => ObjC.InPool(() =>
        ObjC.SendVoid(_tile, ObjC.Selector("setBadgeLabel:"), label is null ? IntPtr.Zero : ObjC.NSString(label)));

    // The run loop retains the tile and the label until it has made the call.
    public void QueueBadgeLabel(string? label) => ObjC.InPool(() =>
        ObjC.SendVoid(_tile, ObjC.Selector("performSelectorOnMainThread:withObject:waitUntilDone:"),
            ObjC.Selector("setBadgeLabel:"), label is null ? IntPtr.Zero : ObjC.NSString(label), false));
}
