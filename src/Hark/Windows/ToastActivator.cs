using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Hark.Windows;

/// <summary>
/// The app's toast activator: an out-of-process COM server implementing
/// <c>INotificationActivationCallback</c>, which is how Windows delivers a toast click to an
/// unpackaged app, including after it has exited.
/// </summary>
/// <remarks>
/// <para>
/// Three registrations, all per user (HKCU) and no admin:
/// the AUMID's <c>CustomActivator</c> names the CLSID; the CLSID's <c>LocalServer32</c> is the
/// command that starts the app; and at run time the class object is registered with COM. A click
/// while the app runs is a COM call into it. A click after it exits starts it (Windows appends
/// <c>-Embedding</c> to the command), waits for the class object, then calls in.
/// </para>
/// <para>
/// The class object is registered from a dedicated thread in the multithreaded apartment that
/// lives as long as the process. That keeps calls off whichever thread created the manager, which
/// in an Avalonia app is the UI thread, and keeps the apartment, and with it the registration, alive.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe class ToastActivator
{
    private static readonly Guid IID_IClassFactory = new("00000001-0000-0000-c000-000000000046");
    private static readonly Guid IID_INotificationActivationCallback = new("53e31837-6600-4a81-9395-75cffe746f94");

    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoRegisterClassObject(Guid* clsid, IntPtr unknown, uint context, uint flags, uint* cookie);

    // Both COM objects are process-lifetime singletons, so reference counting is a formality.
    private static readonly IntPtr s_factory = CreateObject(
        (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&FactoryQueryInterface,
        (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)&CreateInstance,
        (delegate* unmanaged[Stdcall]<IntPtr, int, int>)&LockServer);

    private static readonly IntPtr s_callback = CreateObject(
        (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&CallbackQueryInterface,
        (delegate* unmanaged[Stdcall]<IntPtr, char*, char*, IntPtr, uint, int>)&Activate);

    private static readonly Lock s_gate = new();
    private static Guid s_registered;
    private static Action<string>? s_onActivated;

    /// <summary>
    /// Writes the registry entries for <paramref name="appId"/> and registers the class object.
    /// Returns null on success, or why the activator is unavailable; toasts still work without it,
    /// just not clicks after exit. <paramref name="onActivated"/> receives each click's arguments.
    /// </summary>
    public static string? Register(string appId, Action<string> onActivated)
    {
        var clsid = ToastArguments.ActivatorClsid(appId);
        lock (s_gate)
        {
            s_onActivated = onActivated;
            if (s_registered == clsid)
                return null;
            if (s_registered != Guid.Empty)
                return "A toast activator is already registered in this process for another app id.";

            var command = LaunchCommand();
            if (command is null)
                return "The process has no launchable path for Windows to start it with.";

            try
            {
                using (var server = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{clsid:B}\LocalServer32"))
                    server.SetValue(null, command);
                using (var aumid = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{appId}"))
                    aumid.SetValue("CustomActivator", clsid.ToString("B"));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                return $"Could not register the toast activator: {ex.Message}";
            }

            var hr = RegisterOnApartmentThread(clsid);
            if (hr < 0)
                return $"CoRegisterClassObject failed with HRESULT 0x{hr:X8}.";
            s_registered = clsid;
            return null;
        }
    }

    /// <summary>
    /// What Windows runs to start the app: the app's own executable, or for an app run through the
    /// <c>dotnet</c> host, the host plus the entry assembly.
    /// </summary>
    private static string? LaunchCommand()
    {
        var process = Environment.ProcessPath;
        if (string.IsNullOrEmpty(process))
            return null;
        if (!Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return $"\"{process}\"";

        // Under the dotnet host the first command-line argument is the app's own .dll.
        var entry = Environment.GetCommandLineArgs().FirstOrDefault();
        return string.IsNullOrEmpty(entry) || !File.Exists(entry) ? null : $"\"{process}\" \"{Path.GetFullPath(entry)}\"";
    }

    private static int RegisterOnApartmentThread(Guid clsid)
    {
        var result = 0;
        using var registered = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            WinRT.EnsureComInitialized();
            uint cookie;
            var id = clsid;
            result = CoRegisterClassObject(&id, s_factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, &cookie);
            registered.Set();
            if (result >= 0)
                Thread.Sleep(Timeout.Infinite); // holds the apartment, and the registration, for the process lifetime
        })
        {
            IsBackground = true,
            Name = "Hark toast activator",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        registered.Wait();
        return result;
    }

    // ---- The two COM objects: a vtable pointer followed by nothing. ----

    private static IntPtr CreateObject(params void*[] methods)
    {
        // IUnknown's AddRef/Release are shared; the object-specific methods follow QueryInterface.
        var vtable = (void**)NativeMemory.Alloc((nuint)((methods.Length + 2) * sizeof(void*)));
        vtable[0] = methods[0];
        vtable[1] = (delegate* unmanaged[Stdcall]<IntPtr, uint>)&AddRef;
        vtable[2] = (delegate* unmanaged[Stdcall]<IntPtr, uint>)&Release;
        for (var i = 1; i < methods.Length; i++)
            vtable[i + 2] = methods[i];

        var instance = (void**)NativeMemory.Alloc((nuint)sizeof(void*));
        *instance = vtable;
        return (IntPtr)instance;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(IntPtr self) => 2;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(IntPtr self) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FactoryQueryInterface(IntPtr self, Guid* iid, IntPtr* result)
    {
        if (*iid == WinRT.IID_IUnknown || *iid == IID_IClassFactory || *iid == WinRT.IID_IAgileObject)
        {
            *result = self;
            return WinRT.S_OK;
        }
        *result = IntPtr.Zero;
        return WinRT.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CreateInstance(IntPtr self, IntPtr outer, Guid* iid, IntPtr* result)
    {
        *result = IntPtr.Zero;
        if (outer != IntPtr.Zero)
            return CLASS_E_NOAGGREGATION;
        if (*iid == WinRT.IID_IUnknown || *iid == IID_INotificationActivationCallback || *iid == WinRT.IID_IAgileObject)
        {
            *result = s_callback;
            return WinRT.S_OK;
        }
        return WinRT.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int LockServer(IntPtr self, int @lock) => WinRT.S_OK;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CallbackQueryInterface(IntPtr self, Guid* iid, IntPtr* result)
    {
        if (*iid == WinRT.IID_IUnknown || *iid == IID_INotificationActivationCallback || *iid == WinRT.IID_IAgileObject)
        {
            *result = self;
            return WinRT.S_OK;
        }
        *result = IntPtr.Zero;
        return WinRT.E_NOINTERFACE;
    }

    /// <summary>
    /// <c>INotificationActivationCallback::Activate(appUserModelId, invokedArgs, data, count)</c>.
    /// User input (text boxes, selections) is not used: Hark's toasts have none.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Activate(IntPtr self, char* appUserModelId, char* invokedArgs, IntPtr data, uint count)
    {
        try
        {
            var arguments = invokedArgs == null ? "" : new string(invokedArgs);
            Volatile.Read(ref s_onActivated)?.Invoke(arguments);
        }
        catch (Exception ex)
        {
            // An exception unwinding into COM would kill the process.
            System.Diagnostics.Trace.TraceError($"Hark: toast activation handling threw: {ex}");
        }
        return WinRT.S_OK;
    }
}
