using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GitlabCICDNotify.App;

// A persistent top-level HWND owns the actual Windows menu loop, even with all WPF windows closed.
public sealed class TrayService : NativeWindow, IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly Action[] _actions;
    private readonly string[] _labels = ["Stav", "Nastavenia", "Ukončiť"];
    private Font? _font;
    private bool _menuOpen;
    private bool _disposed;
    private float _scale = 1;
    private int _width;
    private int _height;

    public TrayService(Action status, Action settings, Action exit)
    {
        _actions = [status, settings, exit];
        CreateHandle(new CreateParams { Caption = "GitLab Notify menu owner", Style = unchecked((int)0x80000000), ExStyle = 0x80 });
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/GitlabCICDNotify;component/Assets/App.ico"))!.Stream;
        _icon = new Icon(stream, SystemInformation.SmallIconSize);
        _tray = new NotifyIcon { Icon = _icon, Text = "GitLab CI/CD Notify", Visible = true };
        _tray.MouseUp += OnMouseUp;
        _tray.DoubleClick += OnDoubleClick;
    }

    private void OnDoubleClick(object? sender, EventArgs e) => _actions[0]();
    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) ShowMenu(Cursor.Position);
    }

    public void ShowMenu(Point anchor)
    {
        if (_disposed || _menuOpen) return;
        _menuOpen = true;
        uint selected = 0;
        nint menu = 0;
        try
        {
            // Move the invisible owner to the anchor monitor before measuring its effective DPI.
            SetWindowPos(Handle, 0, anchor.X, anchor.Y, 1, 1, 0x0010 | 0x0004);
            _scale = GetDpiForWindow(Handle) / 96f;
            _font?.Dispose();
            var fontSize = (float)System.Windows.SystemFonts.MenuFontSize;
            _font = new Font(System.Windows.SystemFonts.MenuFontFamily.Source, fontSize * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
            _height = (int)Math.Round(38 * _scale);
            _width = (int)Math.Round(210 * _scale);
            foreach (var label in _labels) _width = Math.Max(_width, TextRenderer.MeasureText(label, _font).Width + (int)(52 * _scale));
            menu = CreatePopupMenu();
            if (menu == 0) throw new System.ComponentModel.Win32Exception();
            for (uint i = 0; i < _labels.Length; i++)
            {
                var item = new MenuItemInfo
                {
                    Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = 0x100 | 0x40 | 0x2 | 0x1,
                    Type = 0x100, Id = i + 1, Text = _labels[i]
                };
                if (!InsertMenuItem(menu, i, true, ref item)) throw new System.ComponentModel.Win32Exception();
            }
            SetForegroundWindow(Handle);
            // TPM_RETURNCMD | TPM_RIGHTBUTTON; shell places it in the monitor work area.
            selected = TrackPopupMenuEx(menu, 0x100 | 0x2, anchor.X, anchor.Y, Handle, 0);
        }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
            PostMessage(Handle, 0, 0, 0);
            _menuOpen = false;
        }
        // Never open a window or destroy the owner inside the native menu loop.
        if (selected > 0 && selected <= _actions.Length) _actions[selected - 1]();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x002C && m.LParam != 0)
        {
            var item = Marshal.PtrToStructure<MeasureItem>(m.LParam);
            if (item.ControlType == 1 && item.Id is >= 1 and <= 3)
            {
                item.Width = (uint)_width; item.Height = (uint)_height;
                Marshal.StructureToPtr(item, m.LParam, false); m.Result = 1; return;
            }
        }
        if (m.Msg == 0x002B && m.LParam != 0)
        {
            var item = Marshal.PtrToStructure<DrawItem>(m.LParam);
            if (item.ControlType == 1 && item.Id is >= 1 and <= 3 && _font != null)
            {
                using var graphics = Graphics.FromHdc(item.Hdc);
                var selected = (item.State & 1) != 0;
                var highContrast = SystemInformation.HighContrast;
                var background = highContrast ? (selected ? SystemColors.Highlight : SystemColors.Menu) : (selected ? Color.FromArgb(235, 232, 252) : Color.White);
                var foreground = highContrast ? (selected ? SystemColors.HighlightText : SystemColors.MenuText) : Color.FromArgb(23, 36, 59);
                var rect = Rectangle.FromLTRB(item.Bounds.Left, item.Bounds.Top, item.Bounds.Right, item.Bounds.Bottom);
                using var brush = new SolidBrush(background);
                graphics.FillRectangle(brush, rect);
                rect.X += (int)(18 * _scale); rect.Width -= (int)(36 * _scale);
                TextRenderer.DrawText(graphics, _labels[item.Id - 1], _font, rect, foreground,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                m.Result = 1; return;
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tray.MouseUp -= OnMouseUp; _tray.DoubleClick -= OnDoubleClick;
        _tray.Visible = false; _tray.Dispose(); _icon.Dispose(); _font?.Dispose(); DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public nint SubMenu, CheckedBitmap, UncheckedBitmap;
        public nuint Data;
        [MarshalAs(UnmanagedType.LPWStr)] public string Text;
        public uint TextLength;
        public nint Bitmap;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MeasureItem { public uint ControlType, ControlId, Id, Width, Height; public nuint Data; }
    [StructLayout(LayoutKind.Sequential)] private struct DrawItem { public uint ControlType, ControlId, Id, Action, State; public nint Window, Hdc; public Rect Bounds; public nuint Data; }
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "InsertMenuItemW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InsertMenuItem(nint menu, uint position, bool byPosition, ref MenuItemInfo item);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
}
