#nullable disable
// Finestra impostazioni: vetro, barra laterale, 5 pagine, gradiente animato in "Home".
using DesktopPulito.Infrastructure.Windows;
using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Scheda principale con gradiente mesh animato, stato e pulsanti.
class Hero : El
{
    readonly MeshCanvas mesh = new MeshCanvas();
    public float Time = 1.3f;

    public override void Paint(Graphics g)
    {
        float rad = S(18);
        mesh.Draw(g, B, T.P, Time, rad, F.GlassOn ? 0.62f : 1f);
        using (GraphicsPath gp = Art.Round(B, rad))
        using (LinearGradientBrush veil = new LinearGradientBrush(new RectangleF(B.X, B.Y - 1, B.Width, B.Height + 2), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(F.GlassOn ? 45 : 70, 0, 0, 0), 90f))
            g.FillPath(veil, gp);
        Art.Gloss(g, B, rad, 60);
        using (GraphicsPath p = Art.Round(new RectangleF(B.X + 0.5f, B.Y + 0.5f, B.Width - 1, B.Height - 1), rad))
        using (LinearGradientBrush lg = new LinearGradientBrush(new RectangleF(B.X, B.Y - 1, B.Width, B.Height + 2), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(25, 255, 255, 255), 90f))
        using (Pen pen = new Pen(lg, 1)) g.DrawPath(pen, p);

        bool vis = App.Shown;
        Tx.Draw(g, vis ? Lang.L("Le icone sono visibili", "Your icons are visible") : Lang.L("Le icone sono nascoste", "Your icons are hidden"),
            Fx.Hero, Color.White, new RectangleF(B.X + S(26), B.Y + S(24), B.Width - S(52), S(34)));
        Tx.Draw(g, App.StatusLine(), Fx.Body, Color.FromArgb(220, 255, 255, 255),
            new RectangleF(B.X + S(26), B.Y + S(60), B.Width - S(52), S(22)));
    }

    public override float LensRadius { get { return 18; } }
    public void Free() { mesh.Dispose(); }
}

class SettingsForm : GlassForm
{
    static readonly int[] PageIcons = { 0, 1, 6, 7, 2, 3, 4 };
    static readonly int[] DelaySteps = { 5, 10, 15, 20, 30, 45, 60, 90, 120, 180, 300 };

    static string PageName(int i)
    {
        switch (i)
        {
            case 0: return "Home";
            case 1: return Lang.L("Aspetto", "Appearance");
            case 2: return Lang.L("Transizioni", "Transitions");
            case 3: return Lang.L("Scene", "Scenes");
            case 4: return Lang.L("Comportamento", "Behavior");
            case 5: return Lang.L("Scorciatoie", "Shortcuts");
            default: return Lang.L("Sistema", "System");
        }
    }

    readonly Nav nav;
    Hero hero;
    int page, order;
    float pageT = 99;
    int cx, cw, y;
    readonly Timer heroTimer = new Timer();
    readonly Timer secTimer = new Timer();
    readonly Timer confirmTimer = new Timer();
    Bitmap logo;
    bool autostartOn, confirmUninstall;

    public SettingsForm() : base(true)
    {
        ClientSize = new Size(S(800), S(624));
        DragH = S(52);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
        autostartOn = Autostart.IsOn();
        logo = Art.AppArt(S(22));

        Add(new CapBtn { Kind = 0, B = new Rectangle(ClientSize.Width - S(46), 0, S(46), S(36)), OnClick = Close });
        Add(new CapBtn { Kind = 1, B = new Rectangle(ClientSize.Width - S(92), 0, S(46), S(36)), OnClick = delegate { WindowState = FormWindowState.Minimized; } });

        Func<string>[] names = new Func<string>[7];
        for (int i = 0; i < 7; i++) { int k = i; names[i] = delegate { return PageName(k); }; }
        // la barra laterale è una lastra di vetro a sé
        Add(new Group { B = new Rectangle(S(12), S(58), S(212), ClientSize.Height - S(70)), Radius = 22, Clear = true });
        nav = Add(new Nav(names, PageIcons));
        nav.B = new Rectangle(S(20), S(68), S(196), S(7 * 44));
        nav.OnSelect = ShowPage;

        cx = S(244); cw = ClientSize.Width - cx - S(28);
        PageArea = new Rectangle(cx - S(4), S(56), cw + S(8), ClientSize.Height - S(56));

        heroTimer.Interval = 33;      // ~30 fps, solo quando la Home è visibile e la finestra è attiva
        heroTimer.Tick += delegate { if (hero != null) { hero.Time += 0.028f; Invalidate(Rectangle.Inflate(hero.B, 2, 2)); } };
        secTimer.Interval = 1000;     // conto alla rovescia dell'auto-nascondi
        secTimer.Tick += delegate { if (hero != null) Invalidate(hero.B); };
        confirmTimer.Interval = 4000;
        confirmTimer.Tick += delegate { confirmTimer.Stop(); confirmUninstall = false; Invalidate(); };

        ShowPage(0);
    }

    // ── pagine (gli elementi entrano a cascata) ──
    void ShowPage(int i)
    {
        page = i;
        nav.Sel = i;
        ClearEls(delegate(El e) { return e.Page; });
        if (hero != null) { hero.Free(); hero = null; }
        order = 0;
        curGroup = null; lastRow = null;
        y = S(112);
        switch (i)
        {
            case 0: BuildHome(); break;
            case 1: BuildLook(); break;
            case 2: BuildTransitions(); break;
            case 3: BuildScenes(); break;
            case 4: BuildBehavior(); break;
            case 5: BuildKeys(); break;
            case 6: BuildSystem(); break;
        }
        EndGroup();
        pageT = T.Motion ? 0 : 99;
        UpdateHeroTimers();
        InvalidateRefraction();
        Kick();
    }

    // Le righe consecutive finiscono nella stessa lastra di vetro (come nelle impostazioni di iOS).
    Group curGroup;
    Row lastRow;

    void EndGroup()
    {
        if (curGroup == null) return;
        curGroup = null; lastRow = null;
        y += S(18);
    }

    T2 P<T2>(T2 e) where T2 : El
    {
        if (!(e is Row) && !(e is Group)) EndGroup();
        e.Page = true; e.Order = order++;
        return Add(e);
    }

    // La finestra principale è il vetro più "estremo": tinta minima, colori vivi, lenti marcate, nessun bagliore.
    protected override float GlassTint { get { return T.Dark ? 0.2f : 0.26f; } }
    protected override float GlassSaturation { get { return 1.6f; } }
    protected override int GlassBlur { get { return 6; } }
    protected override float AmbientOnGlass { get { return 0f; } }
    protected override float LensBand { get { return 24; } }
    protected override float LensShift { get { return 15; } }
    protected override int LensBright { get { return T.Dark ? 44 : 56; } }

    protected override bool Settled { get { return pageT >= 0.7f + order * 0.06f; } }

    // controllo legato alla scheda: entra insieme a lei
    T2 With<T2>(T2 e, El row) where T2 : El { e.Page = true; e.Order = row.Order; return Add(e); }

    protected override int PageOffsetFor(El e)
    {
        float local = Math.Max(0, Math.Min(1, (pageT - e.Order * 0.06f) / 0.7f));
        float ease = 1 - (float)Math.Pow(1 - local, 3);
        return (int)((1 - ease) * S(26));
    }

    protected override bool AnimateExtra()
    {
        float end = 0.7f + order * 0.06f;
        if (pageT >= end) { PageCover = 0; return false; }
        pageT = Math.Min(end, pageT + 0.05f * GlassForm.Dt);
        PageCover = Math.Max(0, 1 - pageT * 2.4f) * 0.9f;
        if (pageT >= end) InvalidateRefraction();    // pagina ferma: i bordi delle lastre tornano a piegare lo sfondo
        return true;
    }

    void Section(string t)
    {
        EndGroup();
        string s = t;
        P(new Label { Text = delegate { return s; }, Font = Fx.H, Color = delegate { return T.Text2; }, B = new Rectangle(cx + S(6), y, cw, S(22)) });
        y += S(30);
    }

    void ToggleRow(string title, string desc, Func<bool> get, Action<bool> set, Func<bool> en)
    {
        Row row = CtlRow(title, desc, en, 80);
        With(new Toggle(get, set) { B = row.B, EnabledIf = en }, row);
    }

    Row CtlRow(string title, string desc, Func<bool> en, int rightPad) { return CtlRow(title, desc, en, rightPad, 62); }

    Row CtlRow(string title, string desc, Func<bool> en, int rightPad, int h)
    {
        if (curGroup == null)
        {
            curGroup = P(new Group { B = new Rectangle(cx, y, cw, 0), Clear = true });
            lastRow = null;
        }
        Row row = With(new Row { B = new Rectangle(cx, y, cw, S(h)), Title = title, Desc = desc, EnabledIf = en, RightPad = rightPad, Last = true }, curGroup);
        if (lastRow != null) lastRow.Last = false;
        lastRow = row;
        y += S(h);
        curGroup.B = new Rectangle(cx, curGroup.B.Y, cw, y - curGroup.B.Y);
        return row;
    }

    static void Set(Action a) { a(); App.ConfigChanged(); }

    void BuildHome()
    {
        hero = P(new Hero { B = new Rectangle(cx, y, cw, S(200)) });
        int by = hero.B.Bottom - S(26) - S(44);
        With(new Pill { Style = 2, B = new Rectangle(cx + S(26), by, S(200), S(44)), Text = delegate { return App.Shown ? Lang.L("Nascondi icone", "Hide icons") : Lang.L("Mostra icone", "Show icons"); }, OnClick = App.Toggle }, hero);
        With(new Pill { Style = 2, IconKind = 5, B = new Rectangle(cx + S(26) + S(212), by, S(200), S(44)), Text = delegate { return Lang.L("Cerca sul desktop", "Search desktop"); }, OnClick = App.ShowSearch }, hero);
        y = hero.B.Bottom + S(26);

        Section(Lang.L("Rapido", "Quick"));
        ToggleRow(Lang.L("Nascondi automaticamente", "Auto-hide"), Lang.L("Quando non usi il desktop per un po'", "When you haven't used the desktop for a while"),
            delegate { return App.Cfg.AutoHide; }, delegate(bool v) { Set(delegate { App.Cfg.AutoHide = v; }); }, null);
        ToggleRow(Lang.L("Pausa per un'ora", "Pause for an hour"), Lang.L("Le icone restano visibili finché non riprendi", "Icons stay visible until you resume"),
            delegate { return App.Paused; }, delegate(bool v) { App.SetPause(v); }, delegate { return App.Cfg.AutoHide; });
        ToggleRow(Lang.L("Modalità Zen", "Zen mode"), Lang.L("Nasconde anche la barra delle applicazioni insieme alle icone", "Also hides the taskbar together with the icons"),
            delegate { return App.Cfg.Zen; }, delegate(bool v) { Set(delegate { App.Cfg.Zen = v; }); }, null);
    }


    void BuildLook()
    {
        Section(Lang.L("Gradiente", "Gradient"));
        Palette[] list = Palette.List();
        Swatches sw = P(new Swatches(list, delegate { return App.Cfg.Palette; }, delegate(string id) { Set(delegate { App.Cfg.Palette = id; }); }));
        sw.B = new Rectangle(cx, y, cw, S(2 * 84));
        y += S(2 * 84) + S(14);

        ToggleRow(Lang.L("Effetto vetro", "Glass effect"),
            Lang.L("Vetro liquido: lo sfondo si sfoca e si piega ai bordi", "Liquid glass: what's behind blurs and bends at the edges"),
            delegate { return App.Cfg.Glass; }, delegate(bool v) { Set(delegate { App.Cfg.Glass = v; }); }, null);
        Row r1 = CtlRow(Lang.L("Tema", "Theme"), Lang.L("Chiaro, scuro o come Windows", "Light, dark or like Windows"), null, 280);
        With(new Segmented(new string[] { Lang.L("Sistema", "System"), Lang.L("Chiaro", "Light"), Lang.L("Scuro", "Dark") }, delegate { return App.Cfg.ThemeMode; }, delegate(int v) { Set(delegate { App.Cfg.ThemeMode = v; }); }) { B = r1.B }, r1);
        Row r2 = CtlRow(Lang.L("Icona vicino all'orologio", "Tray icon"), Lang.L("Monocromatica o con i colori del tema", "Monochrome or in the theme's colors"), null, 200);
        With(new Segmented(new string[] { "Mono", Lang.L("Colore", "Color") }, delegate { return App.Cfg.TrayStyle; }, delegate(int v) { Set(delegate { App.Cfg.TrayStyle = v; }); }) { B = r2.B }, r2);
        ToggleRow(Lang.L("Animazioni", "Animations"), Lang.L("Gradienti in movimento e transizioni morbide", "Moving gradients and smooth transitions"),
            delegate { return App.Cfg.Motion; }, delegate(bool v) { Set(delegate { App.Cfg.Motion = v; }); }, null);
    }

    static readonly string[][] Effects = {
        new string[] { "Liquido", "Liquid", "Il desktop si appanna come vetro e le icone si sciolgono", "The desktop fogs up like glass and the icons melt away" },
        new string[] { "Dissolvenza", "Fade", "Le icone svaniscono dolcemente", "Icons gently fade out" },
        new string[] { "Cerchio", "Circle", "Un cerchio si apre dal puntatore del mouse", "A circle opens from your mouse pointer" },
        new string[] { "Tendina", "Wipe", "Una tendina scorre da sinistra a destra", "A curtain sweeps from left to right" },
        new string[] { "Gravità", "Gravity", "Le icone cadono e spariscono, poi tornano rimbalzando", "Icons fall away, then drop back with a bounce" },
        new string[] { "Onda", "Wave", "Le colonne cadono a cascata partendo dal mouse", "Columns cascade away starting from your mouse" },
        new string[] { "Vola via", "Float", "Le icone salgono leggere e svaniscono", "Icons drift up and vanish" },
        new string[] { "Film burn", "Film burn", "La pellicola si scioglie nel proiettore: luce calda, bruciature, grana (scheda video)", "The film melts in the projector: warm light leaks, burns, grain (GPU)" },
        new string[] { "Pixel", "Pixels", "Tutto si scompone in quadratini che si scambiano (scheda video)", "Everything breaks into pixels that swap places (GPU)" },
        new string[] { "Glitch", "Glitch", "Fasce che saltano e colori separati, stile segnale disturbato (scheda video)", "Jumping bands and split colors, like a broken signal (GPU)" },
        new string[] { "Increspatura", "Ripple", "Un'onda d'acqua parte dal puntatore (scheda video)", "A water wave spreads from your pointer (GPU)" },
        new string[] { "Inchiostro", "Ink", "Una macchia d'inchiostro si allarga dal puntatore (scheda video)", "An ink blot spreads from your pointer (GPU)" } };

    void BuildTransitions()
    {
        ToggleRow(Lang.L("Transizione animata", "Animated transition"), Lang.L("Le icone non spariscono mai di colpo", "Icons never just blink away"),
            delegate { return App.Cfg.Fade; }, delegate(bool v) { Set(delegate { App.Cfg.Fade = v; }); }, null);
        Section(Lang.L("Effetto", "Effect"));
        Func<string>[] names = new Func<string>[Effects.Length];
        for (int i = 0; i < Effects.Length; i++) { int k = i; names[i] = delegate { return Lang.L(Effects[k][0], Effects[k][1]); }; }
        Chips ch = P(new Chips(names, delegate { return App.Cfg.FadeStyle; }, delegate(int v) { Set(delegate { App.Cfg.FadeStyle = v; }); }) { EnabledIf = delegate { return App.Cfg.Fade; } });
        ch.B = new Rectangle(cx, y, cw, 10);
        ch.B = new Rectangle(cx, y, cw, ch.Height);
        y += ch.Height + S(12);
        P(new Label
        {
            Text = delegate { string[] e = Effects[Math.Max(0, Math.Min(Effects.Length - 1, App.Cfg.FadeStyle))]; return Lang.L(e[2], e[3]); },
            Font = Fx.Body, Color = delegate { return T.Text2; }, B = new Rectangle(cx + S(6), y, cw, S(24))
        });
        y += S(36);
        Row rv = CtlRow(Lang.L("Velocità", "Speed"), Lang.L("Durata della transizione", "Transition length"), delegate { return App.Cfg.Fade; }, 280);
        With(new Segmented(new string[] { Lang.L("Veloce", "Fast"), Lang.L("Normale", "Normal"), Lang.L("Lenta", "Slow") }, delegate { return App.Cfg.FadeSpeed; }, delegate(int v) { Set(delegate { App.Cfg.FadeSpeed = v; }); }) { B = rv.B, EnabledIf = delegate { return App.Cfg.Fade; } }, rv);
        Row rp = CtlRow(Lang.L("Prova l'effetto", "Try the effect"), Lang.L("Le icone spariscono e tornano subito", "Icons vanish and come straight back"), delegate { return App.Cfg.Fade; }, 160);
        With(new Pill { Style = 0, B = PillAt(rp.B, 130), Text = delegate { return Lang.L("Prova", "Try it"); }, OnClick = App.PreviewTransition, EnabledIf = delegate { return App.Cfg.Fade; } }, rp);
    }

    // ── Scene: elenco + editor della scena selezionata ──
    DesktopPulito.Domain.SceneId selScene;

    static string EffectName(DesktopPulito.Domain.TransitionSpec t)
    {
        if (t == null) return Lang.L("Predefinito", "Default");
        string[] e = Effects[(int)t.Style];
        return Lang.L(e[0], e[1]);
    }

    void BuildScenes()
    {
        DesktopPulito.Domain.SceneLibrary lib = Services.EditScenes.Library;
        if (!lib.Scenes.Any(s => s.Id == selScene)) selScene = lib.ActiveId;
        Section(Lang.L("Le tue scene", "Your scenes") + "   ·   Ctrl+Alt+S " + Lang.L("per passare alla successiva", "switches to the next one"));
        P(new Pill { Style = 1, B = new Rectangle(cx + cw - S(120), y - S(34), S(120), S(30)), Text = delegate { return Lang.L("+ Nuova", "+ New"); },
            OnClick = delegate { selScene = Services.EditScenes.Create(Lang.L("Scena", "Scene")).Id; ShowPage(3); } });
        foreach (DesktopPulito.Domain.Scene sc in lib.Scenes)
        {
            DesktopPulito.Domain.Scene s = sc;
            bool active = s.Id == lib.ActiveId;
            string desc = (s.ShowIcons ? Lang.L("Icone visibili", "Icons visible") : Lang.L("Desktop pulito", "Clean desktop")) + "  ·  " +
                          (string.IsNullOrEmpty(s.WallpaperPath) ? Lang.L("sfondo attuale", "current wallpaper") : System.IO.Path.GetFileName(s.WallpaperPath)) + "  ·  " + EffectName(s.EnterWith);
            int ry = y;
            Row r = CtlRow(s.Name, desc, null, 170);
            r.Accent = s.Id == selScene;
            With(new Pill { Style = 1, B = new Rectangle(cx, ry, cw - S(150), S(62)), Text = delegate { return ""; }, OnClick = delegate { selScene = s.Id; ShowPage(3); }, Invisible = true }, r);
            With(new Pill
            {
                Style = active ? 0 : 1, B = PillAt(r.B, 120),
                Text = delegate { return active ? Lang.L("In uso", "Active") : Lang.L("Attiva", "Activate"); },
                OnClick = delegate { if (!active) { App.ActivateScene(s.Id); ShowPage(3); } }
            }, r);
        }
        DesktopPulito.Domain.Scene sel = lib.Find(selScene);
        DesktopPulito.Domain.SceneId id = sel.Id;
        Section(Lang.L("Modifica", "Edit") + ": " + sel.Name);

        Row rn = CtlRow(Lang.L("Nome", "Name"), null, null, 390);
        TextField tf = With(new TextField
        {
            B = rn.B, BoxW = 230, RightGap = 140, Placeholder = Lang.L("Nome della scena", "Scene name"),
            Get = delegate { return Services.EditScenes.Library.Find(id).Name; },
            Commit = delegate(string v)
            {
                try { Services.EditScenes.Rename(id, v); Invoker(delegate { ShowPage(3); }); return null; }
                catch (DesktopPulito.Domain.DomainException ex) { return ex.Message; }
            }
        }, rn);
        rn.Warn = delegate { return tf.Error; };
        With(new Pill
        {
            Style = 3, B = PillAt(rn.B, 110), Text = delegate { return Lang.L("Elimina", "Delete"); },
            EnabledIf = delegate { return Services.EditScenes.Library.Scenes.Count > 1; },
            OnClick = delegate { try { Services.EditScenes.Delete(id); } catch (DesktopPulito.Domain.DomainException) { } ShowPage(3); }
        }, rn);

        ToggleRow(Lang.L("Icone visibili", "Icons visible"), Lang.L("Le icone del desktop in questa scena", "Desktop icons in this scene"),
            delegate { return Services.EditScenes.Library.Find(id).ShowIcons; },
            delegate(bool v) { Services.EditScenes.Update(id, x => x.ShowIcons = v); RefreshSoon(); }, null);

        Row rw = CtlRow(Lang.L("Sfondo", "Wallpaper"), string.IsNullOrEmpty(sel.WallpaperPath) ? Lang.L("Usa lo sfondo attuale", "Uses the current wallpaper") : sel.WallpaperPath, null, 270);
        With(new Pill { Style = 1, B = PillAt(rw.B, 120), Text = delegate { return Lang.L("Scegli…", "Choose…"); }, OnClick = delegate { PickWallpaper(id); } }, rw);
        if (!string.IsNullOrEmpty(sel.WallpaperPath))
            With(new Pill { Style = 1, B = new Rectangle(PillAt(rw.B, 120).X - S(110), PillAt(rw.B, 100).Y, S(100), S(34)), Text = delegate { return Lang.L("Togli", "Clear"); },
                OnClick = delegate { Services.EditScenes.Update(id, x => x.WallpaperPath = null); ShowPage(3); } }, rw);

        Row re = CtlRow(Lang.L("Effetto d'ingresso", "Entry effect"), Lang.L("Clic per cambiarlo · Predefinito = quello di Transizioni", "Click to change · Default = the one in Transitions"), null, 200);
        With(new Pill
        {
            Style = 0, B = PillAt(re.B, 170), Text = delegate { return EffectName(Services.EditScenes.Library.Find(id).EnterWith) + "  ›"; },
            OnClick = delegate
            {
                Services.EditScenes.Update(id, x =>
                {
                    int next = x.EnterWith == null ? 0 : (int)x.EnterWith.Style + 1;
                    x.EnterWith = next >= Effects.Length ? null : new DesktopPulito.Domain.TransitionSpec((DesktopPulito.Domain.TransitionStyle)next, (DesktopPulito.Domain.TransitionSpeed)App.Cfg.FadeSpeed);
                });
                RefreshSoon();
            }
        }, re);
    }

    void PickWallpaper(DesktopPulito.Domain.SceneId id)
    {
        using (OpenFileDialog d = new OpenFileDialog { Title = Lang.L("Sfondo della scena", "Scene wallpaper"), Filter = Lang.L("Immagini", "Images") + "|*.jpg;*.jpeg;*.png;*.bmp;*.webp" })
            if (d.ShowDialog(this) == DialogResult.OK) Services.EditScenes.Update(id, x => x.WallpaperPath = d.FileName);
        ShowPage(3);
    }

    void Invoker(MethodInvoker a) { BeginInvoke(a); }
    void RefreshSoon() { BeginInvoke((MethodInvoker)delegate { ShowPage(3); }); }

    void BuildBehavior()
    {
        ToggleRow(Lang.L("Nascondi automaticamente", "Auto-hide"), Lang.L("Clic sullo sfondo o Win+D per farle tornare", "Click the wallpaper or press Win+D to bring them back"),
            delegate { return App.Cfg.AutoHide; }, delegate(bool v) { Set(delegate { App.Cfg.AutoHide = v; }); }, null);
        Row rd = CtlRow(Lang.L("Dopo", "After"), Lang.L("Tempo senza usare il desktop", "Time without using the desktop"), delegate { return App.Cfg.AutoHide; }, 300);
        With(new Slider(DelaySteps, delegate { return App.Cfg.Delay; }, delegate(int v) { App.Cfg.Delay = v; App.ConfigChanged(); }, Fmt) { B = rd.B, EnabledIf = delegate { return App.Cfg.AutoHide; } }, rd);
        ToggleRow(Lang.L("Doppio clic sul desktop", "Double-click the desktop"), Lang.L("Su uno spazio vuoto per nascondere o mostrare", "On an empty spot to hide or show"),
            delegate { return App.Cfg.DoubleClick; }, delegate(bool v) { Set(delegate { App.Cfg.DoubleClick = v; }); }, null);
        ToggleRow(Lang.L("Modalità Zen", "Zen mode"), Lang.L("Nasconde anche la barra delle applicazioni", "Also hides the taskbar"),
            delegate { return App.Cfg.Zen; }, delegate(bool v) { Set(delegate { App.Cfg.Zen = v; }); }, null);
    }

    void BuildKeys()
    {
        Row r1 = CtlRow(Lang.L("Mostra / nascondi icone", "Show / hide icons"), Lang.L("Funziona ovunque, anche dentro altre app", "Works everywhere, even inside other apps"), null, 250);
        HotkeyBox h1 = With(new HotkeyBox(delegate { return App.Cfg.HotToggle; }, delegate(int v) { Set(delegate { App.Cfg.HotToggle = v; }); }, delegate { return App.HotToggleOk; }) { B = r1.B }, r1);
        r1.Warn = delegate { return KeyWarn(h1, App.HotToggleOk); };
        Row r2 = CtlRow(Lang.L("Cerca sul desktop", "Search desktop"), Lang.L("Apri file e programmi senza mostrare le icone", "Open files and apps without showing icons"), null, 250);
        HotkeyBox h2 = With(new HotkeyBox(delegate { return App.Cfg.HotSearch; }, delegate(int v) { Set(delegate { App.Cfg.HotSearch = v; }); }, delegate { return App.HotSearchOk; }) { B = r2.B }, r2);
        r2.Warn = delegate { return KeyWarn(h2, App.HotSearchOk); };
        y += S(10);
        Section(Lang.L("Mouse", "Mouse"));
        string[][] tips = {
            new string[] { Lang.L("Clic sull'icona vicino all'orologio", "Click the tray icon"), Lang.L("mostra / nascondi", "show / hide") },
            new string[] { Lang.L("Clic destro sull'icona", "Right-click the tray icon"), Lang.L("menu rapido", "quick menu") },
            new string[] { Lang.L("Clic con la rotella sull'icona", "Middle-click the tray icon"), Lang.L("cerca sul desktop", "search desktop") },
            new string[] { Lang.L("Clic sullo sfondo", "Click the wallpaper"), Lang.L("fa tornare le icone (auto-nascondi)", "brings icons back (auto-hide)") } };
        foreach (string[] t in tips)
        {
            int ry = y;
            Row r = CtlRow(t[0], null, null, 240, 48);
            string what = t[1];
            With(new Label { Text = delegate { return what; }, Font = Fx.Small, Color = delegate { return T.Text3; }, Align = StringAlignment.Far, B = new Rectangle(cx, ry, cw - S(22), S(48)) }, r);
        }
    }

    void BuildSystem()
    {
        ToggleRow(Lang.L("Avvia con Windows", "Start with Windows"), Lang.L("Parte in silenzio, vicino all'orologio", "Starts quietly in the tray"),
            delegate { return autostartOn; }, delegate(bool v) { Autostart.Set(v); autostartOn = Autostart.IsOn(); }, null);
        Row rl = CtlRow(Lang.L("Lingua", "Language"), Lang.L("Automatica segue la lingua di Windows", "Auto follows your Windows language"), null, 280);
        With(new Segmented(new string[] { "Auto", "Italiano", "English" }, delegate { return App.Cfg.Language; }, delegate(int v) { Set(delegate { App.Cfg.Language = v; }); }) { B = rl.B, SegW = 90 }, rl);
        Row r1 = CtlRow(Lang.L("Cartella Desktop", "Desktop folder"), Lang.L("Apri i file del desktop in Esplora file", "Open your desktop files in File Explorer"), null, 160);
        With(new Pill { Style = 1, B = PillAt(r1.B, 130), Text = delegate { return Lang.L("Apri", "Open"); }, OnClick = DesktopItems.OpenDesktopFolder }, r1);
        Row r2 = CtlRow(Lang.L("Impostazioni predefinite", "Reset settings"), Lang.L("Rimette tutte le opzioni come all'inizio", "Puts every option back to default"), null, 160);
        With(new Pill { Style = 1, B = PillAt(r2.B, 130), Text = delegate { return Lang.L("Ripristina", "Reset"); }, OnClick = delegate { App.ResetConfig(); autostartOn = Autostart.IsOn(); } }, r2);
        Row r3 = CtlRow(Lang.L("Rimuovi dal PC", "Remove from PC"), Lang.L("Toglie avvio automatico e impostazioni, poi si chiude", "Removes autostart and settings, then quits"), null, 200);
        With(new Pill
        {
            Style = 3, B = PillAt(r3.B, 170),
            Text = delegate { return confirmUninstall ? Lang.L("Clic per confermare", "Click to confirm") : Lang.L("Rimuovi", "Remove"); },
            OnClick = delegate
            {
                if (!confirmUninstall) { confirmUninstall = true; confirmTimer.Stop(); confirmTimer.Start(); Invalidate(); return; }
                App.Uninstall();
            }
        }, r3);
        y += S(8);
        P(new AboutCard { B = new Rectangle(cx, y, cw, S(96)) });
    }

    static string KeyWarn(HotkeyBox h, bool ok)
    {
        if (h.Recording) return null;
        return ok ? null : Lang.L("Già usata da un'altra app: scegline un'altra", "Already used by another app: pick another one");
    }

    Rectangle PillAt(Rectangle row, int w) { return new Rectangle(row.Right - S(18) - S(w), row.Y + row.Height / 2 - S(17), S(w), S(34)); }

    static string Fmt(int sec)
    {
        if (sec < 60) return sec + " s";
        if (sec % 60 == 0) return (sec / 60) + " min";
        return (sec / 60) + " min " + (sec % 60) + " s";
    }

    // ── animazioni ──
    void UpdateHeroTimers()
    {
        bool homeVisible = page == 0 && WindowState != FormWindowState.Minimized && Visible;
        if (homeVisible && T.Motion && ContainsFocus) heroTimer.Start(); else heroTimer.Stop();
        if (homeVisible) secTimer.Start(); else secTimer.Stop();
    }

    protected override void OnActivated(EventArgs e) { base.OnActivated(e); UpdateHeroTimers(); }
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); UpdateHeroTimers(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateHeroTimers(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateHeroTimers(); }

    public override void ApplyChrome()
    {
        base.ApplyChrome();
        if (nav != null) ShowPage(page);
    }

    public void Sync() { autostartOn = Autostart.IsOn(); Kick(); }

    protected override void PaintBack(Graphics g)
    {
        g.DrawImage(logo, S(20), S(17));
        Tx.Draw(g, "Desktop Pulito", Fx.BodyB, T.Text1, new RectangleF(S(52), S(14), S(200), S(28)));
        GraphicsState st = g.Save();
        g.TranslateTransform(0, PageOffsetFor(new Row { Order = 0 }));
        Tx.Draw(g, PageName(page), Fx.Title, T.Text1, new RectangleF(cx, S(58), cw, S(40)));
        g.Restore(st);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            heroTimer.Dispose(); secTimer.Dispose(); confirmTimer.Dispose();
            if (hero != null) hero.Free();
            if (logo != null) logo.Dispose();
        }
        base.Dispose(disposing);
    }
}

class AboutCard : El
{
    Bitmap art;

    public override void Paint(Graphics g)
    {
        Art.GlassCard(g, B, S(12), 0, F.Mouse, F.Sc);
        if (art == null) art = Art.AppArt((int)S(52));
        g.DrawImage(art, B.X + S(20), B.Y + (B.Height - S(52)) / 2);
        float x = B.X + S(88);
        Tx.Draw(g, "Desktop Pulito " + App.Version, Fx.BodyB, T.Text1, new RectangleF(x, B.Y + S(18), B.Width - S(110), S(22)));
        Tx.Wrap(g, Lang.L("Open source · nessun file viene spostato o eliminato · a riposo non usa il processore.",
                          "Open source · no file is ever moved or deleted · uses zero CPU when idle."),
            Fx.Small, T.Text2, new RectangleF(x, B.Y + S(44), B.Width - S(108), S(40)));
    }
}
