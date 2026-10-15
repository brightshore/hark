using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.Windows;

/// <summary>
/// A WinRT <c>TypedEventHandler&lt;TSender, TArgs&gt;</c> implemented by hand: a COM object whose
/// <c>Invoke</c> (slot 3, after IUnknown's three) calls a managed callback.
/// </summary>
/// <remarks>
/// Reference counted like any COM object. <see cref="Create"/> returns it holding one reference;
/// the caller passes it to an <c>add_</c> method (which takes its own) and then releases its own.
/// The last <c>Release</c>, from whichever thread the OS is on, frees the callback's GCHandle and
/// the memory. It answers IAgileObject, so the OS may call it from any thread without marshalling.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe class ComEventHandler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Instance
    {
        public void** Vtable;
        public int RefCount;
        public Guid Iid;
        public IntPtr Callback;
    }

    private static readonly void** s_vtable = CreateVtable();

    private static void** CreateVtable()
    {
        var vtable = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
        vtable[0] = (delegate* unmanaged[Stdcall]<Instance*, Guid*, IntPtr*, int>)&QueryInterface;
        vtable[1] = (delegate* unmanaged[Stdcall]<Instance*, uint>)&AddRef;
        vtable[2] = (delegate* unmanaged[Stdcall]<Instance*, uint>)&Release;
        vtable[3] = (delegate* unmanaged[Stdcall]<Instance*, IntPtr, IntPtr, int>)&Invoke;
        return vtable;
    }

    /// <summary>A handler for the delegate interface <paramref name="iid"/>, calling <paramref name="callback"/>(sender, args).</summary>
    public static IntPtr Create(Guid iid, Action<IntPtr, IntPtr> callback)
    {
        var instance = (Instance*)NativeMemory.Alloc((nuint)sizeof(Instance));
        instance->Vtable = s_vtable;
        instance->RefCount = 1;
        instance->Iid = iid;
        instance->Callback = GCHandle.ToIntPtr(GCHandle.Alloc(callback));
        return (IntPtr)instance;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int QueryInterface(Instance* self, Guid* iid, IntPtr* result)
    {
        if (*iid == self->Iid || *iid == WinRT.IID_IUnknown || *iid == WinRT.IID_IAgileObject)
        {
            Interlocked.Increment(ref self->RefCount);
            *result = (IntPtr)self;
            return WinRT.S_OK;
        }
        *result = IntPtr.Zero;
        return WinRT.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint AddRef(Instance* self) => (uint)Interlocked.Increment(ref self->RefCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint Release(Instance* self)
    {
        var count = Interlocked.Decrement(ref self->RefCount);
        if (count == 0)
        {
            GCHandle.FromIntPtr(self->Callback).Free();
            NativeMemory.Free(self);
        }
        return (uint)count;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int Invoke(Instance* self, IntPtr sender, IntPtr args)
    {
        try
        {
            ((Action<IntPtr, IntPtr>)GCHandle.FromIntPtr(self->Callback).Target!)(sender, args);
        }
        catch (Exception ex)
        {
            // An exception unwinding into the OS's notification thread would kill the process.
            System.Diagnostics.Trace.TraceError($"Hark: a toast event handler threw: {ex}");
        }
        return WinRT.S_OK;
    }
}
