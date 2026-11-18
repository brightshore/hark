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
/// <see cref="IDockTile"/> over <c>[NSApp dockTile]</c>. Unlike notifications, the Dock tile needs no
/// app bundle: any process with an <c>NSApplication</c> and a Dock icon has one.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class DockTile : IDockTile
{
    private const string AppKitPath = "/System/Library/Frameworks/AppKit.framework/AppKit";

    private readonly IntPtr _tile;

    private DockTile(IntPtr tile) => _tile = tile;

    /// <summary>
    /// Returns the tile, or null with the reason there is none. <c>NSApp</c> is read rather than
    /// <c>sharedApplication</c> called: creating an application object is the UI framework's job, and a
    /// process without one has no Dock icon to badge.
    /// </summary>
    public static DockTile? TryCreate(out string? unsupportedReason)
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
        return new DockTile(tile);
    }

    public void SetBadgeLabel(string? label) => ObjC.InPool(() =>
    {
        var value = label is null ? IntPtr.Zero : ObjC.NSString(label);
        if (ObjC.SendBool(ObjC.GetClass("NSThread"), ObjC.Selector("isMainThread")))
        {
            ObjC.SendVoid(_tile, ObjC.Selector("setBadgeLabel:"), value);
            return;
        }

        // AppKit belongs to the main thread. The main run loop makes the call, and the tile and the
        // label are retained until it has.
        ObjC.SendVoid(_tile, ObjC.Selector("performSelectorOnMainThread:withObject:waitUntilDone:"),
            ObjC.Selector("setBadgeLabel:"), value, false);
    });
}
