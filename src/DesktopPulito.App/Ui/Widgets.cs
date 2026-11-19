#nullable disable
// Widget in vetro appoggiati sul desktop: orologio, meteo, agenda.
// Ogni scena sceglie i suoi widget; si trascinano dove si vuole e la posizione resta nella scena.
// Ottimizzazione: niente animazioni continue, si ridisegnano solo quando cambia il contenuto.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopPulito.Domain;

static class Widgets
{
    static readonly Dictionary<WidgetKind, DeskWidget> open = new Dictionary<WidgetKind, DeskWidget>();

    public static readonly WidgetKind[] All = { WidgetKind.Clock, WidgetKind.Weather, WidgetKind.Agenda };

    public static string Name(WidgetKind k)
    {
        switch (k)
        {
            case WidgetKind.Clock: return Lang.L("Orologio", "Clock");
            case WidgetKind.Weather: return Lang.L("Meteo", "Weather");
            default: return Lang.L("Agenda", "Agenda");
        }
    }

    // Posizione iniziale: colonna a destra, uno sotto l'altro.
    public static WidgetPlacement Default(WidgetKind k)
    {
        return WidgetPlacement.Create(k, 0.97, k == WidgetKind.Clock ? 0.04 : k == WidgetKind.Weather ? 0.30 : 0.56);
    }

    // Allinea le finestre aperte ai widget della scena attiva.
    public static void Sync()
    {
        Scene s = Services.EditScenes.Library.Active;
        HashSet<WidgetKind> want = new HashSet<WidgetKind>();
        foreach (WidgetPlacement w in s.Widgets) want.Add(w.Kind);
        foreach (WidgetKind k in new List<WidgetKind>(open.Keys))
            if (!want.Contains(k)) { open[k].Close(); open.Remove(k); }
        foreach (WidgetPlacement w in s.Widgets)
        {
            DeskWidget f;
            if (!open.TryGetValue(w.Kind, out f))
            {
                f = w.Kind == WidgetKind.Clock ? new ClockWidget() : w.Kind == WidgetKind.Weather ? (DeskWidget)new WeatherWidget() : new AgendaWidget();
                open[w.Kind] = f;
                f.Place(w);
                f.Show();
            }
            else { f.Place(w); f.RefreshBackdrop(); }
        }
    }

    // Città o calendario cambiati: i widget ricaricano i dati.
    public static void Reload() { foreach (DeskWidget f in open.Values) f.Reload(); }

    public static void Restyle() { foreach (DeskWidget f in open.Values) f.ApplyChrome(); }

    public static void CloseAll()
    {
        foreach (DeskWidget f in open.Values) f.Close();
        open.Clear();
    }
}

abstract class DeskWidget : GlassForm
{
    public readonly WidgetKind Kind;
    bool placing;

    protected DeskWidget(WidgetKind kind, int w, int h) : base(false)
    {
        Kind = kind;
        Text = Widgets.Name(kind);
        TopMost = false;
        DragH = 100000;              // si trascina da qualsiasi punto
        BackdropEvery = 12000;       // dietro c'è lo sfondo: cambia di rado
        ClientSize = new Size(S(w), S(h));
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x08000000;     // WS_EX_TOOLWINDOW (niente Alt+Tab) | WS_EX_NOACTIVATE
            return cp;
        }
    }

    // vetro chiaro e sottile, come una lastra appoggiata sullo sfondo
    protected override float GlassTint { get { return T.Dark ? 0.22f : 0.26f; } }
    protected override float GlassSaturation { get { return 1.6f; } }
    protected override int GlassBlur { get { return 6; } }
    protected override float AmbientOnGlass { get { return 0.22f; } }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS { public IntPtr hwnd, after; public int x, y, cx, cy; public uint flags; }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0046)                                   // WM_WINDOWPOSCHANGING: restiamo sempre sul fondo
        {
            WINDOWPOS wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
            wp.after = (IntPtr)1;                              // HWND_BOTTOM
            wp.flags &= ~0x0004u;                              // togli SWP_NOZORDER
            Marshal.StructureToPtr(wp, m.LParam, false);
        }
        else if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE → MA_NOACTIVATE
        base.WndProc(ref m);
        if (m.Msg == 0x0232 && !placing) SavePlace();          // fine trascinamento
    }

    static Rectangle Area { get { return Screen.PrimaryScreen.WorkingArea; } }

    public void Place(WidgetPlacement p)
    {
        placing = true;
        Rectangle a = Area;
        Location = new Point(a.X + (int)Math.Round(p.X * Math.Max(0, a.Width - Width)), a.Y + (int)Math.Round(p.Y * Math.Max(0, a.Height - Height)));
        placing = false;
    }

    void SavePlace()
    {
        Rectangle a = Area;
        double x = (Left - a.X) / (double)Math.Max(1, a.Width - Width), y = (Top - a.Y) / (double)Math.Max(1, a.Height - Height);
        WidgetPlacement p = WidgetPlacement.Create(Kind, x, y);
        try { Services.EditScenes.Update(Services.EditScenes.Library.ActiveId, s => s.PlaceWidget(p)); } catch (DomainException) { }
    }

    public virtual void Reload() { }

    // ── disegno comune ──
    protected static Font Big(GlassForm f, float px, FontStyle st)
    {
        try { return new Font("Segoe UI Variable Display", f.S(px), st, GraphicsUnit.Pixel); }
        catch { return new Font("Segoe UI", f.S(px), st, GraphicsUnit.Pixel); }
    }

    protected void Draw(Graphics g, string s, Font f, Color c, float x, float y, float w, float h, StringAlignment a)
    {
        Tx.Draw(g, s, f, c, new RectangleF(x, y, w, h), a, StringAlignment.Center);
    }

    protected static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }

    protected static CultureInfo Culture { get { return Lang.En ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("it-IT"); } }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeExtra();
        base.Dispose(disposing);
    }

    protected virtual void DisposeExtra() { }
}

sealed class ClockWidget : DeskWidget
{
    readonly Timer tick = new Timer();
    readonly Font time, date;
    string shown = "";

    public ClockWidget() : base(WidgetKind.Clock, 300, 150)
    {
        time = Big(this, 72, FontStyle.Regular);
        date = Big(this, 17, FontStyle.Regular);
        tick.Interval = 1000;        // controlla una volta al secondo, ridisegna solo al cambio di minuto
        tick.Tick += delegate { if (Now() != shown) Invalidate(); };
        tick.Start();
    }

    static string Now() { return DateTime.Now.ToString("HH:mm"); }

    protected override void PaintBack(Graphics g)
    {
        int W = ClientSize.Width, H = ClientSize.Height;
        shown = Now();
        Draw(g, shown, time, T.Text1, 0, S(14), W, S(88), StringAlignment.Center);
        Draw(g, Cap(DateTime.Now.ToString("dddd d MMMM", Culture)), date, T.Text2, 0, S(98), W, S(30), StringAlignment.Center);
    }

    protected override void DisposeExtra() { tick.Dispose(); time.Dispose(); date.Dispose(); }
}

sealed class WeatherWidget : DeskWidget
{
    readonly Timer every = new Timer();
    readonly Font temp, line;
    WeatherSnapshot now;
    string note;
    bool loading;

    public WeatherWidget() : base(WidgetKind.Weather, 300, 150)
    {
        temp = Big(this, 56, FontStyle.Regular);
        line = Big(this, 16, FontStyle.Regular);
        every.Interval = 15 * 60 * 1000;
        every.Tick += delegate { Reload(); };
        every.Start();
        Reload();
    }

    public override async void Reload()
    {
        if (loading) return;
        loading = true;
        try
        {
            now = await Services.Weather.ExecuteAsync(Lang.En ? "en" : "it");
            note = now == null ? (string.IsNullOrWhiteSpace(App.Cfg.WeatherCity)
                ? Lang.L("Scrivi la città in Impostazioni › Comportamento", "Enter your city in Settings › Behavior")
                : Lang.L("Città non trovata", "City not found")) : null;
        }
        catch { note = Lang.L("Meteo non raggiungibile", "Weather unavailable"); }
        finally { loading = false; }
        if (!IsDisposed) Invalidate();
    }

    static string Describe(WeatherCondition c)
    {
        switch (c)
        {
            case WeatherCondition.Clear: return Lang.L("Sereno", "Clear");
            case WeatherCondition.PartlyCloudy: return Lang.L("Poco nuvoloso", "Partly cloudy");
            case WeatherCondition.Cloudy: return Lang.L("Nuvoloso", "Cloudy");
            case WeatherCondition.Fog: return Lang.L("Nebbia", "Fog");
            case WeatherCondition.Drizzle: return Lang.L("Pioviggine", "Drizzle");
            case WeatherCondition.Rain: return Lang.L("Pioggia", "Rain");
            case WeatherCondition.Snow: return Lang.L("Neve", "Snow");
            case WeatherCondition.Showers: return Lang.L("Rovesci", "Showers");
            case WeatherCondition.Thunderstorm: return Lang.L("Temporale", "Thunderstorm");
            default: return "";
        }
    }

    protected override void PaintBack(Graphics g)
    {
        int W = ClientSize.Width, H = ClientSize.Height;
        if (now == null)
        {
            Draw(g, Lang.L("Meteo", "Weather"), Fx.BodyB, T.Text1, S(22), S(30), W - S(44), S(30), StringAlignment.Near);
            Draw(g, loading && note == null ? "…" : note, line, T.Text2, S(22), S(64), W - S(44), S(50), StringAlignment.Near);
            return;
        }
        DrawIcon(g, new RectangleF(S(22), S(30), S(84), S(84)), now.Condition, now.IsDay);
        Draw(g, Math.Round(now.TemperatureC).ToString("0") + "°", temp, T.Text1, S(118), S(18), W - S(130), S(70), StringAlignment.Near);
        Draw(g, Describe(now.Condition), line, T.Text1, S(122), S(84), W - S(134), S(24), StringAlignment.Near);
        Draw(g, now.Place.Name, line, T.Text2, S(122), S(106), W - S(134), S(24), StringAlignment.Near);
    }

    // Icone disegnate a vettori: nitide a ogni scala, niente file.
    void DrawIcon(Graphics g, RectangleF r, WeatherCondition c, bool day)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float u = r.Width / 10f;
        bool sun = c == WeatherCondition.Clear || c == WeatherCondition.PartlyCloudy;
        if (sun)
        {
            RectangleF s = c == WeatherCondition.Clear ? new RectangleF(r.X + u * 2, r.Y + u * 2, u * 6, u * 6) : new RectangleF(r.X + u * 3.6f, r.Y + u * 0.8f, u * 5, u * 5);
            Color a = day ? Color.FromArgb(255, 255, 214, 102) : Color.FromArgb(255, 226, 232, 255);
            Color b = day ? Color.FromArgb(255, 255, 150, 60) : Color.FromArgb(255, 160, 176, 230);
            using (LinearGradientBrush lg = new LinearGradientBrush(s, a, b, 45f)) g.FillEllipse(lg, s);
            if (c == WeatherCondition.Clear) return;
        }
        // nuvola: tre bolle su una base arrotondata, in vetro bianco
        float cy = sun ? u * 3.2f : u * 2.2f;
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddEllipse(r.X + u * 0.8f, r.Y + cy + u * 2.2f, u * 3.6f, u * 3.6f);
            p.AddEllipse(r.X + u * 2.6f, r.Y + cy, u * 4.6f, u * 4.6f);
            p.AddEllipse(r.X + u * 5.4f, r.Y + cy + u * 1.6f, u * 3.8f, u * 3.8f);
            p.AddRectangle(new RectangleF(r.X + u * 2.6f, r.Y + cy + u * 3.4f, u * 4.8f, u * 2.4f));
            using (LinearGradientBrush lg = new LinearGradientBrush(new RectangleF(r.X, r.Y + cy, r.Width, u * 6), Color.FromArgb(250, 255, 255, 255), Color.FromArgb(200, 210, 220, 235), 90f))
            { lg.WrapMode = WrapMode.TileFlipX; using (Region rg = new Region(p)) g.FillRegion(lg, rg); }
        }
        float by = r.Y + cy + u * 6.3f;
        using (Pen pen = new Pen(Color.FromArgb(230, 120, 180, 255), u * 0.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            if (c == WeatherCondition.Rain || c == WeatherCondition.Drizzle || c == WeatherCondition.Showers)
                for (int i = 0; i < 3; i++) g.DrawLine(pen, r.X + u * (3 + i * 2), by, r.X + u * (2.4f + i * 2), by + u * 1.6f);
            else if (c == WeatherCondition.Snow)
                using (SolidBrush sb = new SolidBrush(Color.White)) for (int i = 0; i < 3; i++) g.FillEllipse(sb, r.X + u * (2.6f + i * 2), by + u * 0.3f, u * 0.9f, u * 0.9f);
            else if (c == WeatherCondition.Thunderstorm)
                using (SolidBrush yb = new SolidBrush(Color.FromArgb(255, 255, 206, 70)))
                    g.FillPolygon(yb, new[] { new PointF(r.X + u * 5.2f, by - u * 0.4f), new PointF(r.X + u * 3.8f, by + u * 1.6f), new PointF(r.X + u * 4.9f, by + u * 1.6f), new PointF(r.X + u * 4.2f, by + u * 3.2f), new PointF(r.X + u * 6.2f, by + u * 0.8f), new PointF(r.X + u * 5f, by + u * 0.8f) });
            else if (c == WeatherCondition.Fog)
                using (Pen fp = new Pen(Color.FromArgb(200, 230, 235, 245), u * 0.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                { g.DrawLine(fp, r.X + u * 1.5f, by, r.X + u * 8.5f, by); g.DrawLine(fp, r.X + u * 2.5f, by + u * 1.2f, r.X + u * 7.5f, by + u * 1.2f); }
        }
    }

    protected override void DisposeExtra() { every.Dispose(); temp.Dispose(); line.Dispose(); }
}

sealed class AgendaWidget : DeskWidget
{
    const int MAX = 4;
    readonly Timer every = new Timer();
    readonly Font head, when, what;
    IReadOnlyList<AgendaEvent> events = Array.Empty<AgendaEvent>();
    string note;
    bool loading;

    public AgendaWidget() : base(WidgetKind.Agenda, 320, 236)
    {
        head = Big(this, 15, FontStyle.Bold);
        when = Big(this, 13, FontStyle.Regular);
        what = Big(this, 16, FontStyle.Regular);
        every.Interval = 5 * 60 * 1000;     // la cache dei dati è di 10 minuti; qui aggiorniamo anche "tra poco"/"oggi"
        every.Tick += delegate { Reload(); };
        every.Start();
        Reload();
    }

    public override async void Reload()
    {
        if (loading) return;
        loading = true;
        try
        {
            events = await Services.Agenda.ExecuteAsync(MAX);
            note = events.Count > 0 ? null : string.IsNullOrEmpty(App.Cfg.CalendarUrl)
                ? Lang.L("Collega il calendario in Impostazioni › Comportamento", "Link your calendar in Settings › Behavior")
                : Lang.L("Nessun impegno in arrivo", "Nothing coming up");
        }
        catch { note = Lang.L("Calendario non raggiungibile", "Calendar unavailable"); }
        finally { loading = false; }
        if (!IsDisposed) Invalidate();
    }

    string When(AgendaEvent e)
    {
        DateTime s = e.Start.LocalDateTime, today = DateTime.Today;
        string day = s.Date == today ? Lang.L("Oggi", "Today") : s.Date == today.AddDays(1) ? Lang.L("Domani", "Tomorrow") : Cap(s.ToString("ddd d MMM", Culture));
        return e.AllDay ? day + " · " + Lang.L("tutto il giorno", "all day") : day + " · " + s.ToString("HH:mm");
    }

    protected override void PaintBack(Graphics g)
    {
        int W = ClientSize.Width;
        Draw(g, Lang.L("PROSSIMI IMPEGNI", "COMING UP"), head, T.Text2, S(22), S(14), W - S(44), S(28), StringAlignment.Near);
        if (events.Count == 0)
        {
            Draw(g, loading && note == null ? "…" : note, what, T.Text2, S(22), S(52), W - S(44), S(60), StringAlignment.Near);
            return;
        }
        float y = S(48);
        for (int i = 0; i < events.Count && i < MAX; i++)
        {
            AgendaEvent e = events[i];
            using (SolidBrush b = new SolidBrush(T.Accent)) g.FillEllipse(b, S(22), y + S(9), S(7), S(7));
            Draw(g, string.IsNullOrEmpty(e.Title) ? Lang.L("(senza titolo)", "(untitled)") : e.Title, what, T.Text1, S(38), y, W - S(60), S(24), StringAlignment.Near);
            Draw(g, When(e), when, T.Text2, S(38), y + S(22), W - S(60), S(20), StringAlignment.Near);
            y += S(46);
        }
    }

    protected override void DisposeExtra() { every.Dispose(); head.Dispose(); when.Dispose(); what.Dispose(); }
}
