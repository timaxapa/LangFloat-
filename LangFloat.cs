using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
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
    [DllImport("user32.dll")] public static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);
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

    public static Guid[] DesktopIds()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops"))
            {
                var bytes = k == null ? null : k.GetValue("VirtualDesktopIDs") as byte[];
                if (bytes != null && bytes.Length >= 16)
                {
                    var ids = new Guid[bytes.Length / 16];
                    for (int i = 0; i < ids.Length; i++)
                    {
                        var one = new byte[16];
                        Array.Copy(bytes, i * 16, one, 0, 16);
                        ids[i] = new Guid(one);
                    }
                    return ids;
                }
            }
        }
        catch { }
        return new Guid[0];
    }

    public static void SendDesktopStep(bool right)
    {
        byte arrow = right ? (byte)0x27 : (byte)0x25;
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        keybd_event(0x5B, 0, 1, UIntPtr.Zero);
        keybd_event(arrow, 0, 1, UIntPtr.Zero);
        keybd_event(arrow, 0, 3, UIntPtr.Zero);
        keybd_event(0x5B, 0, 3, UIntPtr.Zero);
        keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }

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
    static readonly string FilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LangFloat.json");

    public static int HotkeyVk = (int)Keys.CapsLock;
    public static int HotkeyMods = 0;
    public static bool HotkeyEnabled = true;
    public static bool ShowDesktops = true;
    public static Point? Position;
    public static readonly Dictionary<string, string> DesktopNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static void Load()
    {
        if (System.IO.File.Exists(FilePath))
        {
            try
            {
                LoadFile();
                return;
            }
            catch
            {
                try { System.IO.File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            }
        }
        LoadRegistry();
        Save();
    }

    static void LoadFile()
    {
        var root = new JavaScriptSerializer().DeserializeObject(System.IO.File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
        if (root == null) throw new FormatException();
        var hk = Obj(root, "hotkey");
        if (hk != null)
        {
            HotkeyVk = Int(hk, "vk", HotkeyVk);
            HotkeyMods = Int(hk, "mods", HotkeyMods);
            HotkeyEnabled = Bool(hk, "enabled", HotkeyEnabled);
        }
        ShowDesktops = Bool(root, "showDesktops", ShowDesktops);
        var pos = Obj(root, "position");
        if (pos != null && pos.ContainsKey("x") && pos.ContainsKey("y"))
            Position = new Point(Int(pos, "x", 0), Int(pos, "y", 0));
        var desks = Obj(root, "desktops");
        if (desks != null)
            foreach (var kv in desks)
            {
                var name = kv.Value as string;
                if (!string.IsNullOrEmpty(name)) DesktopNames[kv.Key] = name;
            }
    }

    static void LoadRegistry()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Key))
        {
            if (k == null) return;
            HotkeyVk = (int)k.GetValue("HotkeyVk", HotkeyVk);
            HotkeyMods = (int)k.GetValue("HotkeyMods", HotkeyMods);
            HotkeyEnabled = (int)k.GetValue("HotkeyEnabled", 1) != 0;
            ShowDesktops = (int)k.GetValue("ShowDesktops", 1) != 0;
            var x = k.GetValue("X") as int?;
            var y = k.GetValue("Y") as int?;
            if (x.HasValue && y.HasValue) Position = new Point(x.Value, y.Value);
        }
    }

    static Dictionary<string, object> Obj(Dictionary<string, object> o, string key)
    {
        object v;
        return o.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
    }

    static int Int(Dictionary<string, object> o, string key, int def)
    {
        object v;
        if (!o.TryGetValue(key, out v) || v == null) return def;
        try { return Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return def; }
    }

    static bool Bool(Dictionary<string, object> o, string key, bool def)
    {
        object v;
        return o.TryGetValue(key, out v) && v is bool ? (bool)v : def;
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    public static void Save()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"hotkey\": { \"vk\": ").Append(HotkeyVk).Append(", \"mods\": ").Append(HotkeyMods)
          .Append(", \"enabled\": ").Append(HotkeyEnabled ? "true" : "false").Append(" },\n");
        sb.Append("  \"showDesktops\": ").Append(ShowDesktops ? "true" : "false").Append(",\n");
        if (Position.HasValue)
            sb.Append("  \"position\": { \"x\": ").Append(Position.Value.X).Append(", \"y\": ").Append(Position.Value.Y).Append(" },\n");
        sb.Append("  \"desktops\": {");
        bool first = true;
        foreach (var kv in DesktopNames)
        {
            sb.Append(first ? "\n" : ",\n").Append("    ").Append(Quote(kv.Key)).Append(": ").Append(Quote(kv.Value));
            first = false;
        }
        sb.Append(first ? "}\n" : "\n  }\n");
        sb.Append("}\n");
        try
        {
            string tmp = FilePath + ".tmp";
            System.IO.File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (System.IO.File.Exists(FilePath)) System.IO.File.Replace(tmp, FilePath, null);
            else System.IO.File.Move(tmp, FilePath);
        }
        catch { }
    }

    public static void SaveHotkey() { Save(); }

    public static void SaveShowDesktops() { Save(); }

    public static Point? LoadLocation() { return Position; }

    public static void SaveLocation(Point p)
    {
        Position = p;
        Save();
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
    readonly Font gearFont = new Font("Segoe MDL2 Assets", 11f * Program.Dpi);
    readonly Font nameFont = new Font("Segoe UI Semibold", 10.5f * Program.Dpi);
    readonly Font nameBoldFont = new Font("Segoe UI Semibold", 10.5f * Program.Dpi);
    readonly Font numFont = new Font("Segoe UI", 9f * Program.Dpi, FontStyle.Bold);
    readonly ToolStripMenuItem desktopsItem;
    readonly ToolStripMenuItem renameItem;
    const int PanelW = 220, CompactW = 104, Pad = 6, HeaderH = 46, GearSize = 34, RowH = 36, RowGap = 2;
    const int ShadowSide = 14, ShadowTop = 12, ShadowBottom = 16, PanelRadius = 14, RowRadius = 9;
    Guid[] desktopIds = new Guid[0];
    int desktopCount = 1;
    int desktopIndex;
    int hover = -1;
    int menuRow = -1;
    readonly Font editFont = new Font("Segoe MDL2 Assets", 9f * Program.Dpi);
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
    string region = "";
    int currentLangId = -1;
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
        Font = new Font("Segoe UI", 19.5f, FontStyle.Bold);
        ReadDesktops();
        Size = LayoutSize();
        baseLocation = InitialLocation();
        Location = baseLocation;

        autostartItem = new ToolStripMenuItem("Автозапуск", null, (s, e) => ToggleAutostart());
        autostartItem.Checked = IsAutostart();
        var menu = new ContextMenuStrip();
        renameItem = new ToolStripMenuItem("Переименовать стол…", null, (s, e) => RenameDesktop(menuRow));
        menu.Items.Add(renameItem);
        menu.Opening += (s, e) => renameItem.Visible = menuRow >= 0 && ButtonsVisible;
        menu.Items.Add("Настройки…", null, (s, e) => OpenSettings());
        desktopsItem = new ToolStripMenuItem("Кнопки рабочих столов", null, (s, e) => ToggleDesktops());
        desktopsItem.Checked = Settings.ShowDesktops;
        menu.Items.Add(desktopsItem);
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

    static int Px(float v) { return (int)Math.Round(v * Program.Dpi); }

    bool ButtonsVisible { get { return Settings.ShowDesktops && desktopCount > 1; } }

    int PanelWidth { get { return ButtonsVisible ? PanelW : CompactW; } }

    const int RowsTop = Pad + HeaderH + 4 + 1 + 6;

    int PanelHeight
    {
        get { return ButtonsVisible ? RowsTop + desktopCount * (RowH + RowGap) - RowGap + Pad : Pad * 2 + HeaderH; }
    }

    Size LayoutSize()
    {
        return new Size(Px(PanelWidth + ShadowSide * 2), Px(PanelHeight + ShadowTop + ShadowBottom));
    }

    Rectangle PanelRect
    {
        get { return new Rectangle(Px(ShadowSide), Px(ShadowTop), Px(PanelWidth), Px(PanelHeight)); }
    }

    Rectangle GearRect
    {
        get { return new Rectangle(Px(ShadowSide + PanelWidth - Pad - GearSize), Px(ShadowTop + Pad + (HeaderH - GearSize) / 2f), Px(GearSize), Px(GearSize)); }
    }

    Rectangle LangRect
    {
        get { return new Rectangle(Px(ShadowSide + Pad), Px(ShadowTop + Pad), Px(PanelWidth - Pad * 2 - GearSize - 4), Px(HeaderH)); }
    }

    Rectangle RowRect(int i)
    {
        return new Rectangle(Px(ShadowSide + Pad), Px(ShadowTop + RowsTop + i * (RowH + RowGap)), Px(PanelWidth - Pad * 2), Px(RowH));
    }

    Rectangle BadgeRect(int i)
    {
        var r = RowRect(i);
        int size = Px(22);
        return new Rectangle(r.X + Px(8), r.Y + (r.Height - size) / 2, size, size);
    }

    Rectangle EditRect(int i)
    {
        var r = RowRect(i);
        int size = Px(26);
        return new Rectangle(r.Right - Px(5) - size, r.Y + (r.Height - size) / 2, size, size);
    }

    Rectangle NameRect(int i)
    {
        var r = RowRect(i);
        int x = BadgeRect(i).Right + Px(10);
        return new Rectangle(x, r.Y, EditRect(i).X - Px(4) - x, r.Height);
    }

    const int EditHitBase = -100;

    static int HoverRow(int hit)
    {
        if (hit >= 0) return hit;
        if (hit <= EditHitBase) return EditHitBase - hit;
        return -1;
    }

    int HitTest(Point p)
    {
        if (GearRect.Contains(p)) return -2;
        if (LangRect.Contains(p)) return -3;
        if (ButtonsVisible)
            for (int i = 0; i < desktopCount; i++)
            {
                if (EditRect(i).Contains(p)) return EditHitBase - i;
                if (RowRect(i).Contains(p)) return i;
            }
        return -1;
    }

    string DesktopName(int i)
    {
        string name;
        if (i < desktopIds.Length && Settings.DesktopNames.TryGetValue(desktopIds[i].ToString(), out name)) return name;
        return null;
    }

    bool ReadDesktops()
    {
        var ids = Native.DesktopIds();
        int count = Math.Max(1, ids.Length);
        int index = Math.Max(0, Array.IndexOf(ids, Native.CurrentDesktopFromRegistry()));
        bool changed = count != desktopCount || index != desktopIndex;
        for (int i = 0; !changed && i < ids.Length; i++)
            changed = i >= desktopIds.Length || ids[i] != desktopIds[i];
        desktopIds = ids;
        desktopCount = count;
        desktopIndex = index;
        return changed;
    }

    void Relayout()
    {
        var size = LayoutSize();
        if (size != Size)
        {
            Size = size;
            var wa = Screen.FromPoint(Location).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width));
            int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
            Location = new Point(x, y);
            baseLocation = Location;
        }
        Render();
    }

    void RenameDesktop(int i)
    {
        if (i < 0 || i >= desktopIds.Length) return;
        var id = desktopIds[i].ToString();
        using (var dlg = new RenameDialog(i + 1, DesktopName(i) ?? ""))
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            string text = dlg.NameText.Trim();
            if (text.Length == 0) Settings.DesktopNames.Remove(id);
            else Settings.DesktopNames[id] = text;
            Settings.Save();
        }
        Render();
    }

    void ToggleDesktops()
    {
        Settings.ShowDesktops = !Settings.ShowDesktops;
        Settings.SaveShowDesktops();
        desktopsItem.Checked = Settings.ShowDesktops;
        Relayout();
    }

    volatile bool switching;

    void SwitchToDesktop(int target)
    {
        if (switching) return;
        var ids = Native.DesktopIds();
        if (target < 0 || target >= ids.Length) return;
        var targetId = ids[target];
        switching = true;
        var t = new System.Threading.Thread(() =>
        {
            try
            {
                for (int attempt = 0; attempt < ids.Length * 2; attempt++)
                {
                    var cur = Native.CurrentDesktopFromRegistry();
                    if (cur == targetId) break;
                    int idx = Array.IndexOf(ids, cur);
                    if (idx < 0) break;
                    Native.SendDesktopStep(target > idx);
                    int start = Environment.TickCount;
                    while (Native.CurrentDesktopFromRegistry() == cur && Environment.TickCount - start < 1500)
                        System.Threading.Thread.Sleep(30);
                    System.Threading.Thread.Sleep(120);
                }
            }
            finally { switching = false; }
        });
        t.IsBackground = true;
        t.Start();
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

    bool OwnForeground()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Handle) return false;
        uint pid;
        Native.GetWindowThreadProcessId(fg, out pid);
        return pid == ownPid;
    }

    void Tick()
    {
        FollowVirtualDesktop();
        if (ReadDesktops()) Relayout();
        uint thread;
        IntPtr hkl = IntPtr.Zero;
        if (ForeignForeground(out thread) != IntPtr.Zero) hkl = Native.GetKeyboardLayout(thread);
        else if (OwnForeground()) hkl = Native.GetKeyboardLayout(0);
        if (hkl != IntPtr.Zero)
        {
            int langId = (int)((long)hkl & 0xFFFF);
            if (langId != currentLangId)
            {
                currentLangId = langId;
                lang = LangName(langId);
                region = RegionName(langId);
                back = ColorFor(lang);
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
                RectangleF panel = PanelRect;
                for (int k = 16; k >= 1; k--)
                {
                    var sr = panel;
                    sr.Inflate(k * 0.85f * Program.Dpi, k * 0.85f * Program.Dpi);
                    sr.Offset(0, 2 * Program.Dpi);
                    using (var sp = RoundedRect(sr, (PanelRadius + k) * Program.Dpi))
                    using (var sb = new SolidBrush(Color.FromArgb(3, 10, 20, 35)))
                        g.FillPath(sb, sp);
                }
                using (var path = RoundedRect(panel, PanelRadius * Program.Dpi))
                using (var b = new SolidBrush(back))
                    g.FillPath(b, path);
                var edge = panel;
                edge.Inflate(-0.5f * Program.Dpi, -0.5f * Program.Dpi);
                using (var ep = RoundedRect(edge, PanelRadius * Program.Dpi))
                using (var pen = new Pen(Color.FromArgb(40, Color.White), Math.Max(1f, Program.Dpi)))
                    g.DrawPath(pen, ep);

                using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var near = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                {
                    var lr = LangRect;
                    var flagRect = new RectangleF(lr.X + Px(8), lr.Y + (lr.Height - Px(28)) / 2f, Px(42), Px(28));
                    if (!Flags.Draw(g, region, flagRect, 3f * Program.Dpi))
                        using (var f = new Font(Font.FontFamily, Font.Size * Program.Dpi, Font.Style))
                            g.DrawString(lang, f, Brushes.White, new RectangleF(lr.X + Px(8), lr.Y, lr.Width - Px(8), lr.Height), near);

                    using (var gb = new SolidBrush(Color.FromArgb(hover == -2 ? 255 : 215, Color.White)))
                        g.DrawString("\uE713", gearFont, gb, GearRect, center);

                    if (ButtonsVisible)
                    {
                        for (int i = 0; i < desktopCount; i++)
                        {
                            bool current = i == desktopIndex;
                            bool hovered = HoverRow(hover) == i;
                            int rowAlpha = current ? 255 : hovered ? 40 : 0;
                            if (rowAlpha > 0)
                                using (var rp = RoundedRect(RowRect(i), RowRadius * Program.Dpi))
                                using (var rb = new SolidBrush(Color.FromArgb(rowAlpha, Color.White)))
                                    g.FillPath(rb, rp);

                            var badge = BadgeRect(i);
                            using (var bp = RoundedRect(badge, badge.Height / 2f))
                            using (var bb = new SolidBrush(current ? back : Color.FromArgb(46, Color.White)))
                                g.FillPath(bb, bp);
                            g.DrawString((i + 1).ToString(), numFont, Brushes.White, badge, center);

                            string name = DesktopName(i) ?? "Рабочий стол " + (i + 1);
                            using (var tb = new SolidBrush(current ? back : Color.White))
                                g.DrawString(name, current ? nameBoldFont : nameFont, tb, NameRect(i), near);

                            if (hovered)
                            {
                                var er = EditRect(i);
                                bool onEdit = hover == EditHitBase - i;
                                if (onEdit)
                                    using (var hp = RoundedRect(er, 5f * Program.Dpi))
                                    using (var hb = new SolidBrush(current ? Color.FromArgb(40, back) : Color.FromArgb(46, Color.White)))
                                        g.FillPath(hb, hp);
                                using (var eb = new SolidBrush(current ? back : Color.White))
                                    g.DrawString("\uE70F", editFont, eb, er, center);
                            }
                        }
                    }
                }
            }
            Native.UpdateLayered(Handle, bmp, Location, 248);
        }
    }

    void SwitchLanguage()
    {
        uint thread;
        IntPtr focus;
        var fg = ForeignForeground(out thread, out focus);
        if (fg == IntPtr.Zero)
        {
            if (OwnForeground()) Native.ActivateKeyboardLayout(new IntPtr(1), 0);
            return;
        }

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

    static string RegionName(int langId)
    {
        try { return new RegionInfo(new CultureInfo(langId).Name).TwoLetterISORegionName.ToUpperInvariant(); }
        catch { return ""; }
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
        if (e.Button == MouseButtons.Right)
        {
            menuRow = HoverRow(HitTest(e.Location));
        }
        if (e.Button == MouseButtons.Left)
        {
            pressed = true;
            downPoint = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitTest(e.Location);
        Cursor = hit != -1 ? Cursors.Hand : Cursors.Default;
        if (hit != hover)
        {
            hover = hit;
            Render();
        }
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
        int hit = HitTest(e.Location);
        if (hit == -2) OpenSettings();
        else if (hit == -3) SwitchLanguage();
        else if (hit <= EditHitBase) RenameDesktop(EditHitBase - hit);
        else if (hit >= 0 && hit != desktopIndex) SwitchToDesktop(hit);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        int hit = HitTest(e.Location);
        if (hit >= 0) RenameDesktop(hit);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover == -1) return;
        hover = -1;
        Render();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        baseLocation = Location;
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
                if (s.WorkingArea.Contains(new Rectangle(saved.Value, Size))) return saved.Value;
        var wa = Screen.PrimaryScreen.WorkingArea;
        return new Point(wa.Right - Width - 20, wa.Bottom - Height - 20);
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

class RenameDialog : Form
{
    readonly TextBox box = new TextBox();

    public string NameText { get { return box.Text; } }

    public RenameDialog(int number, string name)
    {
        Text = "Имя рабочего стола";
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

        var title = new Label { Text = "Рабочий стол " + number, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        var hint = new Label
        {
            Text = "Пустое имя вернёт «Рабочий стол " + number + "»",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 4, 0, 16)
        };

        box.Width = 320;
        box.Margin = new Padding(0);
        box.Text = name;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(96, 0), Margin = new Padding(8, 0, 0, 0) };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(96, 0), Margin = new Padding(8, 0, 0, 0) };
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var table = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        table.Controls.Add(title, 0, 0);
        table.Controls.Add(box, 0, 1);
        table.Controls.Add(hint, 0, 2);
        table.Controls.Add(buttons, 0, 3);
        Controls.Add(table);
        ActiveControl = box;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        box.SelectAll();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.A) && box.Focused)
        {
            box.SelectAll();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

static class Flags
{
    static readonly Color White = Color.White;

    public static bool Draw(Graphics g, string region, RectangleF r, float radius)
    {
        Action<Graphics, RectangleF> paint;
        switch (region)
        {
            case "RU": paint = (gg, rr) => Horizontal(gg, rr, White, Color.FromArgb(0, 57, 166), Color.FromArgb(213, 43, 30)); break;
            case "UA": paint = (gg, rr) => Horizontal(gg, rr, Color.FromArgb(0, 87, 183), Color.FromArgb(255, 215, 0)); break;
            case "BY": paint = (gg, rr) => Belarus(gg, rr); break;
            case "DE": paint = (gg, rr) => Horizontal(gg, rr, Color.Black, Color.FromArgb(221, 0, 0), Color.FromArgb(255, 206, 0)); break;
            case "FR": paint = (gg, rr) => Vertical(gg, rr, Color.FromArgb(0, 85, 164), White, Color.FromArgb(239, 65, 53)); break;
            case "IT": paint = (gg, rr) => Vertical(gg, rr, Color.FromArgb(0, 146, 70), White, Color.FromArgb(206, 43, 55)); break;
            case "ES": paint = (gg, rr) => Spain(gg, rr); break;
            case "US": paint = (gg, rr) => UnitedStates(gg, rr); break;
            case "GB": paint = (gg, rr) => UnitedKingdom(gg, rr); break;
            default: return false;
        }

        using (var path = Rounded(r, radius))
        {
            var state = g.Save();
            g.SetClip(path);
            paint(g, r);
            g.Restore(state);
            using (var pen = new Pen(Color.FromArgb(90, Color.White), 1f))
                g.DrawPath(pen, path);
        }
        return true;
    }

    static void Fill(Graphics g, RectangleF r, Color c)
    {
        using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
    }

    static void Horizontal(Graphics g, RectangleF r, params Color[] colors)
    {
        float h = r.Height / colors.Length;
        for (int i = 0; i < colors.Length; i++)
            using (var b = new SolidBrush(colors[i]))
                g.FillRectangle(b, r.X, r.Y + i * h, r.Width, h + 0.5f);
    }

    static void Vertical(Graphics g, RectangleF r, params Color[] colors)
    {
        float w = r.Width / colors.Length;
        for (int i = 0; i < colors.Length; i++)
            using (var b = new SolidBrush(colors[i]))
                g.FillRectangle(b, r.X + i * w, r.Y, w + 0.5f, r.Height);
    }

    static void Spain(Graphics g, RectangleF r)
    {
        var red = Color.FromArgb(170, 21, 27);
        Fill(g, r, red);
        using (var b = new SolidBrush(Color.FromArgb(241, 191, 0)))
            g.FillRectangle(b, r.X, r.Y + r.Height / 4f, r.Width, r.Height / 2f);
    }

    static void Belarus(Graphics g, RectangleF r)
    {
        Fill(g, r, Color.FromArgb(0, 151, 57));
        using (var b = new SolidBrush(Color.FromArgb(200, 49, 62)))
            g.FillRectangle(b, r.X, r.Y, r.Width, r.Height * 2f / 3f);
        using (var b = new SolidBrush(White))
            g.FillRectangle(b, r.X, r.Y, r.Width * 0.11f, r.Height);
    }

    static void UnitedStates(Graphics g, RectangleF r)
    {
        var red = Color.FromArgb(178, 34, 52);
        float stripe = r.Height / 13f;
        Fill(g, r, White);
        using (var b = new SolidBrush(red))
            for (int i = 0; i < 13; i += 2)
                g.FillRectangle(b, r.X, r.Y + i * stripe, r.Width, stripe);
        var canton = new RectangleF(r.X, r.Y, r.Width * 0.4f, stripe * 7);
        using (var b = new SolidBrush(Color.FromArgb(60, 59, 110)))
            g.FillRectangle(b, canton);
        float dot = Math.Max(1f, canton.Height / 9f);
        using (var b = new SolidBrush(White))
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 5; col++)
                {
                    float x = canton.X + canton.Width * (col + 0.5f + (row % 2) * 0.25f - 0.1f) / 5.2f;
                    float y = canton.Y + canton.Height * (row + 0.6f) / 4.4f;
                    g.FillEllipse(b, x - dot / 2, y - dot / 2, dot, dot);
                }
    }

    static void UnitedKingdom(Graphics g, RectangleF r)
    {
        var blue = Color.FromArgb(1, 33, 105);
        var red = Color.FromArgb(200, 16, 46);
        Fill(g, r, blue);
        float h = r.Height;
        using (var pen = new Pen(White, h * 0.2f))
        {
            g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
            g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom);
        }
        using (var pen = new Pen(red, h * 0.07f))
        {
            g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
            g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom);
        }
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        using (var b = new SolidBrush(White))
        {
            g.FillRectangle(b, cx - h * 0.17f, r.Top, h * 0.34f, h);
            g.FillRectangle(b, r.Left, cy - h * 0.17f, r.Width, h * 0.34f);
        }
        using (var b = new SolidBrush(red))
        {
            g.FillRectangle(b, cx - h * 0.1f, r.Top, h * 0.2f, h);
            g.FillRectangle(b, r.Left, cy - h * 0.1f, r.Width, h * 0.2f);
        }
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
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
