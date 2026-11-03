using System.Runtime.InteropServices;
using System.Text;

namespace OpenXRInputExplorer.Services;

internal sealed class NativeDesktop : IDesktopInputSink
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput
    { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    private delegate bool EnumWindow(nint hwnd, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect rect, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    internal sealed record WindowChoice(nint Handle, string Title)
    { public override string ToString() => Title; }

    public static List<WindowChoice> ListWindows()
    {
        var windows = new List<WindowChoice>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint process);
            if (!IsWindowVisible(hwnd) || process == Environment.ProcessId) return true;
            var title = new StringBuilder(1024);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.Length > 0) windows.Add(new WindowChoice(hwnd, $"{title}  (PID {process})"));
            return true;
        }, 0);
        return windows.OrderBy(window => window.Title).ToList();
    }

    public static bool IsTargetReady(nint target) => target != 0 && IsWindow(target) && IsWindowVisible(target) && !IsIconic(target);
    public static bool IsFocused(nint target) => target != 0 && GetForegroundWindow() == target;
    public static bool IsUnobstructed(nint target, DesktopPoint point)
    {
        nint previousDpi = SetThreadDpiAwarenessContext(-4);
        try { return GetAncestor(WindowFromPoint(new Point { X = point.X, Y = point.Y }), 2) == target; }
        finally { if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi); }
    }

    public static bool TryMapPoint(nint target, System.Numerics.Vector2 uv, uint width, uint height, out DesktopPoint point)
    {
        point = default;
        if (!IsTargetReady(target) || DwmGetWindowAttribute(target, 9, out var rect, Marshal.SizeOf<Rect>()) < 0)
            return false;
        // Capture buffers can lag one frame behind a resize. Don't point at mismatched geometry.
        if (Math.Abs((long)rect.Right - rect.Left - width) > 2 || Math.Abs((long)rect.Bottom - rect.Top - height) > 2)
            return false;
        point = new DesktopPoint(rect.Left + (int)MathF.Round(uv.X * (rect.Right - rect.Left - 1)),
            rect.Top + (int)MathF.Round(uv.Y * (rect.Bottom - rect.Top - 1)));
        return true;
    }

    public void Key(ushort virtualKey, bool down)
    {
        uint scan = MapVirtualKey(virtualKey, 4);
        uint flags = down ? 0u : 2u;
        if (scan != 0) flags |= 8u;
        if ((scan & 0xFF00) != 0) flags |= 1u;
        Send(new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyInput
        { Vk = scan == 0 ? virtualKey : (ushort)0, Scan = (ushort)(scan & 255), Flags = flags } } });
    }

    public void MoveMouse(DesktopPoint point)
    {
        nint previousDpi = SetThreadDpiAwarenessContext(-4); // Physical pixels across mixed-DPI monitors.
        try
        {
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
            int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            int x = (int)Math.Clamp((2 * ((long)point.X - left) + 1) * 65536 / Math.Max(1, 2L * width), 0, 65535);
            int y = (int)Math.Clamp((2 * ((long)point.Y - top) + 1) * 65536 / Math.Max(1, 2L * height), 0, 65535);
            Send(new Input { Data = new InputUnion { Mouse = new MouseInput { X = x, Y = y, Flags = 0xC001 } } });
        }
        finally { if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi); }
    }

    public void LeftMouseButton(bool down) => Send(new Input
    { Data = new InputUnion { Mouse = new MouseInput { Flags = down ? 2u : 4u } } });

    private static void Send(Input input)
    {
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("Windows rejected simulated input. Run both applications at the same permission level.");
    }
}
