using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.Windows;

/// <summary>
/// The slice of WinRT Hark needs, called through raw COM vtables: no CsWinRT projection, no
/// Windows-only target framework, so a plain <c>net10.0</c> app gets the backend too.
/// </summary>
/// <remarks>
/// Every WinRT interface starts with IInspectable's six slots (QueryInterface, AddRef, Release,
/// GetIids, GetRuntimeClassName, GetTrustLevel), so an interface's first own method is slot 6.
/// The IIDs and slot orders used across this folder were checked against the IDL Wine ships for
/// these namespaces, not written from memory.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe class WinRT
{
    public const int S_OK = 0;
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int InspectableSlots = 6;

    public static readonly Guid IID_IUnknown = new("00000000-0000-0000-c000-000000000046");
    public static readonly Guid IID_IAgileObject = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString(char* sourceString, uint length, IntPtr* hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern char* WindowsGetStringRawBuffer(IntPtr hstring, uint* length);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, Guid* iid, IntPtr* factory);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoActivateInstance(IntPtr activatableClassId, IntPtr* instance);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    /// <summary>
    /// Calls into WinRT bypass the runtime's own COM setup, so a thread-pool thread may have none.
    /// MTA is what such a thread would get anyway; an STA thread (a UI thread) answers
    /// RPC_E_CHANGED_MODE and is already initialised, which is just as good.
    /// </summary>
    public static void EnsureComInitialized() => CoInitializeEx(IntPtr.Zero, 0 /* COINIT_MULTITHREADED */);

    public static void Check(int hr, string operation)
    {
        if (hr < 0)
            throw new InvalidOperationException($"{operation} failed with HRESULT 0x{hr:X8}.", Marshal.GetExceptionForHR(hr));
    }

    /// <summary>Slot <paramref name="slot"/> of <paramref name="obj"/>'s vtable.</summary>
    public static void* Slot(IntPtr obj, int slot) => (*(void***)obj)[slot];

    public static IntPtr QueryInterface(IntPtr obj, Guid iid)
    {
        IntPtr result;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(obj, 0))(obj, &iid, &result);
        Check(hr, $"QueryInterface({iid})");
        return result;
    }

    public static void AddRef(IntPtr obj) => ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 1))(obj);

    public static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 2))(obj);
    }

    public static IntPtr GetActivationFactory(string runtimeClass, Guid iid)
    {
        using var name = new HString(runtimeClass);
        IntPtr factory;
        Check(RoGetActivationFactory(name.Handle, &iid, &factory), $"RoGetActivationFactory({runtimeClass})");
        return factory;
    }

    public static IntPtr ActivateInstance(string runtimeClass)
    {
        using var name = new HString(runtimeClass);
        IntPtr instance;
        Check(RoActivateInstance(name.Handle, &instance), $"RoActivateInstance({runtimeClass})");
        return instance;
    }

    /// <summary>Copies an HSTRING the caller owns into a managed string, then deletes it.</summary>
    public static string TakeString(IntPtr hstring)
    {
        if (hstring == IntPtr.Zero)
            return "";
        try
        {
            uint length;
            var buffer = WindowsGetStringRawBuffer(hstring, &length);
            return new string(buffer, 0, (int)length);
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    /// <summary>An HSTRING Hark owns for the length of a call.</summary>
    public readonly struct HString : IDisposable
    {
        public IntPtr Handle { get; }

        public HString(string value)
        {
            IntPtr handle;
            fixed (char* p = value)
                Check(WindowsCreateString(p, (uint)value.Length, &handle), "WindowsCreateString");
            Handle = handle;
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                WindowsDeleteString(Handle);
        }
    }
}
