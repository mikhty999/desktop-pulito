#nullable disable
// Finestre a comparsa in vetro: "Cerca sul desktop" e il menu dell'icona vicino all'orologio.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class SearchPanel : GlassForm
{
    const int ROWS = 7;
    readonly List<DItem> all;
    List<DItem> shown = new List<DItem>();
    string q = "";
    int sel, top, hi = -1;
    float selY = -1;
    bool caretOn = true;
    readonly Timer caret = new Timer();
    readonly Dictionary<string, int> recent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    int RowH { get { return S(50); } }
    int InputH { get { return S(68); } }
    int FootH { get { return S(40); } }
    Rectangle ListArea { get { return new Rectangle(S(10), InputH + S(6), ClientSize.Width - S(20), ROWS * RowH); } }

    public SearchPanel() : base(false)
    {
        TopMost = true;
        Text = "Cerca sul desktop";
        all = DesktopItems.Load();
        for (int i = 0; i < App.Cfg.Recent.Count; i++) recent[App.Cfg.Recent[i]] = i;
        ClientSize = new Size(S(660), InputH + S(6) + ROWS * RowH + S(6) + FootH);
        DragH = InputH;     // si trascina dalla zona del campo di ricerca
        Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Point saved = new Point(App.Cfg.SearchX, App.Cfg.SearchY);
        bool onScreen = false;
        foreach (Screen s in Screen.AllScreens)
            if (s.WorkingArea.Contains(new Rectangle(saved, new Size(Width / 2, InputH)))) onScreen = true;
        Location = onScreen ? saved : new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (int)(wa.Height * 0.2f));
        caret.Interval = 530;
        caret.Tick += delegate { caretOn = !caretOn; Invalidate(new Rectangle(0, 0, ClientSize.Width, InputH)); };
        caret.Start();
        Filter();
    }

    void Filter()
    {
        string nq = DesktopItems.Norm(q.Trim());
        List<KeyValuePair<float, DItem>> scored = new List<KeyValuePair<float, DItem>>();
        foreach (DItem it in all)
        {
            float s = DesktopItems.Score(it, nq);
            if (s > 0) scored.Add(new KeyValuePair<float, DItem>(s, it));
        }
        scored.Sort(delegate(KeyValuePair<float, DItem> a, KeyValuePair<float, DItem> b)
        {
            int c = b.Key.CompareTo(a.Key);
            if (c != 0) return c;
            int ra, rb;
            bool ha = recent.TryGetValue(a.Value.Path, out ra), hb = recent.TryGetValue(b.Value.Path, out rb);
            if (ha && hb) return ra.CompareTo(rb);
            if (ha != hb) return ha ? -1 : 1;
            return string.Compare(a.Value.Name, b.Value.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        shown = new List<DItem>(scored.Count);
        foreach (KeyValuePair<float, DItem> kv in scored) shown.Add(kv.Value);
        sel = 0; top = 0;
        Kick();
    }

    void MoveSel(int d)
    {
        if (shown.Count == 0) return;
        sel = Math.Max(0, Math.Min(shown.Count - 1, sel + d));
        if (sel < top) top = sel;
        if (sel >= top + ROWS) top = sel - ROWS + 1;
        Kick();
    }

    void Open(bool reveal)
    {
        if (sel < 0 || sel >= shown.Count) return;
        DItem it = shown[sel];
        Hide();
        App.Cfg.Touch(it.Path);
        DesktopItems.Open(it, reveal);
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        switch (k)
        {
            case Keys.Escape: if (q.Length > 0) { q = ""; Filter(); } else Close(); return true;
            case Keys.Down: MoveSel(1); return true;
            case Keys.Up: MoveSel(-1); return true;
            case Keys.PageDown: MoveSel(ROWS); return true;
            case Keys.PageUp: MoveSel(-ROWS); return true;
            case Keys.Enter: Open(false); return true;
            case Keys.Control | Keys.Enter: Open(true); return true;
            case Keys.Back: if (q.Length > 0) { q = q.Substring(0, q.Length - 1); Filter(); } return true;
            case Keys.Control | Keys.Back:
                {
                    string t = q.TrimEnd();
                    int i = t.LastIndexOf(' ');
                    q = i < 0 ? "" : t.Substring(0, i + 1);
                    Filter();
                    return true;
                }
            case Keys.Control | Keys.V:
                try { string c = Clipboard.GetText().Replace("\r", " ").Replace("\n", " "); if (c.Length > 0) { q += c; Filter(); } } catch { }
                return true;
        }
        return base.ProcessCmdKey(ref msg, k);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (e.KeyChar >= ' ' && q.Length < 80) { q += e.KeyChar; caretOn = true; Filter(); }
        e.Handled = true;
    }

    int RowAt(Point p)
    {
        Rectangle la = ListArea;
        if (!la.Contains(p)) return -1;
        int i = top + (p.Y - la.Y) / RowH;
        return i < shown.Count ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = RowAt(e.Location);
        if (h != hi) { hi = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; if (h >= 0) { sel = h; } Kick(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int h = RowAt(e.Location);
        if (h >= 0) { sel = h; Open(e.Button == MouseButtons.Right); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int max = Math.Max(0, shown.Count - ROWS);
        top = Math.Max(0, Math.Min(max, top - Math.Sign(e.Delta) * 2));
        sel = Math.Max(top, Math.Min(top + ROWS - 1, sel));
        Kick();
    }

    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (App.Cfg.SearchX != Location.X || App.Cfg.SearchY != Location.Y)
        {
            App.Cfg.SearchX = Location.X; App.Cfg.SearchY = Location.Y;
            App.Cfg.Save();
        }
        base.OnFormClosed(e);
    }

    protected override bool AnimateExtra()
    {
        float target = sel - top;
        if (selY < 0) selY = target;
        return El.Ease(ref selY, target, 0.4f);
    }

    protected override void PaintBack(Graphics g)
    {
        int W = ClientSize.Width;
        // campo di ricerca: capsula di vetro in rilievo
        RectangleF field = new RectangleF(S(12), S(14), W - S(24), InputH - S(18));
        Art.Liquid(g, field, field.Height / 2, 0, Sc, true, Color.Transparent);
        // maniglia: si può trascinare
        Art.FillRound(g, T.A(T.Text3, 0.45f), new RectangleF(W / 2f - S(18), S(6), S(36), S(4)), S(2));
        // campo di ricerca
        Art.NavIcon(g, 5, new RectangleF(S(26), InputH / 2 - S(10), S(20), S(20)), T.Text2, Sc * 1.2f);
        RectangleF tr = new RectangleF(S(62), 0, W - S(62) - S(120), InputH);
        if (q.Length == 0)
            Tx.Draw(g, Lang.L("Cerca sul desktop…", "Search your desktop…"), Fx.Search, T.Text3, tr);
        else
            Tx.Draw(g, q, Fx.Search, T.Text1, tr);
        if (caretOn)
        {
            float cxp = tr.X + (q.Length == 0 ? 0 : Tx.Width(g, q, Fx.Search)) + S(1);
            using (LinearGradientBrush lg = T.Grad(new RectangleF(cxp, InputH / 2 - S(13), S(2), S(26)), 255, 90f))
                g.FillRectangle(lg, cxp, InputH / 2 - S(13), S(2), S(26));
        }
        Tx.Draw(g, shown.Count + (shown.Count == 1 ? Lang.L(" elemento", " item") : Lang.L(" elementi", " items")), Fx.Small, T.Text3,
            new RectangleF(W - S(140), 0, S(120), InputH), StringAlignment.Far, StringAlignment.Center);

        // risultati
        Rectangle la = ListArea;
        if (shown.Count == 0)
        {
            Tx.Draw(g, all.Count == 0 ? Lang.L("Il desktop è vuoto", "Your desktop is empty") : Lang.L("Nessun elemento trovato", "Nothing found"), Fx.Body, T.Text3, la, StringAlignment.Center, StringAlignment.Center);
        }
        else
        {
            if (selY >= -0.5f && selY < ROWS)
            {
                RectangleF hr = new RectangleF(la.X, la.Y + selY * RowH + S(2), la.Width, RowH - S(4));
                Art.Liquid(g, hr, hr.Height / 2, 0, Sc, true, Color.Transparent);
                using (LinearGradientBrush lg = T.Grad(hr, T.Dark ? 55 : 45, 0f)) Art.FillRound(g, lg, hr, hr.Height / 2);
                Art.Rim(g, hr, hr.Height / 2, Sc, 1.2f);
            }
            for (int i = 0; i < ROWS && top + i < shown.Count; i++)
            {
                DItem it = shown[top + i];
                DesktopItems.LoadIcon(it);   // solo le righe visibili: veloce anche con centinaia di file
                float ry = la.Y + i * RowH;
                int isz = S(32);
                if (it.Icon != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(it.Icon, new RectangleF(la.X + S(14), ry + (RowH - isz) / 2f, isz, isz));
                }
                Tx.Draw(g, it.Name, top + i == sel ? Fx.BodyB : Fx.Body, T.Text1, new RectangleF(la.X + S(60), ry, la.Width - S(60) - S(190), RowH));
                Tx.Draw(g, it.Kind ?? "", Fx.Small, T.Text3, new RectangleF(la.Right - S(190), ry, S(176), RowH), StringAlignment.Far, StringAlignment.Center);
            }
            if (shown.Count > ROWS)
            {
                float h = la.Height * ROWS / (float)shown.Count, yy = la.Y + la.Height * top / (float)shown.Count;
                Art.FillRound(g, T.Track, new RectangleF(la.Right + S(3), yy, S(3), h), S(1.5f));
            }
        }

        // suggerimenti
        float fy = ClientSize.Height - FootH;
        using (Pen p = new Pen(T.Stroke, 1)) g.DrawLine(p, 0, fy, W, fy);
        float x = S(20);
        x = Hint(g, x, fy, Lang.L("Invio", "Enter"), Lang.L("apri", "open"));
        x = Hint(g, x, fy, Lang.L("Ctrl+Invio", "Ctrl+Enter"), Lang.L("mostra nella cartella", "show in folder"));
        x = Hint(g, x, fy, "Esc", Lang.L("chiudi", "close"));
        Tx.Draw(g, Lang.L("trascina dall'alto per spostare", "drag the top to move"), Fx.Tiny, T.Text3, new RectangleF(x, fy, W - x - S(20), FootH), StringAlignment.Far, StringAlignment.Center);
    }

    float Hint(Graphics g, float x, float fy, string key, string what)
    {
        float kw = Tx.Width(g, key, Fx.Key) + S(12);
        RectangleF kr = new RectangleF(x, fy + (FootH - S(22)) / 2, kw, S(22));
        Art.Liquid(g, kr, S(6), 0, Sc, false, Color.Transparent);
        Tx.Draw(g, key, Fx.Key, T.Text2, kr, StringAlignment.Center, StringAlignment.Center);
        float tw = Tx.Width(g, what, Fx.Small);
        Tx.Draw(g, what, Fx.Small, T.Text3, new RectangleF(kr.Right + S(7), fy, tw + S(4), FootH));
        return kr.Right + S(7) + tw + S(22);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            caret.Dispose();
            foreach (DItem it in all) if (it.Icon != null) { it.Icon.Dispose(); it.Icon = null; }
        }
        base.Dispose(disposing);
    }
}

// ───────────── Menu ─────────────
class MItem
{
    public string Text, Hint;
    public bool Bold, Sep, HasCheck, Check, Disabled;
    public Action Act;
}

class GlassMenu : GlassForm
{
    readonly List<MItem> items;
    int hi = -1;
    float hiY = -1;
    bool done;

    int ItemH { get { return S(36); } }
    int SepH { get { return S(9); } }
    int Pad { get { return S(6); } }

    public GlassMenu(List<MItem> it) : base(false)
    {
        items = it;
        TopMost = true;
        Text = "Menu";
        int h = Pad * 2;
        foreach (MItem m in items) h += m.Sep ? SepH : ItemH;
        ClientSize = new Size(S(280), h);
    }

    public void ShowAt(Point p)
    {
        Rectangle wa = Screen.FromPoint(p).WorkingArea;
        int x = Math.Max(wa.X + S(8), Math.Min(p.X - Width / 2, wa.Right - Width - S(8)));
        int y = p.Y - Height - S(10);
        if (y < wa.Y) y = Math.Min(p.Y + S(10), wa.Bottom - Height);
        y = Math.Min(y, wa.Bottom - Height - S(8));
        Location = new Point(x, y);
        Show();
        Activate();
        N.SetForegroundWindow(Handle);
    }

    float ItemY(int idx)
    {
        float yy = Pad;
        for (int i = 0; i < idx; i++) yy += items[i].Sep ? SepH : ItemH;
        return yy;
    }

    int At(Point p)
    {
        float yy = Pad;
        for (int i = 0; i < items.Count; i++)
        {
            float h = items[i].Sep ? SepH : ItemH;
            if (p.Y >= yy && p.Y < yy + h && p.X >= 0 && p.X < ClientSize.Width) return items[i].Sep || items[i].Disabled ? -1 : i;
            yy += h;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = At(e.Location);
        if (h != hi) { hi = h; Kick(); }
    }

    protected override void OnMouseLeave(EventArgs e) { hi = -1; Kick(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int h = At(e.Location);
        if (h >= 0) Run(h);
    }

    void Run(int i)
    {
        if (done) return;
        done = true;
        Action a = items[i].Act;
        Close();
        if (a != null) App.Later(a);      // aspettiamo che il menu sparisca (serve alla dissolvenza)
    }

    void Step(int d)
    {
        int n = items.Count, i = hi;
        for (int k = 0; k < n; k++)
        {
            i = (i + d + n) % n;
            if (!items[i].Sep && !items[i].Disabled) { hi = i; Kick(); return; }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        if (k == Keys.Escape) { Close(); return true; }
        if (k == Keys.Down) { Step(1); return true; }
        if (k == Keys.Up) { Step(-1); return true; }
        if (k == Keys.Enter && hi >= 0) { Run(hi); return true; }
        return base.ProcessCmdKey(ref msg, k);
    }

    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (!done) { done = true; Close(); } }

    protected override bool AnimateExtra()
    {
        if (hi < 0) return false;
        float t = ItemY(hi);
        if (hiY < 0) hiY = t;
        return El.Ease(ref hiY, t, 0.45f);
    }

    protected override void PaintBack(Graphics g)
    {
        int W = ClientSize.Width;
        if (hi >= 0 && hiY >= 0)
        {
            RectangleF hr = new RectangleF(Pad, hiY + S(1), W - 2 * Pad, ItemH - S(2));
            Art.Liquid(g, hr, hr.Height / 2, 0, Sc, false, Color.Transparent);
            using (LinearGradientBrush lg = T.Grad(hr, T.Dark ? 60 : 45, 0f)) Art.FillRound(g, lg, hr, hr.Height / 2);
            Art.Rim(g, hr, hr.Height / 2, Sc, 1.1f);
        }
        float yy = Pad;
        foreach (MItem m in items)
        {
            if (m.Sep)
            {
                using (Pen p = new Pen(T.Stroke, 1)) g.DrawLine(p, Pad + S(10), yy + SepH / 2f, W - Pad - S(10), yy + SepH / 2f);
                yy += SepH;
                continue;
            }
            RectangleF r = new RectangleF(Pad, yy, W - 2 * Pad, ItemH);
            Color c = m.Disabled ? T.Text3 : T.Text1;
            if (m.HasCheck && m.Check) Art.Check(g, new RectangleF(r.X + S(10), r.Y + r.Height / 2 - S(7), S(14), S(14)), T.Accent, S(1.8f));
            float hintW = string.IsNullOrEmpty(m.Hint) ? 0 : Tx.Width(g, m.Hint, Fx.Small) + S(12);
            Tx.Draw(g, m.Text, m.Bold ? Fx.BodyB : Fx.Body, c, new RectangleF(r.X + S(34), r.Y, r.Width - S(34) - S(10) - hintW, r.Height));
            if (!string.IsNullOrEmpty(m.Hint))
                Tx.Draw(g, m.Hint, Fx.Small, T.Text3, new RectangleF(r.Right - S(130), r.Y, S(120), r.Height), StringAlignment.Far, StringAlignment.Center);
            yy += ItemH;
        }
    }
}

