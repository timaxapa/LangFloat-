using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    public static float Dpi = 1f;

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        bool created;
        using (var mutex = new System.Threading.Mutex(true, "LangFloat_SingleInstance", out created))
        {
            if (!created) return;
            SetProcessDPIAware();
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Dpi = g.DpiX / 96f;
            Application.EnableVisualStyles();
            Application.Run(new FloatForm());
        }
    }
}

static class Native
{
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int n, IntPtr[] list);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    public static IntPtr FocusWindow()
    {
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
        return GetGUIThreadInfo(0, ref info) ? info.hwndFocus : IntPtr.Zero;
    }


    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int Cx, Cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    public static void UpdateLayered(IntPtr hwnd, Bitmap bmp, Point pos, byte alpha)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(mem, hbmp);
        try
        {
            var size = new SIZE { Cx = bmp.Width, Cy = bmp.Height };
            var src = new POINT();
            var dst = new POINT { X = pos.X, Y = pos.Y };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            UpdateLayeredWindow(hwnd, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    public interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktopId);
    }

    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    public class VirtualDesktopManager { }

    public static Guid CurrentDesktopFromRegistry()
    {
        try
        {
            string path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\" +
                Process.GetCurrentProcess().SessionId + @"\VirtualDesktops";
            using (var k = Registry.CurrentUser.OpenSubKey(path))
            {
                var bytes = k == null ? null : k.GetValue("CurrentVirtualDesktop") as byte[];
                if (bytes != null && bytes.Length == 16) return new Guid(bytes);
            }
        }
        catch { }
        return Guid.Empty;
    }

    public const int ModCtrl = 1, ModShift = 2, ModAlt = 4, ModWin = 8;

    static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    public static int CurrentMods()
    {
        int m = 0;
        if (Down(0x11)) m |= ModCtrl;
        if (Down(0x10)) m |= ModShift;
        if (Down(0x12)) m |= ModAlt;
        if (Down(0x5B) || Down(0x5C)) m |= ModWin;
        return m;
    }

    public static bool IsModifierKey(Keys k)
    {
        switch (k)
        {
            case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
            case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
            case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
            case Keys.LWin: case Keys.RWin:
                return true;
        }
        return false;
    }

    public static int GenericModifier(int vk)
    {
        switch (vk)
        {
            case 0x10: case 0xA0: case 0xA1: return 0x10;
            case 0x11: case 0xA2: case 0xA3: return 0x11;
            case 0x12: case 0xA4: case 0xA5: return 0x12;
            case 0x5B: case 0x5C: return 0x5B;
        }
        return 0;
    }

    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    public static bool TapMatches(int hotVk, int vk)
    {
        if (hotVk == 0x10 || hotVk == 0x11 || hotVk == 0x12) return GenericModifier(vk) == hotVk;
        return vk == hotVk;
    }

    public static int SpecificModifier(Keys k)
    {
        int g = GenericModifier((int)k);
        if (k == Keys.LWin || k == Keys.RWin) return (int)k;
        int left = g == 0x10 ? 0xA0 : g == 0x11 ? 0xA2 : 0xA4;
        if (Down(left + 1)) return left + 1;
        if (Down(left)) return left;
        return g;
    }

    public static string HotkeyText(int vk, int mods)
    {
        if (mods == 0 && GenericModifier(vk) != 0)
            return ModifierName(vk) + ", нажать и отпустить";
        string s = "";
        if ((mods & ModCtrl) != 0) s += "Ctrl + ";
        if ((mods & ModShift) != 0) s += "Shift + ";
        if ((mods & ModAlt) != 0) s += "Alt + ";
        if ((mods & ModWin) != 0) s += "Win + ";
        return vk == 0 ? s + "…" : s + KeyName(vk);
    }

    static string ModifierName(int vk)
    {
        switch (vk)
        {
            case 0xA0: return "Левый Shift";
            case 0xA1: return "Правый Shift";
            case 0xA2: return "Левый Ctrl";
            case 0xA3: return "Правый Ctrl";
            case 0xA4: return "Левый Alt";
            case 0xA5: return "Правый Alt / Option";
            case 0x5B: return "Левый Win";
            case 0x5C: return "Правый Win";
        }
        return KeyName(GenericModifier(vk));
    }

    static string KeyName(int vk)
    {
        switch ((Keys)vk)
        {
            case Keys.ShiftKey: return "Shift";
            case Keys.ControlKey: return "Ctrl";
            case Keys.Menu: return "Alt";
            case Keys.LWin: return "Win";
            case Keys.CapsLock: return "Caps Lock";
            case Keys.Scroll: return "Scroll Lock";
            case Keys.Pause: return "Pause";
            case Keys.Apps: return "Menu";
            case Keys.Oemtilde: return "~";
            case Keys.Space: return "Space";
            case Keys.Return: return "Enter";
            case Keys.Escape: return "Esc";
        }
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        return ((Keys)vk).ToString();
    }
}

static class Settings
{
    const string Key = @"Software\LangFloat";

    public static int HotkeyVk = (int)Keys.CapsLock;
    public static int HotkeyMods = 0;
    public static bool HotkeyEnabled = true;

    public static void Load()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Key))
        {
            if (k == null) return;
            HotkeyVk = (int)k.GetValue("HotkeyVk", HotkeyVk);
            HotkeyMods = (int)k.GetValue("HotkeyMods", HotkeyMods);
            HotkeyEnabled = (int)k.GetValue("HotkeyEnabled", 1) != 0;
        }
    }

    public static void SaveHotkey()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            k.SetValue("HotkeyVk", HotkeyVk, RegistryValueKind.DWord);
            k.SetValue("HotkeyMods", HotkeyMods, RegistryValueKind.DWord);
            k.SetValue("HotkeyEnabled", HotkeyEnabled ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    public static Point? LoadLocation()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Key))
        {
            if (k == null) return null;
            var x = k.GetValue("X") as int?;
            var y = k.GetValue("Y") as int?;
            if (x.HasValue && y.HasValue) return new Point(x.Value, y.Value);
        }
        return null;
    }

    public static void SaveLocation(Point p)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            k.SetValue("X", p.X, RegistryValueKind.DWord);
            k.SetValue("Y", p.Y, RegistryValueKind.DWord);
        }
    }
}

class FloatForm : Form
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "LangFloat";

    readonly Timer timer = new Timer();
    readonly ToolStripMenuItem autostartItem;
    readonly Native.LowLevelKeyboardProc hookProc;
    readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
    readonly Font gearFont = new Font("Segoe UI Symbol", 10f * Program.Dpi);
    static Size BaseSize { get { return new Size((int)(84 * Program.Dpi), (int)(46 * Program.Dpi)); } }
    readonly Native.IVirtualDesktopManager vdm;
    Point baseLocation;
    readonly Native.LowLevelKeyboardProc mouseProc;
    IntPtr hook;
    IntPtr mouseHook;
    bool tapArmed;
    bool tapKeyDown;
    int tapStart;
    volatile bool hookPaused;
    bool hotkeyHeld;
    string lang = "";
    Color back = Color.DimGray;
    Point downPoint;
    bool pressed;

    public FloatForm()
    {
        Settings.Load();

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        try { vdm = (Native.IVirtualDesktopManager)new Native.VirtualDesktopManager(); }
        catch { vdm = null; }
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = BaseSize;
        Font = new Font("Segoe UI", 18f, FontStyle.Bold);
        baseLocation = InitialLocation();
        Location = new Point(baseLocation.X - Offset.X, baseLocation.Y - Offset.Y);

        autostartItem = new ToolStripMenuItem("Автозапуск", null, (s, e) => ToggleAutostart());
        autostartItem.Checked = IsAutostart();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Настройки…", null, (s, e) => OpenSettings());
        menu.Items.Add(autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (s, e) => Close());
        ContextMenuStrip = menu;

        hookProc = HookCallback;
        mouseProc = MouseCallback;
        var hookThread = new System.Threading.Thread(() =>
        {
            hook = Native.SetWindowsHookEx(13, hookProc, Native.GetModuleHandle(null), 0);
            mouseHook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
            Application.Run();
        });
        hookThread.IsBackground = true;
        hookThread.Start();

        timer.Interval = 150;
        timer.Tick += (s, e) => Tick();
        timer.Start();
        Tick();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00080000 | 0x00000080 | 0x00000008;
            return cp;
        }
    }

    Point Offset
    {
        get { return new Point((Width - BaseSize.Width) / 2, (Height - BaseSize.Height) / 2); }
    }

    Rectangle GearRect
    {
        get
        {
            int g = (int)(22 * Program.Dpi);
            return new Rectangle(Offset.X + BaseSize.Width - g, Offset.Y, g, g);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Render();
    }

    IntPtr ForeignForeground(out uint thread)
    {
        IntPtr focus;
        return ForeignForeground(out thread, out focus);
    }

    IntPtr ForeignForeground(out uint thread, out IntPtr focus)
    {
        thread = 0;
        focus = IntPtr.Zero;
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return IntPtr.Zero;
        uint pid;
        thread = Native.GetWindowThreadProcessId(fg, out pid);
        if (pid == ownPid) return IntPtr.Zero;
        var f = Native.FocusWindow();
        if (f != IntPtr.Zero && f != fg)
        {
            uint fpid;
            uint ft = Native.GetWindowThreadProcessId(f, out fpid);
            if (fpid != ownPid)
            {
                focus = f;
                thread = ft;
            }
        }
        return fg;
    }

    void FollowVirtualDesktop()
    {
        if (vdm == null) return;
        try
        {
            int on;
            if (vdm.IsWindowOnCurrentVirtualDesktop(Handle, out on) != 0 || on != 0) return;
            Guid id = Native.CurrentDesktopFromRegistry();
            if (id == Guid.Empty)
            {
                var fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == Handle || vdm.GetWindowDesktopId(fg, out id) != 0) return;
            }
            if (id != Guid.Empty) vdm.MoveWindowToDesktop(Handle, ref id);
        }
        catch { }
    }

    void Tick()
    {
        FollowVirtualDesktop();
        uint thread;
        if (ForeignForeground(out thread) != IntPtr.Zero)
        {
            int langId = (int)((long)Native.GetKeyboardLayout(thread) & 0xFFFF);
            string name = LangName(langId);
            if (name != lang)
            {
                lang = name;
                back = ColorFor(name);
                Render();
            }
        }
        Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    void Render()
    {
        if (!IsHandleCreated) return;
        using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                var r = new RectangleF(0, 0, Width, Height);
                using (var path = RoundedRect(r, 8f * Program.Dpi))
                using (var b = new SolidBrush(back))
                    g.FillPath(b, path);
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    using (var f = new Font(Font.FontFamily, Font.Size * Program.Dpi, Font.Style))
                        g.DrawString(lang, f, Brushes.White, r, sf);
                    using (var gb = new SolidBrush(Color.FromArgb(230, 230, 230)))
                            g.DrawString("⚙", gearFont, gb, GearRect, sf);
                }
            }
            Native.UpdateLayered(Handle, bmp, Location, 235);
        }
    }

    void SwitchLanguage()
    {
        uint thread;
        IntPtr focus;
        var fg = ForeignForeground(out thread, out focus);
        if (fg == IntPtr.Zero) return;

        int n = Native.GetKeyboardLayoutList(0, null);
        if (n < 2) return;
        var list = new IntPtr[n];
        Native.GetKeyboardLayoutList(n, list);
        int idx = Array.IndexOf(list, Native.GetKeyboardLayout(thread));
        var next = list[(idx + 1) % n];

        if (focus != IntPtr.Zero) Native.PostMessage(focus, 0x0050, IntPtr.Zero, next);
        Native.PostMessage(fg, 0x0050, IntPtr.Zero, next);
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !hookPaused && Settings.HotkeyEnabled)
        {
            var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
            int msg = wParam.ToInt32();
            bool down = msg == 0x0100 || msg == 0x0104;
            if ((k.flags & 0x10) == 0 && Settings.HotkeyMods == 0 && Native.GenericModifier(Settings.HotkeyVk) != 0)
            {
                HandleTap((int)k.vkCode, down);
            }
            else if ((k.flags & 0x10) == 0 && k.vkCode == Settings.HotkeyVk)
            {
                if (down)
                {
                    if (hotkeyHeld) return new IntPtr(1);
                    if (Native.CurrentMods() == Settings.HotkeyMods)
                    {
                        hotkeyHeld = true;
                        BeginInvoke(new Action(SwitchLanguage));
                        return new IntPtr(1);
                    }
                }
                else if (hotkeyHeld)
                {
                    hotkeyHeld = false;
                    return new IntPtr(1);
                }
            }
        }
        return Native.CallNextHookEx(hook, nCode, wParam, lParam);
    }

    void HandleTap(int vk, bool down)
    {
        bool isHotkey = Native.TapMatches(Settings.HotkeyVk, vk);
        if (down)
        {
            if (!isHotkey) tapArmed = false;
            else if (!tapKeyDown)
            {
                tapKeyDown = true;
                tapArmed = true;
                tapStart = Environment.TickCount;
            }
        }
        else if (isHotkey)
        {
            if (tapArmed && Environment.TickCount - tapStart < 600)
            {
                int g = Native.GenericModifier(vk);
                if (g == 0x12 || g == 0x5B)
                {
                    Native.keybd_event(0xE8, 0, 0, UIntPtr.Zero);
                    Native.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                }
                BeginInvoke(new Action(SwitchLanguage));
            }
            tapArmed = false;
            tapKeyDown = false;
        }
    }

    IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() != 0x0200) tapArmed = false;
        return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }

    void OpenSettings()
    {
        hookPaused = true;
        try
        {
            using (var dlg = new SettingsForm())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    Settings.HotkeyVk = dlg.Vk;
                    Settings.HotkeyMods = dlg.Mods;
                    Settings.HotkeyEnabled = dlg.HotkeyOn;
                    Settings.SaveHotkey();
                }
            }
        }
        finally
        {
            hotkeyHeld = false;
            hookPaused = false;
        }
    }

    static string LangName(int langId)
    {
        if (langId == 0) return "??";
        try { return new CultureInfo(langId).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch { return "??"; }
    }

    static Color ColorFor(string name)
    {
        switch (name)
        {
            case "RU": return Color.FromArgb(0, 102, 204);
            case "EN": return Color.FromArgb(200, 60, 40);
            case "UK": return Color.FromArgb(220, 170, 0);
            default: return Color.FromArgb(90, 90, 90);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            pressed = true;
            downPoint = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = GearRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        if (pressed && (Math.Abs(e.X - downPoint.X) > 4 || Math.Abs(e.Y - downPoint.Y) > 4))
        {
            pressed = false;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!pressed || e.Button != MouseButtons.Left) return;
        pressed = false;
        if (GearRect.Contains(e.Location)) OpenSettings();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        baseLocation = new Point(Location.X + Offset.X, Location.Y + Offset.Y);
        Settings.SaveLocation(baseLocation);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Settings.SaveLocation(baseLocation);
        timer.Stop();
        if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
        if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
        base.OnFormClosing(e);
    }

    Point InitialLocation()
    {
        var saved = Settings.LoadLocation();
        if (saved.HasValue)
            foreach (var s in Screen.AllScreens)
                if (s.WorkingArea.Contains(new Rectangle(saved.Value, BaseSize))) return saved.Value;
        var wa = Screen.PrimaryScreen.WorkingArea;
        return new Point(wa.Right - BaseSize.Width - 20, wa.Bottom - BaseSize.Height - 20);
    }

    static bool IsAutostart()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            return key != null && key.GetValue(AppName) != null;
    }

    void ToggleAutostart()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (IsAutostart()) key.DeleteValue(AppName, false);
            else key.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
        }
        autostartItem.Checked = IsAutostart();
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

class HotkeyBox : TextBox
{
    protected override bool IsInputKey(Keys keyData) { return true; }
}

class SettingsForm : Form
{
    public int Vk { get; private set; }
    public int Mods { get; private set; }
    public bool HotkeyOn { get { return enabledBox.Checked; } }

    readonly HotkeyBox box = new HotkeyBox();
    readonly CheckBox enabledBox = new CheckBox();

    public SettingsForm()
    {
        Vk = Settings.HotkeyVk;
        Mods = Settings.HotkeyMods;

        Text = "LangFloat — настройки";
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        var title = new Label { Text = "Горячая клавиша смены языка", AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        var hint = new Label
        {
            Text = "Кликните в поле и нажмите клавишу или сочетание",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 4, 0, 10)
        };

        box.ReadOnly = true;
        box.BackColor = SystemColors.Window;
        box.Width = 340;
        box.Margin = new Padding(0, 1, 8, 0);
        box.Text = Native.HotkeyText(Vk, Mods);
        box.KeyDown += OnBoxKeyDown;
        box.KeyUp += OnBoxKeyUp;

        var defaultButton = new Button { Text = "Caps Lock", AutoSize = true, MinimumSize = new Size(96, 0), Margin = new Padding(0) };
        defaultButton.Click += (s, e) =>
        {
            Vk = (int)Keys.CapsLock;
            Mods = 0;
            box.Text = Native.HotkeyText(Vk, Mods);
        };

        enabledBox.Text = "Горячая клавиша включена";
        enabledBox.Checked = Settings.HotkeyEnabled;
        enabledBox.AutoSize = true;
        enabledBox.Margin = new Padding(0, 0, 0, 16);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(96, 0), Margin = new Padding(8, 0, 0, 0) };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(96, 0), Margin = new Padding(8, 0, 0, 0) };
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Controls.Add(title, 0, 0);
        table.SetColumnSpan(title, 2);
        table.Controls.Add(box, 0, 1);
        table.Controls.Add(defaultButton, 1, 1);
        table.Controls.Add(hint, 0, 2);
        table.SetColumnSpan(hint, 2);
        table.Controls.Add(enabledBox, 0, 3);
        table.SetColumnSpan(enabledBox, 2);
        table.Controls.Add(buttons, 0, 4);
        table.SetColumnSpan(buttons, 2);
        Controls.Add(table);
        ActiveControl = ok;
    }

    void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        int mods = Native.CurrentMods();
        if (Native.IsModifierKey(e.KeyCode))
        {
            int g = Native.SpecificModifier(e.KeyCode);
            if (pendingMod == 0) { pendingMod = g; pendingClean = true; }
            else if (g != pendingMod) pendingClean = false;
            box.Text = Native.HotkeyText(0, mods);
            return;
        }
        pendingMod = 0;
        Vk = (int)e.KeyCode;
        Mods = mods;
        box.Text = Native.HotkeyText(Vk, Mods);
    }

    int pendingMod;
    bool pendingClean;

    void OnBoxKeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (!Native.IsModifierKey(e.KeyCode)) return;
        if (pendingClean && Native.GenericModifier((int)e.KeyCode) == Native.GenericModifier(pendingMod))
        {
            Vk = pendingMod;
            Mods = 0;
        }
        pendingMod = 0;
        box.Text = Native.HotkeyText(Vk, Mods);
    }
}
