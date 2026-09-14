using System.Runtime.InteropServices;
using System.Text;

namespace Cave.Desktop;
internal static class Native
{
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    internal delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetStyle(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetStyle(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wp, nint lp, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd, uint msg, nint wp, nint lp);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    internal static nint Icons()
    {
        nint result = 0;
        EnumWindows((h, _) =>
        {
            var view = FindWindowEx(h, 0, "SHELLDLL_DefView", null);
            if (view != 0) result = FindWindowEx(view, 0, "SysListView32", null);
            return result == 0;
        }, 0);
        return result;
    }
    internal static nint DesktopHost()
    {
        var progman = FindWindow("Progman", null);
        SendMessageTimeout(progman, 0x052C, 0xD, 0, 2, 1000, out _);
        SendMessageTimeout(progman, 0x052C, 0xD, 1, 2, 1000, out _);
        nint result = 0;
        EnumWindows((h, _) =>
        {
            if (FindWindowEx(h, 0, "SHELLDLL_DefView", null) != 0)
            {
                result = FindWindowEx(0, h, "WorkerW", null);
                if (result == 0) result = FindWindowEx(progman, 0, "WorkerW", null);
                if (result == 0) result = h;
            }
            return result == 0;
        }, 0);
        return result;
    }
    internal static string Class(nint hwnd) { var s = new StringBuilder(256); GetClassName(hwnd, s, 256); return s.ToString(); }
}
