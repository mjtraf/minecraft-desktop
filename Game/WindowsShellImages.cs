using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CozyCave;

// Windows Shell extracts icons/thumbnail-provider images on a dedicated COM apartment.
// No Godot objects are touched off the rendering thread.
[SupportedOSPlatform("windows")]
public sealed class WindowsShellImages : IDisposable
{
    public record Pixels(int Width, int Height, byte[] Rgba);
    private record Request(string Path, TaskCompletionSource<Pixels?> Completion);
    private readonly BlockingCollection<Request> requests = new(128);
    private readonly BlockingCollection<Request> thumbnailRequests = new(128);
    private readonly Thread worker;
    public WindowsShellImages()
    {
        worker = new Thread(() => Work(requests, false)) { IsBackground = true, Name = "Windows file icons" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
        var thumbnailWorker = new Thread(() => Work(thumbnailRequests, true)) { IsBackground = true, Name = "Windows thumbnails" };
        thumbnailWorker.SetApartmentState(ApartmentState.STA); thumbnailWorker.Start();
    }
    public Task<Pixels?> Get(string path, bool thumbnail = false)
    {
        var completion = new TaskCompletionSource<Pixels?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { if (!(thumbnail ? thumbnailRequests : requests).TryAdd(new(path, completion))) completion.TrySetResult(null); }
        catch (InvalidOperationException) { completion.TrySetResult(null); }
        return completion.Task;
    }
    private void Work(BlockingCollection<Request> queue, bool thumbnail)
    {
        int initialized = CoInitializeEx(0, 2);
        try
        {
            foreach (var request in queue.GetConsumingEnumerable())
            {
                try { request.Completion.TrySetResult(Extract(request.Path, thumbnail)); }
                catch { request.Completion.TrySetResult(null); }
            }
        }
        finally { if (initialized >= 0) CoUninitialize(); }
    }
    private static Pixels? Extract(string path, bool thumbnail)
    {
        nint bitmap = 0;
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, 0, ref iid, out factory) >= 0 && factory != null)
            {
                using var options = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced");
                uint flags = !thumbnail || options?.GetValue("IconsOnly") is int value && value != 0 ? 4u : 0u;
                if (factory.GetImage(new Size { X = 64, Y = 64 }, flags, out bitmap) >= 0 && bitmap != 0) return ReadBitmap(bitmap);
            }
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
        // A disconnected target still gets the Windows file-association icon.
        var info = new ShellFileInfo();
        if (SHGetFileInfo(path, 0x80, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100 | 0x10) == 0 || info.Icon == 0) return null;
        try
        {
            if (!GetIconInfo(info.Icon, out var icon)) return null;
            try { return icon.Color != 0 ? ReadBitmap(icon.Color) : null; }
            finally { if (icon.Color != 0) DeleteObject(icon.Color); if (icon.Mask != 0) DeleteObject(icon.Mask); }
        }
        finally { DestroyIcon(info.Icon); }
    }
    private static Pixels? ReadBitmap(nint bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<Bitmap>(), out var value) == 0 || value.Width <= 0 || value.Height == 0) return null;
        int width = value.Width, height = Math.Abs(value.Height);
        if (width > 1024 || height > 1024) return null;
        var header = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32, SizeImage = (uint)(width * height * 4) };
        var bytes = new byte[width * height * 4]; var dc = GetDC(0);
        try { if (GetDIBits(dc, bitmap, 0, (uint)height, bytes, ref header, 0) == 0) return null; }
        finally { ReleaseDC(0, dc); }
        bool hasAlpha = false;
        for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] != 0) { hasAlpha = true; break; }
        for (int i = 0; i < bytes.Length; i += 4)
        {
            (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
            if (!hasAlpha) bytes[i + 3] = 255;
            // Shell bitmap RGB is premultiplied; Godot expects straight alpha.
            else if (bytes[i + 3] is > 0 and < 255)
                for (int c = 0; c < 3; c++) bytes[i + c] = (byte)Math.Min(255, bytes[i + c] * 255 / bytes[i + 3]);
        }
        return new Pixels(width, height, bytes);
    }
    public void Dispose() { requests.CompleteAdding(); thumbnailRequests.CompleteAdding(); }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(Size size, uint flags, out nint bitmap); }
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int IsIcon; public uint HotspotX, HotspotY; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellFileInfo
    { public nint Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName; }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string path, nint bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(nint obj, int count, out Bitmap bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint count, byte[] bits, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
