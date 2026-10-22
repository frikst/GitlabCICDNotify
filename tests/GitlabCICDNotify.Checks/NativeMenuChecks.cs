using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using GitlabCICDNotify.App;

namespace GitlabCICDNotify.Checks;

internal static class NativeMenuChecks
{
    public static void Run(Action<bool, string> check)
    {
        var called = false;
        var inMenu = false;
        var correctOwner = false;
        using var tray = new TrayService(() => called = true, () => called = true, () => called = true);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            if (GetGUIThreadInfo(GetCurrentThreadId(), ref info))
            {
                inMenu = (info.Flags & 4) != 0;
                correctOwner = info.MenuOwner == tray.Handle;
            }
            EndMenu();
            timer.Stop();
        };
        timer.Start();
        tray.ShowMenu(new Point(80, 80));
        timer.Stop();
        check(inMenu, "Tray uses native GUI_INMENUMODE");
        check(correctOwner, "Native menu owner is the persistent application HWND");
        check(!called, "Canceling the menu does not invoke a command");
        var after = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        check(GetGUIThreadInfo(GetCurrentThreadId(), ref after) && (after.Flags & 4) == 0, "Native menu loop exits cleanly");

        // Activate the first command using messages directed solely to our own menu window.
        var callbackOutsideLoop = false;
        using var selectedTray = new TrayService(() =>
        {
            var state = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            callbackOutsideLoop = GetGUIThreadInfo(GetCurrentThreadId(), ref state) && (state.Flags & 4) == 0;
        }, () => { }, () => { });
        var attempts = 0;
        var selectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        selectTimer.Tick += (_, _) =>
        {
            attempts++;
            EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
            {
                var name = new System.Text.StringBuilder(64);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() == "#32768")
                {
                    PostMessage(window, 0x0100, 0x28, 0); // WM_KEYDOWN, VK_DOWN
                    PostMessage(window, 0x0100, 0x0D, 0); // WM_KEYDOWN, VK_RETURN
                }
                return true;
            }, 0);
            if (attempts > 15) { EndMenu(); selectTimer.Stop(); }
        };
        selectTimer.Start();
        selectedTray.ShowMenu(new Point(80, 80));
        selectTimer.Stop();
        check(callbackOutsideLoop, "Selected action executes after native menu cleanup");
    }

    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    private delegate bool EnumWindowsCallback(nint window, nint parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EndMenu();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumThreadWindows(uint thread, EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
