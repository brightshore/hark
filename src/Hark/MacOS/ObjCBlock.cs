using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.MacOS;

/// <summary>
/// Objective-C blocks (closures) built by hand, so completion handlers can call back into .NET.
/// </summary>
/// <remarks>
/// <para>
/// A block is a struct whose first word is a class pointer, followed by flags, the function to
/// invoke, and a descriptor; captured state follows. Hark appends one word, a
/// <see cref="GCHandle"/> to the managed callback.
/// </para>
/// <para>
/// Lifetime is the subtle part, because the callee may keep the block after the call that received
/// it returns, and releases it whenever it is done. So Hark builds a <em>stack</em> block in its own
/// memory, has <c>_Block_copy</c> move it to a runtime-owned, reference-counted heap block, frees
/// its own copy at once, hands over the heap block, and drops its own reference after the send.
/// When the callee drops the last one, the runtime calls the descriptor's dispose helper, which
/// frees the <see cref="GCHandle"/>. Nothing is freed while the block can still be invoked.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
internal static unsafe class ObjCBlock
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const int BlockHasCopyDispose = 1 << 25;

    [StructLayout(LayoutKind.Sequential)]
    private struct Literal
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public Descriptor* Descriptor;
        public IntPtr Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Descriptor
    {
        public nuint Reserved;
        public nuint Size;
        public IntPtr Copy;
        public IntPtr Dispose;
    }

    [DllImport(LibSystem, EntryPoint = "_Block_copy")]
    private static extern IntPtr BlockCopy(IntPtr block);

    [DllImport(LibSystem, EntryPoint = "_Block_release")]
    private static extern void BlockRelease(IntPtr block);

    private static readonly IntPtr s_stackBlockClass =
        NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_NSConcreteStackBlock");

    // One descriptor serves every block Hark makes: same size, same helpers. Never freed.
    private static readonly Descriptor* s_descriptor = CreateDescriptor();

    private static Descriptor* CreateDescriptor()
    {
        var descriptor = (Descriptor*)NativeMemory.Alloc((nuint)sizeof(Descriptor));
        descriptor->Reserved = 0;
        descriptor->Size = (nuint)sizeof(Literal);
        descriptor->Copy = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&CopyHelper;
        descriptor->Dispose = (IntPtr)(delegate* unmanaged<IntPtr, void>)&DisposeHelper;
        return descriptor;
    }

    /// <summary>
    /// Creates a heap block that calls <paramref name="invoke"/> (an <c>[UnmanagedCallersOnly]</c>
    /// function whose first parameter is the block itself) with <paramref name="state"/> reachable
    /// through <see cref="GetState{T}"/>. The caller owns one reference: pass it to the callee, then
    /// call <see cref="Release"/>.
    /// </summary>
    public static IntPtr Create(IntPtr invoke, object state)
    {
        var literal = (Literal*)NativeMemory.Alloc((nuint)sizeof(Literal));
        try
        {
            literal->Isa = s_stackBlockClass;
            literal->Flags = BlockHasCopyDispose;
            literal->Reserved = 0;
            literal->Invoke = invoke;
            literal->Descriptor = s_descriptor;
            literal->Context = GCHandle.ToIntPtr(GCHandle.Alloc(state));
            return BlockCopy((IntPtr)literal);
        }
        finally
        {
            // The heap copy holds every byte it needs, the handle included.
            NativeMemory.Free(literal);
        }
    }

    public static void Release(IntPtr block) => BlockRelease(block);

    /// <summary>The state given to <see cref="Create"/>, from inside the invoke function.</summary>
    public static T GetState<T>(IntPtr block) where T : class
        => (T)GCHandle.FromIntPtr(((Literal*)block)->Context).Target!;

    /// <summary>The invoke function of a block someone else made, such as a completion handler passed to a delegate.</summary>
    public static IntPtr GetInvoke(IntPtr block) => ((Literal*)block)->Invoke;

    // The runtime has already copied the bytes, handle included; one handle serves the one heap
    // block, which is never copied again (further copies only bump its reference count).
    [UnmanagedCallersOnly]
    private static void CopyHelper(IntPtr destination, IntPtr source)
    {
    }

    [UnmanagedCallersOnly]
    private static void DisposeHelper(IntPtr block)
    {
        var context = ((Literal*)block)->Context;
        if (context != IntPtr.Zero)
            GCHandle.FromIntPtr(context).Free();
    }
}
