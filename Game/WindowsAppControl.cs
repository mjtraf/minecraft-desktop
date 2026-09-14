using System.Runtime.InteropServices;
namespace CozyCave;
internal static class WindowsAppControl
{
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window,int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    internal static bool Activate(string id)
    {
        if(!long.TryParse(id,out long value) || !IsWindow((nint)value)) return false;
        if(IsIconic((nint)value)) ShowWindow((nint)value,9);
        return SetForegroundWindow((nint)value);
    }
}
