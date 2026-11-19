#nullable disable
// Mini-toolkit grafico: finestra di vetro liquido e controlli disegnati a mano.
// - Animazioni agganciate al refresh reale dello schermo e calcolate sul tempo (uguali a 60 o 144 Hz).
// - Si ridisegna solo ciò che si muove; le lastre di vetro sono disegnate una volta e riusate.
// - A riposo: nessun timer attivo, zero processore.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

abstract class El
{
    public Rectangle B;
    public bool Visible = true, Page, Reveal;
    public int Order;
    public GlassForm F;
    public Func<bool> EnabledIf;
    public bool Hover, Pressed;
    public float HoverK, PressK;

    public bool Enabled { get { return EnabledIf == null || EnabledIf(); } }
    public virtual bool Interactive { get { return false; } }
    public virtual float LensRadius { get { return -1; } }      // >= 0: i bordi di questo elemento piegano lo sfondo
    public virtual bool Hit(Point p) { return B.Contains(p); }
    public virtual Rectangle Dirty { get { return Rectangle.Inflate(B, (int)S(14), (int)S(14)); } }
    public virtual void Paint(Graphics g) { }
    public virtual void Down(Point p) { }
    public virtual void Drag(Point p) { }
    public virtual void Move(Point p) { }
    public virtual void Click(Point p) { }
    public virtual bool Key(Keys k) { return false; }
    public virtual void Blur() { }

    public virtual bool Animate()
    {
        bool a = Ease(ref HoverK, Hover && Enabled ? 1f : 0f, 0.28f);
        a |= Ease(ref PressK, Pressed ? 1f : 0f, 0.4f);
        return a;
    }

    // Avvicina v al bersaglio in modo indipendente dal refresh (f = quota per fotogramma a 60 Hz).
    public static bool Ease(ref float v, float t, float f)
    {
        float d = t - v;
        if (!T.Motion || Math.Abs(d) < 0.003f)
        {
            if (v != t) { v = t; return true; }
            return false;
        }
        float k = 1f - (float)Math.Pow(1f - f, GlassForm.Dt);
        v += d * k;
        return true;
    }

    protected float S(float v) { return v * F.Sc; }
}

class GlassForm : Form
{
    public static float Dt = 1f;          // durata dell'ultimo fotogramma, in fotogrammi da 60 Hz
    public readonly float Sc;
    public readonly List<El> Els = new List<El>();
    public Point Mouse = new Point(-9999, -9999);
    protected int DragH;
    protected float PageCover;
    protected Rectangle PageArea;
    public bool GlassOn;
    El hot, down, focus;
    Bitmap ambient;
    string ambientKey;

    // vetro liquido
    Dib matter, page;                     // sfondo-vetro (con bordo finestra) e versione con le lastre a lente
    bool inMove, movePending, refractDirty;
    readonly Timer refresh = new Timer();

    // orologio
    volatile bool framePending;
    bool clockOn;
    readonly Stopwatch frameWatch = Stopwatch.StartNew();
    double lastFrame;
    readonly MethodInvoker frameAction;

    public GlassForm(bool taskbar)
    {
        Sc = N.SystemDpi() / 96f;
        Fx.Init(Sc);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = "Desktop Pulito";
        ShowInTaskbar = taskbar;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        frameAction = Frame;
        refresh.Interval = 2500;     // lo sfondo dietro può cambiare: lo rileggiamo ogni tanto (≈10 ms)
        refresh.Tick += delegate { if (Visible && WindowState != FormWindowState.Minimized && !inMove) RefreshBackdrop(); };
    }

    public int S(float v) { return (int)Math.Round(v * Sc); }

    public T2 Add<T2>(T2 e) where T2 : El { e.F = this; Els.Add(e); return e; }

    // Niente cornice di Windows: la finestra è tutta nostra, ma DWM ci dà ombra e angoli arrotondati.
    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case 0x0083: if (m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; } break;   // WM_NCCALCSIZE
            case 0x0084:                                                                             // WM_NCHITTEST
                {
                    base.WndProc(ref m);
                    long lp = m.LParam.ToInt64();
                    Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    if (p.Y >= 0 && p.Y < DragH && HitEl(p) == null && CanDrag(p)) m.Result = (IntPtr)2;
                    else if (m.Result.ToInt64() > 1) m.Result = (IntPtr)1;
                    return;
                }
            case 0x0231:                                                                             // inizio trascinamento
                if (Fader.Recording) break;
                inMove = true;
                if (GlassOn) try { N.SetWindowDisplayAffinity(Handle, 0x11); } catch { }
                break;
            case 0x0232:                                                                             // fine trascinamento
                inMove = false;
                if (GlassOn) { try { N.SetWindowDisplayAffinity(Handle, 0); } catch { } RefreshBackdrop(); }
                break;
            case 0x0003:                                                                             // WM_MOVE
                if (GlassOn && !movePending && IsHandleCreated && Visible)
                {
                    // trascinamento: a ogni passo; spostamento da tastiera/aggancio: una volta, a posizione raggiunta
                    movePending = true;
                    BeginInvoke((MethodInvoker)delegate { movePending = false; RefreshBackdrop(); });
                }
                break;
        }
        base.WndProc(ref m);
    }

    protected virtual bool CanDrag(Point p) { return true; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome();
    }

    public virtual void ApplyChrome()
    {
        N.MARGINS mg = new N.MARGINS(); mg.L = mg.R = mg.T = mg.B = -1;
        N.DwmExtendFrameIntoClientArea(Handle, ref mg);
        int dark = T.Dark ? 1 : 0;
        N.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        int none = 1;
        N.DwmSetWindowAttribute(Handle, 38, ref none, 4);
        int round = 2;
        N.DwmSetWindowAttribute(Handle, 33, ref round, 4);
        GlassOn = App.Cfg.Glass;
        if (GlassOn) { RefreshBackdrop(); refresh.Start(); }
        else { FreeGlass(); refresh.Stop(); }
        Invalidate();
    }

    void FreeGlass()
    {
        if (matter != null) { matter.Dispose(); matter = null; }
        if (page != null) { page.Dispose(); page = null; }
    }

    // Fotografa ciò che sta dietro la finestra e ne fa vetro.
    public void RefreshBackdrop()
    {
        if (!GlassOn || !IsHandleCreated) return;
        Rectangle sr = Bounds;
        if (sr.Width < 2 || sr.Height < 2) return;
        IntPtr scr = N.GetDC(IntPtr.Zero);
        try
        {
            if (Fader.Recording && Visible) return;
            bool hide = Visible && !inMove;
            if (hide) { try { N.SetWindowDisplayAffinity(Handle, 0x11); } catch { } N.DwmFlush(); N.DwmFlush(); }
            Dib cap;
            try { cap = Dib.Capture(scr, sr); }
            finally { if (hide) try { N.SetWindowDisplayAffinity(Handle, 0); } catch { } }

            Color tint = T.Dark ? Color.FromArgb(255, 14, 14, 20) : Color.FromArgb(255, 246, 246, 250);
            // vetro adattivo: trasparentissimo su sfondi scuri, velato quanto basta per leggere su quelli chiari
            float lum = GlassFx.MeanLum(cap);
            float tintA = GlassTint + (T.Dark ? Math.Max(0, lum - 0.32f) * 0.8f : Math.Max(0, 0.62f - lum) * 0.6f);
            Dib mat = GlassFx.Material(scr, cap, tint, Math.Min(0.75f, tintA), GlassSaturation, inMove ? GlassBlur + 2 : GlassBlur);
            cap.Dispose();
            // la finestra stessa è una lastra: il bordo piega lo sfondo verso l'interno
            Dib withRim = GlassFx.Copy(scr, mat);
            GlassFx.Refract(mat, withRim, new RectangleF(0, 0, mat.W, mat.H), S(8), S(20), S(12), T.Dark ? 34 : 46, false);
            mat.Dispose();
            if (matter != null) matter.Dispose();
            matter = withRim;
            refractDirty = true;
        }
        catch { }
        finally { N.ReleaseDC(IntPtr.Zero, scr); }
        Invalidate();
    }

    // Le lastre (gruppi) piegano lo sfondo ai loro bordi.
    void BuildRefraction()
    {
        refractDirty = false;
        if (page != null) { page.Dispose(); page = null; }
        if (matter == null) return;
        IntPtr scr = N.GetDC(IntPtr.Zero);
        try
        {
            Dib p = GlassFx.Copy(scr, matter);
            foreach (El e in Els)
            {
                if (!e.Visible || e.LensRadius < 0) continue;
                GlassFx.Refract(matter, p, e.B, S(e.LensRadius), S(LensBand), S(LensShift), LensBright, true);
            }
            page = p;
        }
        finally { N.ReleaseDC(IntPtr.Zero, scr); }
    }

    public void InvalidateRefraction() { refractDirty = true; Invalidate(); }

    // Carattere del vetro (ogni finestra può renderlo più o meno estremo).
    protected virtual float GlassTint { get { return T.Dark ? 0.46f : 0.52f; } }
    protected virtual float GlassSaturation { get { return 1.35f; } }
    protected virtual int GlassBlur { get { return 8; } }
    protected virtual float AmbientOnGlass { get { return 0.45f; } }
    protected virtual float LensBand { get { return 16; } }
    protected virtual float LensShift { get { return 9; } }
    protected virtual int LensBright { get { return T.Dark ? 30 : 40; } }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected virtual int PageOffsetFor(El e) { return 0; }
    protected virtual bool Settled { get { return true; } }

    static readonly bool trace = Environment.GetEnvironmentVariable("DP_TRACE") == "1";

    protected override void OnPaint(PaintEventArgs e)
    {
        Stopwatch psw = trace ? Stopwatch.StartNew() : null;
        PaintAll(e);
        if (trace) System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dp_paint.txt"),
            psw.Elapsed.TotalMilliseconds.ToString("0.0") + " ms  " + e.ClipRectangle.Width + "x" + e.ClipRectangle.Height + Environment.NewLine);
    }

    void PaintAll(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Rectangle clip = e.ClipRectangle;
        int W = ClientSize.Width, H = ClientSize.Height;

        // 1) sfondo: vetro liquido (copia di pixel) oppure tinta piena
        if (GlassOn && matter != null)
        {
            if (refractDirty && Settled) BuildRefraction();
            Dib src = (page != null && Settled) ? page : matter;
            IntPtr hdc = g.GetHdc();
            N.BitBlt(hdc, clip.X, clip.Y, clip.Width, clip.Height, src.Dc, clip.X, clip.Y, 0x00CC0020);
            g.ReleaseHdc(hdc);
        }
        else g.Clear(T.Bg);
        Art.Quality(g);

        // 2) bagliore del tema (una bitmap riusata)
        string key = Art.AmbientKey(W, H) + GlassOn;
        if (ambient == null || key != ambientKey)
        {
            if (ambient != null) ambient.Dispose();
            ambient = Art.MakeAmbient(W, H, GlassOn ? AmbientOnGlass : 1f);
            ambientKey = key;
        }
        g.DrawImage(ambient, clip, clip, GraphicsUnit.Pixel);

        PaintBack(g);
        foreach (El el in Els)
        {
            if (!el.Visible) continue;
            int off = el.Page ? PageOffsetFor(el) : 0;
            Rectangle b = el.Dirty;
            b.Offset(0, off);
            if (!b.IntersectsWith(clip)) continue;
            if (off != 0)
            {
                GraphicsState st = g.Save();
                g.TranslateTransform(0, off);
                el.Paint(g);
                g.Restore(st);
            }
            else el.Paint(g);
        }
        if (PageCover > 0.01f)
        {
            Color c = T.Dark ? Color.FromArgb(255, 20, 20, 26) : Color.FromArgb(255, 240, 240, 245);
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(150 * PageCover), c))) g.FillRectangle(b, PageArea);
        }
        PaintFront(g);
    }

    protected virtual void PaintBack(Graphics g) { }
    protected virtual void PaintFront(Graphics g) { }
    protected virtual bool AnimateExtra() { return false; }

    El HitEl(Point p)
    {
        for (int i = Els.Count - 1; i >= 0; i--)
        {
            El e = Els[i];
            if (e.Visible && e.Interactive && e.Enabled && e.Hit(p)) return e;
        }
        return null;
    }

    // ── orologio ──
    public void Kick()
    {
        if (!clockOn) { clockOn = true; lastFrame = frameWatch.Elapsed.TotalMilliseconds; Clock.Want(this); }
        Invalidate();
    }

    public void PostFrame()
    {
        if (framePending) return;
        framePending = true;
        try { if (IsHandleCreated && !IsDisposed) BeginInvoke(frameAction); else framePending = false; }
        catch { framePending = false; }
    }

    void Frame()
    {
        framePending = false;
        double now = frameWatch.Elapsed.TotalMilliseconds;
        Dt = (float)Math.Max(0.25, Math.Min(4, (now - lastFrame) / 16.667));
        lastFrame = now;

        bool any = false;
        Rectangle dirty = Rectangle.Empty;
        foreach (El el in Els)
        {
            if (!el.Visible || !el.Animate()) continue;
            any = true;
            Rectangle r = el.Dirty;
            if (el.Page) r.Offset(0, PageOffsetFor(el));
            dirty = dirty.IsEmpty ? r : Rectangle.Union(dirty, r);
        }
        if (AnimateExtra()) { any = true; dirty = ClientRectangle; }
        if (!dirty.IsEmpty) { Invalidate(dirty); Update(); }
        if (!any) { clockOn = false; Clock.Drop(this); }
    }

    public void SetFocus(El e)
    {
        if (focus == e) return;
        if (focus != null) focus.Blur();
        focus = e;
    }

    protected void ClearEls(Predicate<El> which)
    {
        if (hot != null && which(hot)) hot = null;
        if (down != null && which(down)) down = null;
        if (focus != null && which(focus)) { focus.Blur(); focus = null; }
        Els.RemoveAll(which);
    }

    // Il riflesso sulle righe segue il mouse: ridisegniamo solo le righe interessate.
    void RevealMove(Point old, Point now)
    {
        bool kick = false;
        foreach (El e in Els)
        {
            if (!e.Reveal || !e.Visible) continue;
            bool a = e.B.Contains(old), b = e.B.Contains(now);
            if (a != b) kick = true;
        }
        if (kick) Kick();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point old = Mouse;
        Mouse = e.Location;
        RevealMove(old, Mouse);
        if (down != null) { down.Drag(e.Location); return; }
        El h = HitEl(e.Location);
        if (h != hot)
        {
            if (hot != null) hot.Hover = false;
            hot = h;
            if (h != null) h.Hover = true;
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            Kick();
        }
        if (hot != null) hot.Move(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        Point old = Mouse;
        Mouse = new Point(-9999, -9999);
        RevealMove(old, Mouse);
        if (hot != null && down == null) { hot.Hover = false; hot.Move(Mouse); hot = null; Kick(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        El h = HitEl(e.Location);
        if (h != focus) SetFocus(null);
        if (h == null) return;
        down = h;
        h.Pressed = true;
        Capture = true;
        h.Down(e.Location);
        Kick();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (down == null) return;
        El d = down;
        down = null;
        d.Pressed = false;
        Capture = false;
        if (d.Hit(e.Location)) d.Click(e.Location);
        Kick();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (focus != null && focus.Key(keyData)) { Kick(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Clock.Drop(this);
        refresh.Stop();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Clock.Drop(this);
            refresh.Dispose();
            FreeGlass();
            if (ambient != null) ambient.Dispose();
        }
        base.Dispose(disposing);
    }
}

// ───────────── Controlli ─────────────

// Lastra di vetro che raggruppa più righe (i suoi bordi piegano lo sfondo: vedi GlassForm.BuildRefraction).
class Group : El
{
    public float Radius = 18;
    public bool Clear;          // vetro "nudo": quasi nessun riempimento, solo un velo per leggere e il bordo di luce
    public override float LensRadius { get { return Radius; } }
    public override Rectangle Dirty { get { return Rectangle.Inflate(B, (int)S(8), (int)S(10)); } }
    public override void Paint(Graphics g)
    {
        if (Clear) Art.LiquidCached(g, B, S(Radius), 0, F.Sc, false, Color.FromArgb(T.Dark ? 26 : 30, T.Dark ? 0 : 255, T.Dark ? 0 : 255, T.Dark ? 0 : 255));
        else Art.LiquidCached(g, B, S(Radius), 0, F.Sc, true, Color.Transparent);
    }
}

// Riga dentro una lastra: titolo, descrizione, linea sottile di separazione; il controllo sta a destra.
class Row : El
{
    public string Title, Desc;
    public int RightPad = 70;
    public bool Last;
    public Func<string> Warn;    // se restituisce un testo, prende il posto della descrizione (in rosso)

    public Row() { Reveal = true; }
    public override Rectangle Dirty { get { return B; } }

    public override bool Animate() { return Ease(ref HoverK, B.Contains(F.Mouse) ? 1f : 0f, 0.25f); }

    public override void Paint(Graphics g)
    {
        if (HoverK > 0.01f)
            Art.FillRound(g, Color.FromArgb((int)((T.Dark ? 16 : 70) * HoverK), 255, 255, 255), RectangleF.Inflate(B, -S(5), -S(4)), S(13));
        string w = Warn != null ? Warn() : null;
        bool en = Enabled;
        float tw = B.Width - S(20) - S(RightPad);
        if (string.IsNullOrEmpty(Desc))
            Tx.DrawS(g, Title, Fx.Body, en ? T.Text1 : T.Text3, new RectangleF(B.X + S(20), B.Y, tw, B.Height));
        else
        {
            float top = B.Y + B.Height / 2f - S(20);
            Tx.DrawS(g, Title, Fx.Body, en ? T.Text1 : T.Text3, new RectangleF(B.X + S(20), top, tw, S(21)));
            Tx.DrawS(g, w ?? Desc, Fx.Small, w != null ? T.Danger : (en ? T.Text2 : T.Text3), new RectangleF(B.X + S(20), top + S(22), tw, S(19)));
        }
        if (!Last)
            using (Pen p = new Pen(Color.FromArgb(T.Dark ? 22 : 30, T.Dark ? 255 : 0, T.Dark ? 255 : 0, T.Dark ? 255 : 0), 1))
                g.DrawLine(p, B.X + S(20), B.Bottom - 0.5f, B.Right - S(20), B.Bottom - 0.5f);
    }
}

class Label : El
{
    public Func<string> Text;
    public Font Font;
    public Func<Color> Color;
    public StringAlignment Align = StringAlignment.Near;

    public override void Paint(Graphics g)
    {
        Tx.DrawS(g, Text(), Font ?? Fx.Small, Color != null ? Color() : T.Text2, B, Align, StringAlignment.Center);
    }
}

// Interruttore "Liquid Glass": traccia luminosa, manopola a bolla di vetro che fa da lente e si allunga come una goccia.
class Toggle : El
{
    readonly Func<bool> get;
    readonly Action<bool> set;
    float k = -1;

    public Toggle(Func<bool> g, Action<bool> s) { get = g; set = s; }
    public override bool Interactive { get { return true; } }
    public override void Click(Point p) { set(!get()); F.Kick(); }
    RectangleF Track { get { return new RectangleF(B.Right - S(24) - S(48), B.Y + B.Height / 2f - S(12), S(48), S(24)); } }
    public override Rectangle Dirty { get { return Rectangle.Round(RectangleF.Inflate(Track, S(16), S(14))); } }

    public override bool Animate()
    {
        bool a = base.Animate();
        float t = get() ? 1 : 0;
        if (k < 0) k = t;
        a |= Ease(ref k, t, 0.2f);
        return a;
    }

    public override void Paint(Graphics g)
    {
        if (k < 0) k = get() ? 1 : 0;
        bool en = Enabled;
        RectangleF tr = Track;
        float rad = tr.Height / 2;
        float glow = k * (en ? 1 : 0.3f);
        Art.FillRound(g, Color.FromArgb(T.Dark ? 50 : 22, 0, 0, 0), tr, rad);           // incavo
        if (glow > 0.01f)
        {
            for (int i = 3; i >= 1; i--)
            {
                RectangleF gr = RectangleF.Inflate(tr, i * S(2.5f), i * S(2.5f));
                using (LinearGradientBrush lg = T.Grad(gr, (int)(28 * glow / i), 0f)) Art.FillRound(g, lg, gr, gr.Height / 2);
            }
            using (LinearGradientBrush lg = T.Grad(tr, (int)(240 * glow), 0f)) Art.FillRound(g, lg, tr, rad);
            Art.Gloss(g, tr, rad, (int)(60 * k));
        }
        Art.Rim(g, tr, rad, F.Sc, 0.7f);

        float d = tr.Height + S(8) + S(2) * HoverK;
        float kw = d + S(10) * PressK;
        float x = tr.X - S(4) + (tr.Width + S(8) - kw) * k;
        RectangleF knob = new RectangleF(x, tr.Y + tr.Height / 2 - d / 2, kw, d);
        // lente: dentro la bolla si vede la traccia colorata, ingrandita
        if (glow > 0.01f)
            using (LinearGradientBrush lg = T.Grad(RectangleF.Inflate(tr, S(10), 0), (int)(150 * glow), 0f)) Art.FillRound(g, lg, RectangleF.Inflate(knob, -S(2), -S(2)), (d - S(4)) / 2);
        Art.BubbleCached(g, knob, T.Mix(T.P.C[0], T.P.C[1], k), glow, F.Sc);
    }
}

class Slider : El
{
    readonly int[] steps;
    readonly Func<int> get;
    readonly Action<int> set;
    readonly Func<int, string> fmt;
    float pos = -1;
    public int TrackW = 170;

    public Slider(int[] st, Func<int> g, Action<int> s, Func<int, string> f) { steps = st; get = g; set = s; fmt = f; }
    public override bool Interactive { get { return true; } }

    RectangleF Track { get { return new RectangleF(B.Right - S(28) - S(TrackW), B.Y + B.Height / 2f - S(2.5f), S(TrackW), S(5)); } }
    public override bool Hit(Point p) { return B.Contains(p) && p.X >= Track.X - S(14); }
    public override Rectangle Dirty { get { return Rectangle.Round(new RectangleF(Track.X - S(110), B.Y, Track.Width + S(130), B.Height)); } }

    int Idx()
    {
        int v = get(), best = 0;
        for (int i = 0; i < steps.Length; i++) if (Math.Abs(steps[i] - v) < Math.Abs(steps[best] - v)) best = i;
        return best;
    }

    void SetFrom(int x)
    {
        RectangleF tr = Track;
        float f = Math.Max(0, Math.Min(1, (x - tr.X) / tr.Width));
        int v = steps[(int)Math.Round(f * (steps.Length - 1))];
        if (v != get()) set(v);
        F.Kick();
    }

    public override void Down(Point p) { SetFrom(p.X); }
    public override void Drag(Point p) { SetFrom(p.X); }

    public override bool Animate()
    {
        bool a = base.Animate();
        float t = Idx() / (float)(steps.Length - 1);
        if (pos < 0) pos = t;
        a |= Ease(ref pos, t, 0.3f);
        return a;
    }

    public override void Paint(Graphics g)
    {
        if (pos < 0) pos = Idx() / (float)(steps.Length - 1);
        bool en = Enabled;
        RectangleF tr = Track;
        Art.FillRound(g, Color.FromArgb(T.Dark ? 60 : 30, 0, 0, 0), tr, tr.Height / 2);
        RectangleF fill = new RectangleF(tr.X, tr.Y, Math.Max(tr.Height, tr.Width * pos), tr.Height);
        if (en) using (LinearGradientBrush lg = T.Grad(tr, 255, 0f)) Art.FillRound(g, lg, fill, tr.Height / 2);
        else Art.FillRound(g, T.Text3, fill, tr.Height / 2);
        Art.Rim(g, tr, tr.Height / 2, F.Sc, 0.5f);

        float cx = tr.X + tr.Width * pos, cy = tr.Y + tr.Height / 2;
        float ro = S(12) + S(1.5f) * HoverK;
        float rw = ro * 2 + S(8) * PressK;
        RectangleF knob = new RectangleF(cx - rw / 2, cy - ro, rw, ro * 2);
        if (en)   // lente sul punto della traccia
            using (LinearGradientBrush lg = T.Grad(tr, 120, 0f)) Art.FillRound(g, lg, new RectangleF(knob.X + S(3), cy - S(4), knob.Width - S(6), S(8)), S(4));
        Art.BubbleCached(g, knob, en ? T.Accent : T.Text3, en ? 0.7f : 0, F.Sc);

        Tx.DrawS(g, fmt(steps[Idx()]), Fx.BodyB, en ? T.Text1 : T.Text3,
            new RectangleF(tr.X - S(104), B.Y, S(84), B.Height), StringAlignment.Far, StringAlignment.Center);
    }
}

class Segmented : El
{
    readonly string[] opts;
    readonly Func<int> get;
    readonly Action<int> set;
    float pos = -1;
    int hi = -1;
    public int SegW = 84;

    public Segmented(string[] o, Func<int> g, Action<int> s) { opts = o; get = g; set = s; }
    public override bool Interactive { get { return true; } }

    RectangleF Ctl { get { float w = S(SegW) * opts.Length; return new RectangleF(B.Right - S(20) - w, B.Y + B.Height / 2f - S(17), w, S(34)); } }
    public override bool Hit(Point p) { return Ctl.Contains(p); }
    public override Rectangle Dirty { get { return Rectangle.Round(RectangleF.Inflate(Ctl, S(8), S(8))); } }
    int IdxAt(Point p) { RectangleF c = Ctl; return Math.Max(0, Math.Min(opts.Length - 1, (int)((p.X - c.X) / S(SegW)))); }

    public override void Move(Point p) { int h = Ctl.Contains(p) ? IdxAt(p) : -1; if (h != hi) { hi = h; F.Kick(); } }
    public override void Click(Point p) { set(IdxAt(p)); F.Kick(); }

    public override bool Animate()
    {
        bool a = base.Animate();
        if (pos < 0) pos = get();
        a |= Ease(ref pos, get(), 0.22f);
        return a;
    }

    public override void Paint(Graphics g)
    {
        if (pos < 0) pos = get();
        bool en = Enabled;
        RectangleF c = Ctl;
        float cr = c.Height / 2;
        Art.FillRound(g, Color.FromArgb(T.Dark ? 45 : 16, 0, 0, 0), c, cr);   // binario incavato
        Art.Rim(g, c, cr, F.Sc, 0.5f);
        float sw = S(SegW);
        if (hi >= 0 && hi != get() && Hover)
            Art.FillRound(g, Color.FromArgb(T.Dark ? 14 : 60, 255, 255, 255), new RectangleF(c.X + hi * sw + S(3), c.Y + S(3), sw - S(6), c.Height - S(6)), (c.Height - S(6)) / 2);
        // lente di vetro colorata che scivola e si allunga mentre si muove
        float stretch = Math.Abs(pos - get()) * sw * 0.5f;
        RectangleF ind = new RectangleF(c.X + pos * sw + S(2) - stretch / 2, c.Y + S(2), sw - S(4) + stretch, c.Height - S(4));
        using (LinearGradientBrush lg = T.Grad(ind, en ? (T.Dark ? 130 : 160) : 45, 0f)) Art.FillRound(g, lg, ind, ind.Height / 2);
        Art.BubbleCached(g, ind, T.Accent, 0, F.Sc);
        for (int i = 0; i < opts.Length; i++)
        {
            bool sel = i == get();
            Color col = sel ? Color.White : (en ? T.Text1 : T.Text3);
            Tx.DrawS(g, opts[i], sel ? Fx.BodyB : Fx.Body, col, new RectangleF(c.X + i * sw, c.Y, sw, c.Height), StringAlignment.Center, StringAlignment.Center);
        }
    }
}

// Pulsante: 0 = gradiente, 1 = vetro, 2 = vetro chiaro (sopra i gradienti), 3 = vetro con testo rosso
class Pill : El
{
    public Func<string> Text;
    public Action OnClick;
    public int Style;
    public int IconKind = -1;

    public override bool Interactive { get { return true; } }
    public override void Click(Point p) { if (OnClick != null) OnClick(); }

    public override void Paint(Graphics g)
    {
        RectangleF r = B;
        r.Inflate(-S(1.5f) * PressK, -S(1.5f) * PressK);
        float rad = r.Height / 2;
        Color tc;
        if (Style == 0)
        {
            using (LinearGradientBrush lg = T.Grad(r, 255, 0f)) Art.FillRound(g, lg, r, rad);
            Art.Gloss(g, r, rad, 70);
            Art.Rim(g, r, rad, F.Sc, 1);
            tc = T.OnGrad();
        }
        else if (Style == 2)
        {
            Art.FillRound(g, Color.FromArgb((int)(26 + 26 * HoverK), 255, 255, 255), r, rad);
            Art.Gloss(g, r, rad, 60);
            Art.Rim(g, r, rad, F.Sc, 1.6f);
            tc = Color.White;
        }
        else
        {
            Art.LiquidCached(g, r, rad, HoverK, F.Sc, true, Color.Transparent);
            tc = Style == 3 ? T.Danger : T.Text1;
        }
        string s = Text();
        if (IconKind >= 0)
        {
            float tw = Tx.Width(g, s, Fx.BodyB);
            float total = S(16) + S(8) + tw;
            float ix = r.X + (r.Width - total) / 2;
            Art.NavIcon(g, IconKind, new RectangleF(ix, r.Y + r.Height / 2 - S(8), S(16), S(16)), tc, F.Sc);
            Tx.DrawS(g, s, Fx.BodyB, tc, new RectangleF(ix + S(24), r.Y, tw + S(4), r.Height), StringAlignment.Near, StringAlignment.Center);
        }
        else Tx.DrawS(g, s, Fx.BodyB, tc, r, StringAlignment.Center, StringAlignment.Center);
    }
}

// Campo per registrare una scorciatoia: clic, poi premi la combinazione.
class HotkeyBox : El
{
    readonly Func<int> get;
    readonly Action<int> set;
    readonly Func<bool> ok;
    bool rec;
    float pulse;

    public HotkeyBox(Func<int> g, Action<int> s, Func<bool> o) { get = g; set = s; ok = o; }
    public bool Recording { get { return rec; } }
    public override bool Interactive { get { return true; } }
    RectangleF Box { get { return new RectangleF(B.Right - S(20) - S(220), B.Y + B.Height / 2f - S(18), S(220), S(36)); } }
    public override bool Hit(Point p) { return Box.Contains(p); }
    public override Rectangle Dirty { get { return Rectangle.Round(RectangleF.Inflate(Box, S(6), S(6))); } }

    public override void Click(Point p)
    {
        if (rec) { Stop(); return; }
        rec = true;
        F.SetFocus(this);
        App.SuspendHotkeys(true);
        F.Kick();
    }

    void Stop()
    {
        if (!rec) return;
        rec = false;
        App.SuspendHotkeys(false);
        F.Kick();
    }

    public override void Blur() { Stop(); }

    public override bool Key(Keys k)
    {
        if (!rec) return false;
        Keys code = k & Keys.KeyCode;
        if (code == Keys.Escape && (k & Keys.Modifiers) == 0) { Stop(); return true; }
        if ((code == Keys.Back || code == Keys.Delete) && (k & Keys.Modifiers) == 0) { set(0); Stop(); return true; }
        int c = HotkeyCodec.FromKeys(k);
        if (c > 0) { set(c); Stop(); F.SetFocus(null); }
        return true;
    }

    public override bool Animate()
    {
        bool a = base.Animate();
        if (rec) { pulse += 0.07f * GlassForm.Dt; a = true; }
        return a;
    }

    public override void Paint(Graphics g)
    {
        RectangleF bx = Box;
        float rad = bx.Height / 2;
        Art.FillRound(g, Color.FromArgb(T.Dark ? 45 : 16, 0, 0, 0), bx, rad);
        if (rec)
        {
            int al = (int)(150 + 100 * Math.Sin(pulse));
            using (LinearGradientBrush lg = T.Grad(bx, al, 0f))
            using (Pen p = new Pen(lg, S(1.6f)))
            using (GraphicsPath gp = Art.Round(bx, rad)) g.DrawPath(p, gp);
            Tx.DrawS(g, Lang.L("Premi la combinazione…", "Press the keys…"), Fx.Body, T.Text2, bx, StringAlignment.Center, StringAlignment.Center);
            return;
        }
        if (!ok()) Art.DrawRound(g, T.A(T.Danger, 0.7f), bx, rad, S(1.4f));
        else Art.Rim(g, bx, rad, F.Sc, 0.6f);
        List<string> parts = HotkeyCodec.Parts(get());
        if (parts.Count == 0)
        {
            Tx.DrawS(g, Lang.L("Nessuna · clic per impostare", "None · click to set"), Fx.Small, T.Text3, bx, StringAlignment.Center, StringAlignment.Center);
            return;
        }
        float total = 0;
        float[] ws = new float[parts.Count];
        for (int i = 0; i < parts.Count; i++) { ws[i] = Math.Max(S(26), Tx.Width(g, parts[i], Fx.Key) + S(16)); total += ws[i] + S(6); }
        float x = bx.X + (bx.Width - total + S(6)) / 2;
        for (int i = 0; i < parts.Count; i++)
        {
            RectangleF kr = new RectangleF(x, bx.Y + S(6), ws[i], bx.Height - S(12));
            Art.LiquidCached(g, kr, kr.Height / 2, 0, F.Sc, false, Color.Transparent);
            Tx.DrawS(g, parts[i], Fx.Key, T.Text1, kr, StringAlignment.Center, StringAlignment.Center);
            x += ws[i] + S(6);
        }
    }
}

// Pulsanti della barra del titolo.
class CapBtn : El
{
    public int Kind;     // 0 chiudi, 1 riduci
    public Action OnClick;
    public override bool Interactive { get { return true; } }
    public override void Click(Point p) { OnClick(); }
    public override Rectangle Dirty { get { return B; } }

    public override void Paint(Graphics g)
    {
        if (HoverK > 0.01f)
        {
            RectangleF r = RectangleF.Inflate(B, -S(7), -S(4));
            Color bg = Kind == 0 ? Color.FromArgb((int)(230 * HoverK), 232, 72, 80) : Color.FromArgb((int)((T.Dark ? 30 : 90) * HoverK), 255, 255, 255);
            Art.FillRound(g, bg, r, r.Height / 2);
        }
        Color c = Kind == 0 && HoverK > 0.5f ? Color.White : T.Text1;
        float cx = B.X + B.Width / 2f, cy = B.Y + B.Height / 2f, u = S(4.5f);
        using (Pen p = new Pen(c, S(1.2f)))
        {
            p.StartCap = p.EndCap = LineCap.Round;
            if (Kind == 0) { g.DrawLine(p, cx - u, cy - u, cx + u, cy + u); g.DrawLine(p, cx - u, cy + u, cx + u, cy - u); }
            else g.DrawLine(p, cx - u, cy, cx + u, cy);
        }
    }
}

// Barra laterale: la lastra di vetro dell'elemento scelto scivola (e si allunga come una goccia).
class Nav : El
{
    readonly Func<string>[] items;
    readonly int[] icons;
    public int Sel;
    public Action<int> OnSelect;
    float pos = -1, bg = -1;
    int hi = -1;

    public Nav(Func<string>[] it, int[] ic) { items = it; icons = ic; }
    public override bool Interactive { get { return true; } }
    float ItemH { get { return S(40); } }
    float Gap { get { return S(4); } }
    int IdxAt(Point p) { int i = (int)((p.Y - B.Y) / (ItemH + Gap)); return i >= 0 && i < items.Length ? i : -1; }
    public override bool Hit(Point p) { return B.Contains(p) && IdxAt(p) >= 0; }
    public override void Move(Point p) { int h = B.Contains(p) ? IdxAt(p) : -1; if (h != hi) { hi = h; F.Kick(); } }
    public override void Click(Point p) { int i = IdxAt(p); if (i >= 0 && i != Sel) { Sel = i; if (OnSelect != null) OnSelect(i); F.Kick(); } }

    public override bool Animate()
    {
        bool a = base.Animate();
        if (pos < 0) pos = Sel;
        if (bg < 0) bg = Sel;
        a |= Ease(ref pos, Sel, 0.2f);
        a |= Ease(ref bg, Sel, 0.26f);
        return a;
    }

    public override void Paint(Graphics g)
    {
        if (pos < 0) pos = Sel;
        if (bg < 0) bg = Sel;
        float stretch = Math.Abs(bg - Sel) * S(16);
        RectangleF sr = new RectangleF(B.X, B.Y + bg * (ItemH + Gap) - stretch / 2, B.Width, ItemH + stretch);
        Art.FillRound(g, Color.FromArgb(T.Dark ? 18 : 90, 255, 255, 255), sr, ItemH / 2);
        Art.Gloss(g, sr, ItemH / 2, T.Dark ? 30 : 60);
        Art.Rim(g, sr, ItemH / 2, F.Sc, 1.1f);
        for (int i = 0; i < items.Length; i++)
        {
            RectangleF r = new RectangleF(B.X, B.Y + i * (ItemH + Gap), B.Width, ItemH);
            if (i == hi && Hover && i != Sel) Art.FillRound(g, Color.FromArgb(T.Dark ? 9 : 40, 255, 255, 255), r, ItemH / 2);
            Color c = i == Sel ? T.Text1 : T.Text2;
            Art.NavIcon(g, icons[i], new RectangleF(r.X + S(16), r.Y + r.Height / 2 - S(8), S(16), S(16)), c, F.Sc);
            Tx.DrawS(g, items[i](), i == Sel ? Fx.BodyB : Fx.Body, c, new RectangleF(r.X + S(44), r.Y, r.Width - S(50), r.Height));
        }
        float y = B.Y + pos * (ItemH + Gap) + ItemH / 2;
        float st2 = Math.Abs(pos - Sel) * S(18);
        RectangleF bar = new RectangleF(B.X + S(6), y - S(8) - st2 / 2, S(3), S(16) + st2);
        using (LinearGradientBrush lg = T.Grad(bar, 255, 90f)) Art.FillRound(g, lg, bar, S(1.5f));
    }
}

// Griglia di scelte a capsula (effetti): una lente di vetro colorata scivola sulla scelta attiva.
class Chips : El
{
    readonly Func<string>[] names;
    readonly Func<int> get;
    readonly Action<int> set;
    public int Cols = 4;
    int hi = -1;
    float px = -1, py = -1;

    public Chips(Func<string>[] n, Func<int> g, Action<int> s) { names = n; get = g; set = s; }
    public override bool Interactive { get { return true; } }
    float Gap { get { return S(10); } }
    float CW { get { return (B.Width - (Cols - 1) * Gap) / Cols; } }
    float CH { get { return S(42); } }
    public int Height { get { int rows = (names.Length + Cols - 1) / Cols; return (int)(rows * CH + (rows - 1) * Gap); } }
    RectangleF Cell(int i) { return new RectangleF(B.X + (i % Cols) * (CW + Gap), B.Y + (i / Cols) * (CH + Gap), CW, CH); }
    int At(Point p) { for (int i = 0; i < names.Length; i++) if (Cell(i).Contains(p)) return i; return -1; }

    public override bool Hit(Point p) { return At(p) >= 0; }
    public override void Move(Point p) { int h = At(p); if (h != hi) { hi = h; F.Kick(); } }
    public override void Click(Point p) { int i = At(p); if (i >= 0) { set(i); F.Kick(); } }

    public override bool Animate()
    {
        bool a = base.Animate();
        RectangleF c = Cell(get());
        if (px < 0) { px = c.X; py = c.Y; }
        a |= Ease(ref px, c.X, 0.22f);
        a |= Ease(ref py, c.Y, 0.22f);
        return a;
    }

    public override void Paint(Graphics g)
    {
        int sel = get();
        if (px < 0) { RectangleF c0 = Cell(sel); px = c0.X; py = c0.Y; }
        for (int i = 0; i < names.Length; i++)
        {
            RectangleF c = Cell(i);
            Art.FillRound(g, Color.FromArgb(T.Dark ? 34 : 14, 0, 0, 0), c, c.Height / 2);
            if (i == hi && Hover && i != sel) Art.FillRound(g, Color.FromArgb(T.Dark ? 16 : 70, 255, 255, 255), c, c.Height / 2);
            Art.Rim(g, c, c.Height / 2, F.Sc, 0.55f);
        }
        // lente: si allunga nella direzione in cui scivola
        RectangleF target = Cell(sel);
        float stretch = Math.Abs(px - target.X) * 0.25f;
        RectangleF lens = new RectangleF(Math.Min(px, target.X) + S(2) - stretch * 0.0f, py + S(2), CW - S(4) + stretch, CH - S(4));
        bool en = Enabled;
        using (System.Drawing.Drawing2D.LinearGradientBrush lg = T.Grad(lens, en ? (T.Dark ? 140 : 170) : 50, 0f)) Art.FillRound(g, lg, lens, lens.Height / 2);
        Art.BubbleCached(g, lens, T.Accent, en ? 0.35f : 0, F.Sc);
        for (int i = 0; i < names.Length; i++)
        {
            bool s = i == sel;
            Tx.DrawS(g, names[i](), s ? Fx.BodyB : Fx.Body, s ? Color.White : (en ? T.Text1 : T.Text3), Cell(i), StringAlignment.Center, StringAlignment.Center);
        }
    }
}

// Griglia dei temi a gradiente.
class Swatches : El
{
    readonly Palette[] ps;
    readonly Func<string> get;
    readonly Action<string> set;
    public int Cols = 4;
    int hi = -1;
    float lift;

    public Swatches(Palette[] p, Func<string> g, Action<string> s) { ps = p; get = g; set = s; }
    public override bool Interactive { get { return true; } }
    float Gap { get { return S(12); } }
    float TW { get { return (B.Width - (Cols - 1) * Gap) / Cols; } }
    float TH { get { return S(52); } }
    float CellH { get { return TH + S(26); } }

    RectangleF Tile(int i) { return new RectangleF(B.X + (i % Cols) * (TW + Gap), B.Y + (i / Cols) * (CellH + S(6)), TW, TH); }

    int IdxAt(Point p)
    {
        for (int i = 0; i < ps.Length; i++) { RectangleF t = Tile(i); t.Height += S(22); if (t.Contains(p)) return i; }
        return -1;
    }

    public override bool Hit(Point p) { return IdxAt(p) >= 0; }
    public override void Move(Point p) { int h = IdxAt(p); if (h != hi) { hi = h; lift = 0; F.Kick(); } }
    public override void Click(Point p) { int i = IdxAt(p); if (i >= 0) set(ps[i].Id); }

    public override bool Animate()
    {
        bool a = base.Animate();
        a |= Ease(ref lift, hi >= 0 && Hover ? 1 : 0, 0.3f);
        return a;
    }

    public override void Paint(Graphics g)
    {
        string cur = get();
        for (int i = 0; i < ps.Length; i++)
        {
            RectangleF t = Tile(i);
            bool sel = ps[i].Id == cur;
            float up = i == hi ? S(3) * lift : 0;
            t.Offset(0, -up);
            float rad = S(14);
            if (up > 0) Art.FillRound(g, Color.FromArgb((int)(40 * lift), 0, 0, 0), new RectangleF(t.X + S(2), t.Y + up + S(3), t.Width - S(4), t.Height), rad);
            Bitmap pv = Art.Preview(ps[i], (int)t.Width, (int)t.Height);
            using (GraphicsPath gp = Art.Round(t, rad))
            using (TextureBrush tb = new TextureBrush(pv, WrapMode.Clamp))
            {
                tb.TranslateTransform(t.X, t.Y);
                g.FillPath(tb, gp);
            }
            Art.Gloss(g, t, rad, 60);
            Art.Rim(g, t, rad, F.Sc, sel ? 2f : 1f);
            if (sel)
            {
                RectangleF badge = new RectangleF(t.Right - S(24), t.Y + S(6), S(18), S(18));
                Art.BubbleCached(g, badge, Color.White, 0, F.Sc);
                Art.Check(g, badge, Color.White, S(1.8f));
            }
            Tx.DrawS(g, ps[i].Name, sel ? Fx.BodyB : Fx.Small, sel ? T.Text1 : T.Text2,
                new RectangleF(t.X, t.Bottom + up + S(4), t.Width, S(18)), StringAlignment.Center, StringAlignment.Center);
        }
    }
}
