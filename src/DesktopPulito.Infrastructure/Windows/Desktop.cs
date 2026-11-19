#nullable disable
// Il "motore": desktop, dissolvenza, barra delle applicazioni, scorciatoie, doppio clic, elementi del desktop.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// ───────────── Icone del desktop ─────────────
static class Desk
{
    // La vista delle icone (SHELLDLL_DefView) vive sotto Progman o sotto un WorkerW.
    public static IntPtr DefView()
    {
        IntPtr dv = N.FindWindowEx(N.FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        IntPtr w = IntPtr.Zero;
        while (dv == IntPtr.Zero)
        {
            w = N.FindWindowEx(IntPtr.Zero, w, "WorkerW", null);
            if (w == IntPtr.Zero) break;
            dv = N.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        return dv;
    }

    public static bool IconsVisible()
    {
        IntPtr dv = DefView();
        if (dv == IntPtr.Zero) return true;
        IntPtr lv = N.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
        return lv == IntPtr.Zero || N.IsWindowVisible(lv);
    }

    // Comando interno di Explorer "Mostra icone del desktop". Timeout: se Explorer è bloccato non ci blocca.
    public static void ToggleRaw()
    {
        IntPtr dv = DefView();
        IntPtr r;
        if (dv != IntPtr.Zero) N.SendMessageTimeout(dv, 0x0111, (IntPtr)0x7402, IntPtr.Zero, 2, 1500, out r);
    }

    public static string Cls(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(64);
        N.GetClassName(h, sb, 64);
        return sb.ToString();
    }

    public static bool IsDesktop(IntPtr h)
    {
        if (h == IntPtr.Zero) return false;
        string c = Cls(h);
        return c == "Progman" || c == "WorkerW";
    }

    public static bool IsDesktopAt(N.POINT p)
    {
        IntPtr h = N.WindowFromPoint(p);
        for (int i = 0; i < 4 && h != IntPtr.Zero; i++)
        {
            string c = Cls(h);
            if (c == "SHELLDLL_DefView" || c == "SysListView32" || c == "Progman" || c == "WorkerW") return true;
            h = N.GetParent(h);
        }
        return false;
    }

    public static bool CursorOnDesktop()
    {
        N.POINT p;
        N.GetCursorPos(out p);
        return IsDesktopAt(p);
    }

    // Rettangoli (in pixel dello schermo) di tutte le icone del desktop, letti tramite l'accessibilità di Windows.
    // Servono agli effetti che muovono le icone. Si leggono quando le icone sono visibili e si tengono in memoria.
    static List<Rectangle> iconRects;
    public static List<Rectangle> IconRects { get { if (iconRects == null) LoadIconRects(); return iconRects; } }

    // Le posizioni si conservano nel registro (≈3 KB): pronte anche se l'app parte con le icone nascoste.
    static void SaveIconRects(List<Rectangle> l)
    {
        try
        {
            byte[] b = new byte[l.Count * 16];
            for (int i = 0; i < l.Count; i++)
            {
                BitConverter.GetBytes(l[i].X).CopyTo(b, i * 16);
                BitConverter.GetBytes(l[i].Y).CopyTo(b, i * 16 + 4);
                BitConverter.GetBytes(l[i].Width).CopyTo(b, i * 16 + 8);
                BitConverter.GetBytes(l[i].Height).CopyTo(b, i * 16 + 12);
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\DesktopPulito")) k.SetValue("IconRects", b, RegistryValueKind.Binary);
        }
        catch { }
    }

    static void LoadIconRects()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\DesktopPulito"))
            {
                byte[] b = k == null ? null : k.GetValue("IconRects") as byte[];
                if (b == null || b.Length < 16) return;
                List<Rectangle> l = new List<Rectangle>(b.Length / 16);
                for (int i = 0; i + 16 <= b.Length; i += 16)
                    l.Add(new Rectangle(BitConverter.ToInt32(b, i), BitConverter.ToInt32(b, i + 4), BitConverter.ToInt32(b, i + 8), BitConverter.ToInt32(b, i + 12)));
                iconRects = l;
            }
        }
        catch { }
    }

    public static void RefreshIconRects()
    {
        try
        {
            IntPtr dv = DefView();
            IntPtr lv = dv == IntPtr.Zero ? IntPtr.Zero : N.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
            if (lv == IntPtr.Zero || !N.IsWindowVisible(lv)) return;
            Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            object o;
            if (N.AccessibleObjectFromWindow(lv, 0xFFFFFFFC, ref iid, out o) != 0) return;
            Accessibility.IAccessible acc = o as Accessibility.IAccessible;
            if (acc == null) return;
            int n = acc.accChildCount;
            List<Rectangle> list = new List<Rectangle>(n);
            for (int i = 1; i <= n; i++)
            {
                int x, y, w, h;
                try { acc.accLocation(out x, out y, out w, out h, i); } catch { continue; }
                if (w > 0 && h > 0) list.Add(new Rectangle(x, y, w, h));
            }
            if (list.Count > 0)
            {
                bool changed = iconRects == null || iconRects.Count != list.Count;
                if (!changed) for (int i = 0; i < list.Count && !changed; i++) changed = list[i] != iconRects[i];
                iconRects = list;
                if (changed) SaveIconRects(list);
            }
        }
        catch { }
    }

    // C'è un'icona sotto il punto? (accessibilità di Windows: 0x22 = elemento di lista)
    public static bool IconAt(N.POINT p)
    {
        try
        {
            Accessibility.IAccessible acc;
            object child;
            if (N.AccessibleObjectFromPoint(p, out acc, out child) != 0 || acc == null) return true;
            object role = acc.get_accRole(child);
            return role is int && (int)role == 0x22;
        }
        catch { return true; }   // nel dubbio non tocchiamo niente
    }
}

// ───────────── Modalità Zen: barra delle applicazioni a scomparsa ─────────────
static class Zen
{
    const string KEY = @"Software\DesktopPulito";
    static int saved = -1;

    static N.APPBARDATA Data()
    {
        N.APPBARDATA d = new N.APPBARDATA();
        d.cbSize = Marshal.SizeOf(typeof(N.APPBARDATA));
        d.hWnd = N.FindWindow("Shell_TrayWnd", null);
        return d;
    }

    static int Get() { N.APPBARDATA d = Data(); return (int)N.SHAppBarMessage(4, ref d); }
    static void Set(int st) { N.APPBARDATA d = Data(); d.lParam = (IntPtr)st; N.SHAppBarMessage(10, ref d); }

    static void Persist(int v)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KEY)) k.SetValue("ZenSaved", v, RegistryValueKind.DWord);
    }

    public static void Apply(bool hide)
    {
        if (hide)
        {
            if (saved >= 0) return;
            saved = Get();
            Persist(saved);
            Set(saved | 1);          // ABS_AUTOHIDE
        }
        else if (saved >= 0)
        {
            Set(saved);
            saved = -1;
            Persist(-1);
        }
    }

    // Se l'app si era chiusa male con la barra nascosta, rimettiamo com'era.
    public static void Recover()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KEY))
        {
            object v = k == null ? null : k.GetValue("ZenSaved");
            if (v is int && (int)v >= 0) { Set((int)v); Persist(-1); }
        }
    }
}

// ───────────── Scorciatoie da tastiera globali ─────────────
static class HotkeyCodec
{
    public const int ALT = 1, CTRL = 2, SHIFT = 4, WIN = 8;

    public static int Make(int mods, Keys k) { return (mods << 16) | (int)k; }
    public static int Mods(int c) { return (c >> 16) & 0xF; }
    public static int Vk(int c) { return c & 0xFFFF; }

    public static List<string> Parts(int c)
    {
        List<string> p = new List<string>();
        if (c <= 0) return p;
        int m = Mods(c);
        if ((m & CTRL) != 0) p.Add("Ctrl");
        if ((m & ALT) != 0) p.Add("Alt");
        if ((m & SHIFT) != 0) p.Add("Maiusc");
        if ((m & WIN) != 0) p.Add("Win");
        p.Add(KeyName((Keys)Vk(c)));
        return p;
    }

    public static string Format(int c) { return c <= 0 ? "" : string.Join(" + ", Parts(c).ToArray()); }

    static string KeyName(Keys k)
    {
        switch (k)
        {
            case Keys.Space: return "Spazio";
            case Keys.Return: return "Invio";
            case Keys.Escape: return "Esc";
            case Keys.Back: return "Backspace";
            case Keys.Tab: return "Tab";
            case Keys.Up: return "↑";
            case Keys.Down: return "↓";
            case Keys.Left: return "←";
            case Keys.Right: return "→";
            case Keys.OemPeriod: return ".";
            case Keys.Oemcomma: return ",";
            case Keys.OemMinus: return "-";
            case Keys.Oemplus: return "+";
        }
        if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        return k.ToString();
    }

    // Da tasto premuto a combinazione; -1 = non valida (solo modificatori o senza modificatori).
    public static int FromKeys(Keys keyData)
    {
        Keys k = keyData & Keys.KeyCode;
        if (k == Keys.ShiftKey || k == Keys.ControlKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin || k == Keys.None) return -1;
        int m = 0;
        if ((keyData & Keys.Control) != 0) m |= CTRL;
        if ((keyData & Keys.Alt) != 0) m |= ALT;
        if ((keyData & Keys.Shift) != 0) m |= SHIFT;
        if ((N.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (N.GetAsyncKeyState(0x5C) & 0x8000) != 0) m |= WIN;
        bool fkey = k >= Keys.F1 && k <= Keys.F24;
        if (m == 0 && !fkey) return -1;
        return Make(m, k);
    }
}

class HotkeyWindow : NativeWindow
{
    public event Action<int> Pressed;   // id della scorciatoia premuta: decide l'interfaccia cosa fare

    public HotkeyWindow() { CreateHandle(new CreateParams()); }

    public bool Register(int id, int combo)
    {
        N.UnregisterHotKey(Handle, id);
        if (combo <= 0) return true;
        return N.RegisterHotKey(Handle, id, (uint)(HotkeyCodec.Mods(combo) | 0x4000), (uint)HotkeyCodec.Vk(combo));
    }

    public void Unregister(int id) { N.UnregisterHotKey(Handle, id); }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312)
        {
            int id = m.WParam.ToInt32();
            Action<int> h = Pressed;
            if (h != null) h(id);
            return;
        }
        base.WndProc(ref m);
    }
}

// ───────────── Doppio clic sul desktop ─────────────
// Hook del mouse su un thread dedicato: risponde in microsecondi e non dipende dall'interfaccia.
// Rileva solo il "tempo" del doppio clic; la verifica di dove è avvenuto si fa dopo, sul thread principale.
class DoubleClickHook
{
    Thread th;
    uint tid;
    IntPtr hook;
    N.LowLevelProc proc;
    uint lastTime;
    N.POINT lastPt;
    int dblTime, dx, dy;
    readonly ManualResetEvent ready = new ManualResetEvent(false);

    public bool Running { get { return th != null; } }
    Control invoker;
    Action<N.POINT> onDouble;

    // invoker: dove eseguire la risposta (thread dell'interfaccia); cb: cosa fare al doppio clic
    public void Start(Control inv, Action<N.POINT> cb) { invoker = inv; onDouble = cb; Start(); }

    public void Start()
    {
        if (th != null) return;
        dblTime = (int)N.GetDoubleClickTime();
        dx = Math.Max(4, N.GetSystemMetrics(36));
        dy = Math.Max(4, N.GetSystemMetrics(37));
        ready.Reset();
        th = new Thread(Loop);
        th.IsBackground = true;
        th.Name = "DoubleClickHook";
        th.Start();
        ready.WaitOne(1000);
    }

    public void Stop()
    {
        if (th == null) return;
        N.PostThreadMessage(tid, 0x0012, IntPtr.Zero, IntPtr.Zero);   // WM_QUIT
        th.Join(500);
        th = null;
    }

    void Loop()
    {
        tid = N.GetCurrentThreadId();
        proc = Proc;
        hook = N.SetWindowsHookEx(14, proc, N.GetModuleHandle(null), 0);   // WH_MOUSE_LL
        ready.Set();
        N.MSG m;
        while (N.GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
        if (hook != IntPtr.Zero) N.UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    IntPtr Proc(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0 && w.ToInt32() == 0x0201)   // WM_LBUTTONDOWN
        {
            N.MSLLHOOKSTRUCT d = (N.MSLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(N.MSLLHOOKSTRUCT));
            int dt = unchecked((int)(d.time - lastTime));
            if (lastTime != 0 && dt >= 0 && dt <= dblTime && Math.Abs(d.pt.X - lastPt.X) <= dx && Math.Abs(d.pt.Y - lastPt.Y) <= dy)
            {
                lastTime = 0;
                N.POINT p = d.pt;
                Control inv = invoker; Action<N.POINT> cb = onDouble;
                if (inv != null && cb != null) inv.BeginInvoke((MethodInvoker)delegate { cb(p); });
            }
            else
            {
                lastTime = d.time == 0 ? 1 : d.time;
                lastPt = d.pt;
            }
        }
        return N.CallNextHookEx(IntPtr.Zero, code, w, l);
    }
}

// ───────────── Elementi del desktop (per la ricerca) ─────────────
class DItem
{
    public string Path, Name, Key, Kind;
    public bool Dir, Loaded;
    public Bitmap Icon;
}

static class DesktopItems
{
    static string[] Dirs()
    {
        return new string[] {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) };
    }

    static bool Skip(string path, out bool dir)
    {
        dir = false;
        try
        {
            FileAttributes a = File.GetAttributes(path);
            dir = (a & FileAttributes.Directory) != 0;
            return (a & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch { return true; }
    }

    public static List<DItem> Load()
    {
        List<DItem> list = new List<DItem>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string d in Dirs())
        {
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(d); } catch { continue; }
            foreach (string p in entries)
            {
                bool dir;
                if (Skip(p, out dir)) continue;
                string ext = Path.GetExtension(p).ToLowerInvariant();
                string name = (!dir && (ext == ".lnk" || ext == ".url")) ? Path.GetFileNameWithoutExtension(p) : Path.GetFileName(p);
                if (!seen.Add(name + "|" + dir)) continue;
                DItem it = new DItem();
                it.Path = p; it.Name = name; it.Dir = dir; it.Key = Norm(name);
                list.Add(it);
            }
        }
        return list;
    }

    public static int Count()
    {
        int n = 0;
        foreach (string d in Dirs())
        {
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
            try
            {
                foreach (string p in Directory.GetFileSystemEntries(d))
                {
                    bool dir;
                    if (!Skip(p, out dir)) n++;
                }
            }
            catch { }
        }
        return n;
    }

    public static string Norm(string s)
    {
        string f = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder(f.Length);
        foreach (char c in f)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    // Punteggio di somiglianza: inizio del nome > inizio di una parola > contenuto > lettere in ordine.
    public static float Score(DItem it, string q)
    {
        if (q.Length == 0) return 1;
        string k = it.Key;
        if (k.StartsWith(q)) return 4;
        int i = k.IndexOf(q);
        if (i > 0)
        {
            char prev = k[i - 1];
            return (prev == ' ' || prev == '-' || prev == '_' || prev == '.') ? 3 : 2;
        }
        int j = 0;
        foreach (char c in k) if (j < q.Length && c == q[j]) j++;
        return j == q.Length ? 0.5f : 0;
    }

    public static void LoadIcon(DItem d)
    {
        if (d.Loaded) return;
        d.Loaded = true;
        N.SHFILEINFO fi = new N.SHFILEINFO();
        N.SHGetFileInfo(d.Path, 0, ref fi, (uint)Marshal.SizeOf(fi), 0x100 | 0x400);   // ICON | TYPENAME (grande)
        if (fi.hIcon != IntPtr.Zero)
        {
            try { using (Icon ic = Icon.FromHandle(fi.hIcon)) d.Icon = ic.ToBitmap(); }
            catch { }
            N.DestroyIcon(fi.hIcon);
        }
        d.Kind = string.IsNullOrEmpty(fi.szTypeName) ? (d.Dir ? "Cartella" : "File") : fi.szTypeName;
    }

    public static void Open(DItem d, bool reveal)
    {
        try
        {
            if (reveal) Process.Start("explorer.exe", "/select,\"" + d.Path + "\"");
            else Process.Start(new ProcessStartInfo(d.Path) { UseShellExecute = true });
        }
        catch (Exception e) { MessageBox.Show("Impossibile aprire:\n" + d.Path + "\n\n" + e.Message, "Desktop Pulito"); }
    }

    public static void OpenDesktopFolder()
    {
        try { Process.Start("explorer.exe", "\"" + Dirs()[0] + "\""); } catch { }
    }
}
