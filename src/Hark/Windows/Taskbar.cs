using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.Windows;

/// <summary>
/// <see cref="ITaskbar"/> over the shell's <c>ITaskbarList3</c>, called through its raw COM vtable,
/// and an icon made with GDI's <c>CreateIconIndirect</c>.
/// </summary>
/// <remarks>
/// A fresh <c>TaskbarList</c> is made on the calling thread for each change, so no pointer is ever
/// used from an apartment other than the one it was made in, and making one is cheap.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe class Taskbar : ITaskbar
{
    private static readonly Guid CLSID_TaskbarList = new("56fdf344-fd6d-11d0-958a-006097c9a090");
    // Checked against Windows 11 itself: TaskbarList answers this IID, and E_NOINTERFACE to the
    // look-alike ending ...8a5eee84 that circulates in hand-written bindings.
    private static readonly Guid IID_ITaskbarList3 = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");

    // Vtable slots: IUnknown's three, then ITaskbarList, ITaskbarList2 and ITaskbarList3 in order.
    private const int HrInit = 3;
    private const int SetProgressValueSlot = 9;
    private const int SetProgressStateSlot = 10;
    private const int SetOverlayIconSlot = 18;

    // TBPFLAG
    private const int TBPF_NOPROGRESS = 0;
    private const int TBPF_NORMAL = 2;

    /// <summary>The progress bar's resolution: SetProgressValue takes a fraction of whole numbers.</summary>
    internal const ulong ProgressTotal = 1000;

    private const uint CLSCTX_INPROC_SERVER = 1;

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(Guid* clsid, IntPtr outer, uint context, Guid* iid, IntPtr* instance);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr CreateIconIndirect(IconInfo* info);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetIconInfo(IntPtr icon, IconInfo* info);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr CreateDIBSection(IntPtr dc, BitmapInfoHeader* info, uint usage, void** bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, void* bits);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int GetObjectW(IntPtr gdiObject, int size, void* buffer);

    public int OverlaySize(nint window)
    {
        // 0 when the window is gone or on Windows before 1607: then assume 100%.
        var dpi = GetDpiForWindow(window);
        return (int)(16 * (dpi == 0 ? 96 : dpi) / 96);
    }

    public void SetOverlay(nint window, BadgeIcon? icon, string? description)
    {
        var hicon = icon is null ? IntPtr.Zero : CreateIcon(icon);
        try
        {
            WithTaskbar(taskbar =>
            {
                fixed (char* text = description)
                {
                    WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, char*, int>)WinRT.Slot(taskbar, SetOverlayIconSlot))(
                        taskbar, window, hicon, text), "ITaskbarList3.SetOverlayIcon");
                }
            });
        }
        finally
        {
            // The taskbar keeps its own copy.
            if (hicon != IntPtr.Zero)
                DestroyIcon(hicon);
        }
    }

    public void SetProgress(nint window, double? progress) => WithTaskbar(taskbar =>
    {
        var state = progress is null ? TBPF_NOPROGRESS : TBPF_NORMAL;
        WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)WinRT.Slot(taskbar, SetProgressStateSlot))(
            taskbar, window, state), "ITaskbarList3.SetProgressState");
        if (progress is { } value)
        {
            WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong, ulong, int>)WinRT.Slot(taskbar, SetProgressValueSlot))(
                taskbar, window, (ulong)Math.Round(value * ProgressTotal), ProgressTotal), "ITaskbarList3.SetProgressValue");
        }
    });

    private static void WithTaskbar(Action<IntPtr> body)
    {
        WinRT.EnsureComInitialized();
        var clsid = CLSID_TaskbarList;
        var iid = IID_ITaskbarList3;
        IntPtr taskbar;
        WinRT.Check(CoCreateInstance(&clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, &iid, &taskbar), "CoCreateInstance(TaskbarList)");
        try
        {
            WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRT.Slot(taskbar, HrInit))(taskbar), "ITaskbarList.HrInit");
            body(taskbar);
        }
        finally
        {
            WinRT.Release(taskbar);
        }
    }

    /// <summary>
    /// An icon from <paramref name="icon"/>'s pixels: a 32-bit top-down DIB, whose alpha is what
    /// Windows draws with, and an all-zero mask. The caller destroys it.
    /// </summary>
    internal static IntPtr CreateIcon(BadgeIcon icon)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = icon.Size,
            Height = -icon.Size, // negative: rows run top-down, as BadgeIcon's do
            Planes = 1,
            BitCount = 32,
        };
        void* bits;
        var color = CreateDIBSection(IntPtr.Zero, &header, 0 /* DIB_RGB_COLORS */, &bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero)
            throw new InvalidOperationException("CreateDIBSection failed for the icon.");

        // Monochrome rows are padded to 16 bits.
        var mask = new byte[(icon.Size + 15) / 16 * 2 * icon.Size];
        IntPtr maskBitmap = 0;
        try
        {
            // 0xAARRGGBB on a little-endian machine is B, G, R, A in memory: the DIB's own order.
            icon.Pixels.AsSpan().CopyTo(new Span<uint>(bits, icon.Pixels.Length));
            fixed (byte* maskBits = mask)
                maskBitmap = CreateBitmap(icon.Size, icon.Size, 1, 1, maskBits);
            if (maskBitmap == IntPtr.Zero)
                throw new InvalidOperationException("CreateBitmap failed for the icon mask.");

            var info = new IconInfo { IsIcon = 1, Mask = maskBitmap, Color = color };
            var hicon = CreateIconIndirect(&info);
            return hicon != IntPtr.Zero ? hicon : throw new InvalidOperationException("CreateIconIndirect failed.");
        }
        finally
        {
            // The icon holds copies of both bitmaps.
            DeleteObject(color);
            if (maskBitmap != IntPtr.Zero)
                DeleteObject(maskBitmap);
        }
    }

    /// <summary>The width, height and bits per pixel of an icon's colour bitmap, for checking one Hark made.</summary>
    internal static (int Width, int Height, int BitsPerPixel) DescribeIcon(IntPtr hicon)
    {
        IconInfo info;
        if (GetIconInfo(hicon, &info) == 0)
            throw new InvalidOperationException("GetIconInfo failed.");
        try
        {
            Bitmap bitmap;
            if (GetObjectW(info.Color, sizeof(Bitmap), &bitmap) == 0)
                throw new InvalidOperationException("GetObject failed for the icon's colour bitmap.");
            return (bitmap.Width, bitmap.Height, bitmap.BitsPixel);
        }
        finally
        {
            DeleteObject(info.Color);
            DeleteObject(info.Mask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }
}
