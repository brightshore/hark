using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.MacOS;

/// <summary>
/// The slice of the Objective-C runtime Hark needs, called directly: no binding library, no
/// Objective-C compiled at build time.
/// </summary>
/// <remarks>
/// <para>
/// <c>objc_msgSend</c> has no fixed signature: each call site casts it to the exact C prototype of
/// the method it sends, and on arm64 a mismatch is undefined behaviour, not a marshalling error. So
/// each <c>Send</c> overload below names one prototype, and every caller picks the one whose
/// argument and return types match the selector's declaration in the SDK headers. <c>id</c>,
/// <c>SEL</c>, <c>Class</c> and pointers are <see cref="IntPtr"/>; <c>NSUInteger</c> is
/// <see cref="nuint"/>; <c>BOOL</c> is a byte.
/// </para>
/// <para>
/// An Objective-C exception that unwinds into managed code kills the process. Callers only send
/// messages documented not to raise, after checking the preconditions (an app bundle, a selector
/// the OS version has) that would.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
internal static unsafe class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    private static readonly IntPtr s_msgSend = NativeLibrary.GetExport(NativeLibrary.Load(LibObjC), "objc_msgSend");

    [DllImport(LibObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClassCore(byte* name);

    [DllImport(LibObjC, EntryPoint = "objc_getProtocol")]
    private static extern IntPtr GetProtocolCore(byte* name);

    [DllImport(LibObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr RegisterSelector(byte* name);

    [DllImport(LibObjC, EntryPoint = "objc_allocateClassPair")]
    private static extern IntPtr AllocateClassPairCore(IntPtr superclass, byte* name, nuint extraBytes);

    [DllImport(LibObjC, EntryPoint = "objc_registerClassPair")]
    public static extern void RegisterClassPair(IntPtr cls);

    [DllImport(LibObjC, EntryPoint = "class_addMethod")]
    private static extern byte AddMethodCore(IntPtr cls, IntPtr selector, IntPtr implementation, byte* types);

    [DllImport(LibObjC, EntryPoint = "class_addProtocol")]
    public static extern byte AddProtocol(IntPtr cls, IntPtr protocol);

    [DllImport(LibObjC, EntryPoint = "objc_autoreleasePoolPush")]
    public static extern IntPtr AutoreleasePoolPush();

    [DllImport(LibObjC, EntryPoint = "objc_autoreleasePoolPop")]
    public static extern void AutoreleasePoolPop(IntPtr pool);

    [DllImport(LibObjC, EntryPoint = "objc_retain")]
    public static extern IntPtr Retain(IntPtr obj);

    [DllImport(LibObjC, EntryPoint = "objc_release")]
    public static extern void Release(IntPtr obj);

    public static IntPtr GetClass(string name) => WithUtf8(name, static p => GetClassCore((byte*)p));

    public static IntPtr GetProtocol(string name) => WithUtf8(name, static p => GetProtocolCore((byte*)p));

    public static IntPtr Selector(string name) => WithUtf8(name, static p => RegisterSelector((byte*)p));

    public static IntPtr AllocateClassPair(IntPtr superclass, string name) => WithUtf8(name, p => AllocateClassPairCore(superclass, (byte*)p, 0));

    public static bool AddMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types)
        => WithUtf8(types, p => AddMethodCore(cls, selector, implementation, (byte*)p) != 0);

    // ---- objc_msgSend, one overload per prototype ----

    /// <summary><c>id (id, SEL)</c></summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)s_msgSend)(receiver, selector);

    /// <summary><c>id (id, SEL, id)</c></summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)s_msgSend)(receiver, selector, a);

    /// <summary><c>id (id, SEL, id, id)</c></summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)s_msgSend)(receiver, selector, a, b);

    /// <summary><c>id (id, SEL, id, id, id)</c></summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)s_msgSend)(receiver, selector, a, b, c);

    /// <summary><c>id (id, SEL, id, id, NSUInteger)</c></summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, nuint c)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, nuint, IntPtr>)s_msgSend)(receiver, selector, a, b, c);

    /// <summary><c>id (id, SEL, id, id, id, NSUInteger)</c></summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c, nuint d)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, nuint, IntPtr>)s_msgSend)(receiver, selector, a, b, c, d);

    /// <summary><c>id (id, SEL, id, id, id, NSError**)</c></summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c, IntPtr* error)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr*, IntPtr>)s_msgSend)(receiver, selector, a, b, c, error);

    /// <summary><c>id (Class, SEL, const id *, NSUInteger)</c>, for <c>arrayWithObjects:count:</c>.</summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr* objects, nuint count)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, nuint, IntPtr>)s_msgSend)(receiver, selector, objects, count);

    /// <summary><c>id (Class, SEL, const char *)</c>, for <c>stringWithUTF8String:</c>.</summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector, byte* utf8)
        => ((delegate* unmanaged<IntPtr, IntPtr, byte*, IntPtr>)s_msgSend)(receiver, selector, utf8);

    /// <summary><c>void (id, SEL, id)</c></summary>
    public static void SendVoid(IntPtr receiver, IntPtr selector, IntPtr a)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)s_msgSend)(receiver, selector, a);

    /// <summary><c>void (id, SEL, id, block)</c></summary>
    public static void SendVoid(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr block)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)s_msgSend)(receiver, selector, a, block);

    /// <summary><c>void (id, SEL, NSUInteger)</c></summary>
    public static void SendVoid(IntPtr receiver, IntPtr selector, nuint a)
        => ((delegate* unmanaged<IntPtr, IntPtr, nuint, void>)s_msgSend)(receiver, selector, a);

    /// <summary><c>void (id, SEL, NSUInteger, block)</c></summary>
    public static void SendVoid(IntPtr receiver, IntPtr selector, nuint a, IntPtr block)
        => ((delegate* unmanaged<IntPtr, IntPtr, nuint, IntPtr, void>)s_msgSend)(receiver, selector, a, block);

    /// <summary><c>BOOL (id, SEL, id)</c>; also fits <c>BOOL (id, SEL, SEL)</c>.</summary>
    public static bool SendBool(IntPtr receiver, IntPtr selector, IntPtr a)
        => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)s_msgSend)(receiver, selector, a) != 0;

    // ---- Foundation conveniences. All results are autoreleased: call inside a pool. ----

    public static IntPtr NSString(string value)
    {
        var nsString = GetClass("NSString");
        var selector = Selector("stringWithUTF8String:");
        // Interior NULs would truncate the C string; nothing Hark sends needs them.
        var bytes = System.Text.Encoding.UTF8.GetBytes(value.Replace('\0', ' ') + "\0");
        fixed (byte* p = bytes)
            return Send(nsString, selector, p);
    }

    public static string? ToManagedString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
            return null;
        return Marshal.PtrToStringUTF8(Send(nsString, Selector("UTF8String")));
    }

    public static IntPtr NSArray(ReadOnlySpan<IntPtr> objects)
    {
        fixed (IntPtr* p = objects)
            return Send(GetClass("NSArray"), Selector("arrayWithObjects:count:"), p, (nuint)objects.Length);
    }

    public static IntPtr NSSet(ReadOnlySpan<IntPtr> objects)
        => Send(GetClass("NSSet"), Selector("setWithArray:"), NSArray(objects));

    /// <summary>The <c>localizedDescription</c> of an <c>NSError</c>.</summary>
    public static string? ErrorDescription(IntPtr error)
        => error == IntPtr.Zero ? null : ToManagedString(Send(error, Selector("localizedDescription")));

    public static bool RespondsTo(IntPtr receiver, string selector)
        => SendBool(receiver, Selector("respondsToSelector:"), Selector(selector));

    /// <summary>Runs <paramref name="body"/> inside an autorelease pool, which pool threads do not have.</summary>
    public static T InPool<T>(Func<T> body)
    {
        var pool = AutoreleasePoolPush();
        try
        {
            return body();
        }
        finally
        {
            AutoreleasePoolPop(pool);
        }
    }

    public static void InPool(Action body) => InPool(() => { body(); return 0; });

    private static T WithUtf8<T>(string value, Func<IntPtr, T> body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* p = bytes)
            return body((IntPtr)p);
    }
}
