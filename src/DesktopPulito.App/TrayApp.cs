#nullable disable
// Logica dell'app: icona vicino all'orologio, stato, auto-nascondi, Zen, scorciatoie, finestre.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using DesktopPulito.Domain;
using DesktopPulito.Infrastructure.Windows;

static class App
{
    public const string Version = "1.0.0";
    public static Config Cfg;
    public static bool Shown = true;
    public static Control Invoker;
    public static bool HotToggleOk = true, HotSearchOk = true, HotSceneOk = true;
    public static int DesktopCount;

    const int FRAMES = 7;
    static NotifyIcon tray;
    static Icon[] frames;
    static int frame, frameTarget;
    static readonly System.Windows.Forms.Timer trayAnim = new System.Windows.Forms.Timer();
    static readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer();
    static readonly System.Windows.Forms.Timer trimTimer = new System.Windows.Forms.Timer();
    static readonly System.Windows.Forms.Timer pauseTimer = new System.Windows.Forms.Timer();
    static SettingsForm settings;
    static SearchPanel search;
    static GlassMenu menu;
    static HotkeyWindow hot;
    static readonly DoubleClickHook dbl = new DoubleClickHook();
    static IntPtr fgHook;
    static N.WinEventProc fgProc;   // tenuto in vita per il GC
    static int lastAct;
    static bool busy, hotSuspended, paused;
    static string themeSig = "";

    public static bool Paused { get { return paused; } }

    public static void Run(bool silent)
    {
        Cfg = Config.Load();
        Lang.Apply(Cfg);
        T.Load(Cfg);
        themeSig = ThemeSig();
        Invoker = new Control();
        Invoker.CreateControl();
        Zen.Recover();

        BuildFrames();
        tray = new NotifyIcon();
        tray.MouseUp += OnTrayMouse;

        trayAnim.Interval = 22;
        trayAnim.Tick += delegate { TrayStep(); };
        watch.Tick += delegate { WatchTick(); };
        trimTimer.Interval = 2500;
        trimTimer.Tick += delegate { trimTimer.Stop(); Trim(); };
        pauseTimer.Tick += delegate { SetPause(false); };

        Shown = Desk.IconsVisible();
        if (Shown) Desk.RefreshIconRects();
        frame = frameTarget = Shown ? 0 : FRAMES - 1;
        UpdateTray();
        tray.Visible = true;

        hot = new HotkeyWindow();
        hot.Pressed += delegate(int id)
        {
            if (id == 1) Toggle();
            else if (id == 2) ShowSearch();
            else if (id == 3) NextScene();
        };
        RegisterHotkeys();
        ApplyDoubleClick();
        SyncZen();

        lastAct = Environment.TickCount;
        UpdateWatch();

        SystemEvents.UserPreferenceChanged += delegate { CheckTheme(); };
        SystemEvents.SessionEnding += delegate { RestoreAll(); };

        // Se apri l'app una seconda volta, questa istanza mostra le impostazioni.
        EventWaitHandle open = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopPulito_Open");
        ThreadPool.RegisterWaitForSingleObject(open, delegate { Invoker.BeginInvoke((MethodInvoker)ShowSettings); }, null, -1, false);
        // comandi da riga di comando per automazioni: DesktopPulito.exe /toggle  oppure  /search
        EventWaitHandle evToggle = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopPulito_Toggle");
        ThreadPool.RegisterWaitForSingleObject(evToggle, delegate { Invoker.BeginInvoke((MethodInvoker)delegate { Toggle(); }); }, null, -1, false);
        EventWaitHandle evSearch = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopPulito_Search");
        ThreadPool.RegisterWaitForSingleObject(evSearch, delegate { Invoker.BeginInvoke((MethodInvoker)ShowSearch); }, null, -1, false);

        // effetti sulla scheda video: shader compilati in background, così la prima transizione parte subito
        if (Cfg.FadeStyle >= 7) ThreadPool.QueueUserWorkItem(delegate { try { DesktopPulito.Infrastructure.Gpu.GpuTransition.Warmup(); } catch { } });
        Widgets.Sync();
        if (!silent) ShowSettings();
        ScheduleTrim();
        Application.Run();
        Widgets.CloseAll();

        dbl.Stop();
        Unhook();
        tray.Dispose();
    }

    // ── icona nell'area di notifica ──
    static void BuildFrames()
    {
        int n;
        try { n = N.GetSystemMetricsForDpi(49, N.SystemDpi()); } catch { n = SystemInformation.SmallIconSize.Width; }
        Icon[] old = frames;
        frames = new Icon[FRAMES];
        for (int i = 0; i < FRAMES; i++) frames[i] = Art.TrayFrame(n, Cfg.TrayStyle == 1, i / (float)(FRAMES - 1));
        if (tray != null) tray.Icon = frames[frame];
        if (old != null) foreach (Icon ic in old) ic.Dispose();
    }

    static void SetTrayState(bool shown, bool animate)
    {
        Shown = shown;
        frameTarget = shown ? 0 : FRAMES - 1;
        if (animate && T.Motion) trayAnim.Start(); else frame = frameTarget;
        UpdateTray();
        if (settings != null) settings.Invalidate();
    }

    static void TrayStep()
    {
        if (frame == frameTarget) { trayAnim.Stop(); return; }
        frame += frame < frameTarget ? 1 : -1;
        tray.Icon = frames[frame];
    }

    static void UpdateTray()
    {
        tray.Icon = frames[frame];
        string st = Shown ? Lang.L("icone visibili", "icons visible") : Lang.L("icone nascoste", "icons hidden");
        if (Cfg.AutoHide) st += paused ? Lang.L(" · in pausa", " · paused") : " · auto";
        tray.Text = "Desktop Pulito · " + st;
    }

    static void OnTrayMouse(object s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Toggle();
        else if (e.Button == MouseButtons.Middle) ShowSearch();
        else if (e.Button == MouseButtons.Right) ShowMenu();
    }

    static void ShowMenu()
    {
        if (menu != null && !menu.IsDisposed) menu.Close();
        bool vis = Desk.IconsVisible();
        SetTrayState(vis, false);
        List<MItem> it = new List<MItem>();
        it.Add(new MItem { Text = vis ? Lang.L("Nascondi icone", "Hide icons") : Lang.L("Mostra icone", "Show icons"), Hint = HotkeyCodec.Format(Cfg.HotToggle), Bold = true, Act = Toggle });
        it.Add(new MItem { Text = Lang.L("Cerca sul desktop", "Search desktop"), Hint = HotkeyCodec.Format(Cfg.HotSearch), Act = ShowSearch });
        it.Add(new MItem { Sep = true });
        it.Add(new MItem { Text = Lang.L("Nascondi automaticamente", "Auto-hide"), HasCheck = true, Check = Cfg.AutoHide, Act = delegate { Cfg.AutoHide = !Cfg.AutoHide; ConfigChanged(); } });
        it.Add(new MItem { Text = paused ? Lang.L("Riprendi", "Resume") : Lang.L("Pausa per un'ora", "Pause for an hour"), Disabled = !Cfg.AutoHide, Act = delegate { SetPause(!paused); } });
        it.Add(new MItem { Text = Lang.L("Modalità Zen", "Zen mode"), HasCheck = true, Check = Cfg.Zen, Act = delegate { Cfg.Zen = !Cfg.Zen; ConfigChanged(); } });
        it.Add(new MItem { Sep = true });
        it.Add(new MItem { Text = Lang.L("Impostazioni", "Settings"), Act = ShowSettings });
        it.Add(new MItem { Text = Lang.L("Esci", "Quit"), Act = Exit });
        menu = new GlassMenu(it);
        menu.FormClosed += delegate { menu.Dispose(); menu = null; ScheduleTrim(); };
        menu.ShowAt(Cursor.Position);
    }

    public static void Later(Action a) { Later(a, 180); }

    public static void Later(Action a, int ms)
    {
        System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        t.Interval = ms;
        t.Tick += delegate { t.Stop(); t.Dispose(); a(); };
        t.Start();
    }

    // ── tema ──
    static string ThemeSig() { return Lang.En + "|" + T.Dark + "|" + T.SysDark + "|" + T.Motion + "|" + T.P.Id + T.P.C[0].ToArgb() + "|" + Cfg.Glass + "|" + Cfg.TrayStyle; }

    static void CheckTheme()
    {
        Lang.Apply(Cfg);
        T.Load(Cfg);
        string sig = ThemeSig();
        if (sig == themeSig) return;
        themeSig = sig;
        Art.ClearCache();
        BuildFrames();
        if (settings != null) settings.ApplyChrome();
        if (search != null) search.ApplyChrome();
    }

    // ── azioni ──
    public static void Toggle() { SetVisible(!Desk.IconsVisible()); }

    // Scena successiva (scorciatoia Ctrl+Alt+S): sfondo, icone e widget della scena, con la sua transizione.
    public static void NextScene() { ActivateScene(null); }

    // Attiva una scena precisa (dalla pagina Scene) o, con null, la successiva.
    public static void ActivateScene(SceneId? id)
    {
        if (busy) return;
        busy = true;
        try { if (id.HasValue) Services.SwitchScene.To(id.Value); else Services.SwitchScene.Next(); }
        catch (DomainException) { }
        finally { busy = false; }
        SetTrayState(Desk.IconsVisible(), true);
        Widgets.Sync();
        SyncZen();
        UpdateWatch();
        ScheduleTrim();
    }

    // Anteprima dell'effetto scelto: le icone cambiano stato e poi tornano come prima.
    public static void PreviewTransition()
    {
        if (busy) return;
        Toggle();
        Later(Toggle, 900);
    }

    static void SetVisible(bool show)
    {
        if (busy) return;
        if (Desk.IconsVisible() != show)
        {
            busy = true;
            try
            {
                Services.Icons.SetVisible(show, new TransitionSpec((TransitionStyle)Cfg.FadeStyle, (TransitionSpeed)Cfg.FadeSpeed, Cfg.Fade && T.Motion));
            }
            finally { busy = false; }
        }
        if (show) lastAct = Environment.TickCount;
        SetTrayState(Desk.IconsVisible(), true);
        SyncZen();
        UpdateWatch();
        if (Shown) Later(Desk.RefreshIconRects);
        ScheduleTrim();
    }

    static void SyncZen() { Zen.Apply(Cfg.Zen && !Shown); }

    public static void ConfigChanged()
    {
        Cfg.Save();
        lastAct = Environment.TickCount;
        if (!Cfg.AutoHide && paused) SetPause(false);
        CheckTheme();
        UpdateTray();
        RegisterHotkeys();
        ApplyDoubleClick();
        SyncZen();
        UpdateWatch();
        if (settings != null) settings.Sync();
        Widgets.Restyle();
    }

    public static void ResetConfig()
    {
        Cfg = Config.Defaults(Cfg);
        Autostart.Set(false);
        ConfigChanged();
    }

    public static void SetPause(bool on)
    {
        paused = on && Cfg.AutoHide;
        pauseTimer.Stop();
        if (paused) { pauseTimer.Interval = 60 * 60 * 1000; pauseTimer.Start(); }
        lastAct = Environment.TickCount;
        UpdateTray();
        UpdateWatch();
        if (settings != null) settings.Kick();
    }

    public static string StatusLine()
    {
        string s = DesktopCount + (DesktopCount == 1 ? Lang.L(" elemento sul desktop", " item on your desktop") : Lang.L(" elementi sul desktop", " items on your desktop"));
        if (!Shown)
        {
            string k = HotkeyCodec.Format(Cfg.HotToggle);
            return s + (k.Length > 0 ? "  ·  " + k + Lang.L(" per mostrarle", " to show them") : "");
        }
        if (!Cfg.AutoHide) return s;
        if (paused) return s + Lang.L("  ·  auto-nascondi in pausa", "  ·  auto-hide paused");
        int left = Math.Max(0, Cfg.Delay - unchecked(Environment.TickCount - lastAct) / 1000);
        return s + Lang.L("  ·  si nascondono tra ", "  ·  hiding in ") + (left >= 60 ? (left / 60) + " min " + (left % 60) + " s" : left + " s");
    }

    public static void ShowSettings()
    {
        if (settings != null && !settings.IsDisposed)
        {
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
            return;
        }
        DesktopCount = DesktopItems.Count();
        SetTrayState(Desk.IconsVisible(), false);
        settings = new SettingsForm();
        settings.FormClosed += delegate { settings.Dispose(); settings = null; Art.ClearCache(); ScheduleTrim(); };
        settings.Show();
        settings.Activate();
    }

    public static void ShowSearch()
    {
        if (search != null && !search.IsDisposed) { search.Activate(); return; }
        search = new SearchPanel();
        search.FormClosed += delegate { search.Dispose(); search = null; ScheduleTrim(); };
        search.Show();
        search.Activate();
        N.SetForegroundWindow(search.Handle);
    }

    static void RestoreAll()
    {
        if (!Desk.IconsVisible()) Desk.ToggleRaw();
        Zen.Apply(false);
    }

    public static void Uninstall()
    {
        RestoreAll();
        Autostart.Uninstall();
        tray.Visible = false;
        Application.Exit();
    }

    static void Exit()
    {
        RestoreAll();   // all'uscita icone e barra tornano sempre
        tray.Visible = false;
        Application.Exit();
    }

    // ── scorciatoie ──
    static void RegisterHotkeys()
    {
        if (hot == null) return;
        if (hotSuspended) { hot.Unregister(1); hot.Unregister(2); return; }
        HotToggleOk = hot.Register(1, Cfg.HotToggle);
        HotSearchOk = hot.Register(2, Cfg.HotSearch);
        HotSceneOk = hot.Register(3, Cfg.HotNextScene);
    }

    // Mentre registri una nuova scorciatoia, quelle attuali vanno spente (altrimenti le "mangerebbero").
    public static void SuspendHotkeys(bool on)
    {
        hotSuspended = on;
        RegisterHotkeys();
    }

    // ── doppio clic sul desktop ──
    static void ApplyDoubleClick()
    {
        if (Cfg.DoubleClick) dbl.Start(Invoker, OnDesktopDoubleClick); else dbl.Stop();
    }

    public static void OnDesktopDoubleClick(N.POINT p)
    {
        if (busy || !Desk.IsDesktopAt(p)) return;
        if (!Desk.IconsVisible()) SetVisible(true);
        else if (!Desk.IconAt(p)) SetVisible(false);
    }

    // ── auto-nascondi, guidato dagli eventi ──
    // icone visibili            → controllo ogni 1 s se è passato il tempo di inattività
    // nascoste + desktop attivo → controllo leggero ogni 100 ms del clic sullo sfondo
    // nascoste + altra finestra → nessun timer: ci sveglia Windows quando torni al desktop
    static void UpdateWatch()
    {
        if (!Cfg.AutoHide) { watch.Stop(); Unhook(); return; }
        Hook();
        if (Desk.IconsVisible()) { watch.Interval = 1000; watch.Start(); }
        else if (Desk.IsDesktop(N.GetForegroundWindow())) { watch.Interval = 100; watch.Start(); }
        else watch.Stop();
    }

    static void WatchTick()
    {
        if (busy) return;
        bool vis = Desk.IconsVisible();
        if (vis != Shown) { SetTrayState(vis, true); SyncZen(); }
        bool onDesk = Desk.IsDesktop(N.GetForegroundWindow());

        if (vis)
        {
            if (watch.Interval != 1000) watch.Interval = 1000;
            if (paused) { lastAct = Environment.TickCount; return; }
            if (onDesk)
            {
                N.LASTINPUTINFO li = new N.LASTINPUTINFO();
                li.cbSize = (uint)Marshal.SizeOf(li);
                if (N.GetLastInputInfo(ref li) && unchecked((int)li.dwTime - lastAct) > 0) lastAct = (int)li.dwTime;
            }
            if (search != null || menu != null) lastAct = Environment.TickCount;
            if (unchecked(Environment.TickCount - lastAct) >= Cfg.Delay * 1000) SetVisible(false);
        }
        else
        {
            if (!onDesk) { UpdateWatch(); return; }
            if (watch.Interval != 100) watch.Interval = 100;
            if ((N.GetAsyncKeyState(0x01) & 0x8001) != 0 && Desk.CursorOnDesktop()) SetVisible(true);
        }
    }

    static void Hook()
    {
        if (fgHook != IntPtr.Zero) return;
        fgProc = OnForeground;
        fgHook = N.SetWinEventHook(3, 3, IntPtr.Zero, fgProc, 0, 0, 0x0002);   // EVENT_SYSTEM_FOREGROUND, fuori processo
    }

    static void Unhook()
    {
        if (fgHook == IntPtr.Zero) return;
        N.UnhookWinEvent(fgHook);
        fgHook = IntPtr.Zero;
    }

    static void OnForeground(IntPtr h, uint ev, IntPtr hwnd, int o, int c, uint th, uint t)
    {
        if (busy) return;
        if (Desk.IsDesktop(hwnd) && !Desk.IconsVisible()) SetVisible(true);   // clic sul desktop o Win+D
        else UpdateWatch();
    }

    // ── memoria ──
    public static void ScheduleTrim() { trimTimer.Stop(); trimTimer.Start(); }

    static void Trim()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        N.SetProcessWorkingSetSize(N.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Pixel reali su ogni monitor (serve alla dissolvenza con schermi a scala diversa).
        try { N.SetProcessDpiAwarenessContext((IntPtr)(-4)); }
        catch { try { N.SetProcessDPIAware(); } catch { } }

        if (args.Length == 2 && args[0] == "/makeicon") { Art.WriteIco(args[1]); return; }
        if (args.Length == 2 && args[0] == "/blurtest") { Fader.BlurTest(args[1]); return; }   // diagnostica
        if (args.Length == 2 && args[0] == "/iconrects")                                          // diagnostica
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            Desk.RefreshIconRects();
            System.Collections.Generic.List<System.Drawing.Rectangle> l = Desk.IconRects;
            System.IO.File.WriteAllText(args[1], (l == null ? 0 : l.Count) + " icone in " + sw.ElapsedMilliseconds + " ms" + (l != null && l.Count > 0 ? "  prima: " + l[0] + "  ultima: " + l[l.Count - 1] : ""));
            return;
        }
        bool silent = Array.IndexOf(args, "/tray") >= 0 || Array.IndexOf(args, "/toggle") >= 0 || Array.IndexOf(args, "/search") >= 0;

        bool created;
        using (Mutex m = new Mutex(true, "Local\\DesktopPulito_v2", out created))
        {
            if (!created)
            {
                string ev = Array.IndexOf(args, "/toggle") >= 0 ? "Toggle" : Array.IndexOf(args, "/search") >= 0 ? "Search" : "Open";
                try { EventWaitHandle.OpenExisting("Local\\DesktopPulito_" + ev).Set(); } catch { }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            App.Run(silent);
        }
    }
}
