using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.Avalonia.Attention;

/// <summary>
/// <c>[NSApp requestUserAttention:]</c>: the Dock icon bounces. It is the app's request, not the
/// window's, and macOS ignores it while the app is active.
/// </summary>
/// <remarks>Only the members that call the runtime are macOS-only, so the mapping is testable anywhere.</remarks>
internal sealed unsafe class MacAttention : IAttentionPlatform
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // NSRequestUserAttentionType
    internal const nuint NSCriticalRequest = 0;
    internal const nuint NSInformationalRequest = 10;

    // Each overload is one exact C prototype; see Hark.MacOS.ObjC for why that matters on arm64.
    [DllImport(LibObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(byte* name);

    [DllImport(LibObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector(byte* name);

    /// <summary><c>id (id, SEL)</c></summary>
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    /// <summary><c>NSInteger (id, SEL, NSUInteger)</c>, for <c>requestUserAttention:</c>.</summary>
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendRequest(IntPtr receiver, IntPtr selector, nuint type);

    /// <summary><c>void (id, SEL, NSInteger)</c>, for <c>cancelUserAttentionRequest:</c>.</summary>
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendCancel(IntPtr receiver, IntPtr selector, nint request);

    /// <summary>The request still running, or 0.</summary>
    private nint _request;

    public AttentionCapabilities Capabilities => AttentionCapabilities.Request | AttentionCapabilities.Critical | AttentionCapabilities.Cancel;

    [SupportedOSPlatform("macos")]
    public void Request(AttentionLevel level)
    {
        // A new request replaces this object's last one, so a critical one can be stepped down.
        Cancel();
        _request = SendRequest(App(), Sel("requestUserAttention:\0"u8), RequestType(level));
    }

    [SupportedOSPlatform("macos")]
    public void Cancel()
    {
        if (_request == 0)
            return;
        SendCancel(App(), Sel("cancelUserAttentionRequest:\0"u8), _request);
        _request = 0;
    }

    internal static nuint RequestType(AttentionLevel level) => level == AttentionLevel.Critical ? NSCriticalRequest : NSInformationalRequest;

    [SupportedOSPlatform("macos")]
    private static IntPtr App()
    {
        fixed (byte* name = "NSApplication\0"u8)
            return Send(GetClass(name), Sel("sharedApplication\0"u8));
    }

    [SupportedOSPlatform("macos")]
    private static IntPtr Sel(ReadOnlySpan<byte> nullTerminatedName)
    {
        fixed (byte* name = nullTerminatedName)
            return Selector(name);
    }
}
