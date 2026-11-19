#nullable disable
// Transizioni: "liquida" (sfocatura progressiva, come vetro che si appanna) e "classica" (dissolvenza).
//
// Come funziona, senza scatti né pixel:
//  1. una "foto" nitida dello schermo copre il desktop (sopra il desktop, sotto le tue finestre),
//     così il cambio delle icone avviene di nascosto;
//  2. sopra si dissolvono due versioni sfocate (leggera → forte): la sfocatura cresce in modo continuo;
//  3. dentro la sfocatura le icone si sciolgono (o si condensano), poi tutto torna nitido (forte → leggera → reale).
// La sfocatura è un vero filtro morbido (media + sfocatura a box ripetuta + ingrandimento bilineare)
// calcolato in parallelo su tutti i core in pochi millisecondi; il resto lo compone la GPU (DWM).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopPulito.Infrastructure.Gpu;

class Overlay : NativeWindow
{
    public Overlay(int x, int y, int w, int h)
    {
        CreateParams cp = new CreateParams();
        cp.Style = unchecked((int)0x80000000);
        cp.ExStyle = 0x80000 | 0x20 | 0x80 | 0x08000000;     // LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE
        cp.X = x; cp.Y = y; cp.Width = w; cp.Height = h;
        CreateHandle(cp);
    }
}

// Immagine a 32 bit in memoria, con accesso diretto ai pixel.
unsafe class Dib : IDisposable
{
    public IntPtr Dc, Bmp, Old, Bits;
    public int W, H;

    public Dib(IntPtr scr, int w, int h)
    {
        W = w; H = h;
        N.BITMAPINFOHEADER bi = new N.BITMAPINFOHEADER();
        bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
        Bmp = N.CreateDIBSection(scr, ref bi, 0, out Bits, IntPtr.Zero, 0);
        Dc = N.CreateCompatibleDC(scr);
        Old = N.SelectObject(Dc, Bmp);
    }

    public static Dib Capture(IntPtr scr, Rectangle r)
    {
        Dib d = new Dib(scr, r.Width, r.Height);
        N.BitBlt(d.Dc, 0, 0, r.Width, r.Height, scr, r.X, r.Y, 0x00CC0020);
        N.GdiFlush();
        return d;
    }

    // Sfocatura morbida. f = quanto rimpicciolire (più alto = più sfocato), passes = passate di sfocatura a box.
    public Dib Blur(IntPtr scr, int f, int passes)
    {
        int sw = Math.Max(2, W / f), sh = Math.Max(2, H / f);
        int[] r = new int[sw * sh], g = new int[sw * sh], b = new int[sw * sh];
        IntPtr src = Bits;
        int W0 = W, H0 = H;
        int step = f >= 8 ? 2 : 1;

        // 1) media dei blocchi (riduzione pulita, senza aliasing)
        Parallel.For(0, sh, delegate(int y)
        {
            uint* p = (uint*)src;
            int y0 = y * H0 / sh, y1 = Math.Max(y0 + 1, (y + 1) * H0 / sh);
            for (int x = 0; x < sw; x++)
            {
                int x0 = x * W0 / sw, x1 = Math.Max(x0 + 1, (x + 1) * W0 / sw);
                int sr = 0, sg = 0, sb = 0, n = 0;
                for (int yy = y0; yy < y1; yy += step)
                {
                    uint* row = p + (long)yy * W0;
                    for (int xx = x0; xx < x1; xx += step)
                    {
                        uint c = row[xx];
                        sr += (int)((c >> 16) & 255); sg += (int)((c >> 8) & 255); sb += (int)(c & 255); n++;
                    }
                }
                int i = y * sw + x;
                r[i] = sr / n; g[i] = sg / n; b[i] = sb / n;
            }
        });

        // 2) sfocatura a box ripetuta (≈ gaussiana)
        int[] tr = new int[sw * sh], tg = new int[sw * sh], tb = new int[sw * sh];
        for (int k = 0; k < passes; k++)
        {
            BoxH(r, tr, sw, sh); BoxH(g, tg, sw, sh); BoxH(b, tb, sw, sh);
            BoxV(tr, r, sw, sh); BoxV(tg, g, sw, sh); BoxV(tb, b, sw, sh);
        }

        // 3) ingrandimento bilineare a piena risoluzione (in parallelo)
        Dib d = new Dib(scr, W, H);
        IntPtr dst = d.Bits;
        int[] xi = new int[W], xf = new int[W];
        for (int x = 0; x < W; x++)
        {
            int fx = (int)(((x + 0.5) * sw / W - 0.5) * 256);
            if (fx < 0) fx = 0;
            int ix = fx >> 8;
            if (ix >= sw - 1) { ix = sw - 2; fx = (sw - 1) << 8; }
            xi[x] = ix; xf[x] = fx - (ix << 8);
        }
        Parallel.For(0, H, delegate(int y)
        {
            uint* p = (uint*)dst + (long)y * W0;
            int fy = (int)(((y + 0.5) * sh / H0 - 0.5) * 256);
            if (fy < 0) fy = 0;
            int iy = fy >> 8;
            if (iy >= sh - 1) { iy = sh - 2; fy = (sh - 1) << 8; }
            int wy = fy - (iy << 8), wy0 = 256 - wy;
            int o0 = iy * sw, o1 = o0 + sw;
            for (int x = 0; x < W0; x++)
            {
                int a = o0 + xi[x], c = o1 + xi[x], wx = xf[x], wx0 = 256 - wx;
                int R = ((r[a] * wx0 + r[a + 1] * wx) * wy0 + (r[c] * wx0 + r[c + 1] * wx) * wy) >> 16;
                int G = ((g[a] * wx0 + g[a + 1] * wx) * wy0 + (g[c] * wx0 + g[c + 1] * wx) * wy) >> 16;
                int B = ((b[a] * wx0 + b[a + 1] * wx) * wy0 + (b[c] * wx0 + b[c + 1] * wx) * wy) >> 16;
                p[x] = 0xFF000000u | ((uint)R << 16) | ((uint)G << 8) | (uint)B;
            }
        });
        return d;
    }

    static void BoxH(int[] s, int[] d, int w, int h)
    {
        for (int y = 0; y < h; y++)
        {
            int o = y * w;
            for (int x = 0; x < w; x++)
            {
                int a = x > 0 ? s[o + x - 1] : s[o + x], c = x < w - 1 ? s[o + x + 1] : s[o + x];
                d[o + x] = (a + 2 * s[o + x] + c) >> 2;
            }
        }
    }

    static void BoxV(int[] s, int[] d, int w, int h)
    {
        for (int y = 0; y < h; y++)
        {
            int up = y > 0 ? -w : 0, dn = y < h - 1 ? w : 0;
            int o = y * w;
            for (int x = 0; x < w; x++) d[o + x] = (s[o + x + up] + 2 * s[o + x] + s[o + x + dn]) >> 2;
        }
    }

    public void Dispose()
    {
        if (Dc == IntPtr.Zero) return;
        N.SelectObject(Dc, Old);
        N.DeleteObject(Bmp);
        N.DeleteDC(Dc);
        Dc = IntPtr.Zero;
    }
}

// Uno strato sullo schermo: finestra trasparente al mouse con una copia dell'immagine (la tiene la GPU).
class Layer : IDisposable
{
    readonly Overlay w;
    N.BLENDFUNCTION bf;
    public readonly bool Hidden;     // esclusa dalle catture: ci permette di "fotografare" sotto di lei
    public IntPtr Handle { get { return w.Handle; } }

    public Layer(IntPtr scr, Dib s, Rectangle r, double alpha, IntPtr after) : this(scr, s, Point.Empty, r, alpha, after, false) { }

    // src: punto di partenza dentro l'immagine s; perPixel: l'immagine ha la sua trasparenza (sagome delle icone)
    public Layer(IntPtr scr, Dib s, Point src, Rectangle r, double alpha, IntPtr after, bool perPixel)
    {
        w = new Overlay(r.X, r.Y, r.Width, r.Height);
        if (!Fader.Recording)
            try { Hidden = N.SetWindowDisplayAffinity(w.Handle, 0x11); } catch { Hidden = false; }
        N.POINT pd = new N.POINT(r.X, r.Y), ps = new N.POINT(src.X, src.Y);
        N.SIZE sz = new N.SIZE(r.Width, r.Height);
        bf.SourceConstantAlpha = (byte)Math.Round(alpha * 255);
        bf.AlphaFormat = (byte)(perPixel ? 1 : 0);
        N.UpdateLayeredWindow(w.Handle, scr, ref pd, ref sz, s.Dc, ref ps, 0, ref bf, 2);
        N.SetWindowPos(w.Handle, after, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
    }

    public void Alpha(double a)
    {
        bf.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, Math.Round(a * 255)));
        N.UpdateLayeredWindow(w.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref bf, 2);
    }

    // sposta e cambia trasparenza in un'unica chiamata (la GPU sposta la texture: costo quasi nullo)
    public void Place(int x, int y, double a)
    {
        bf.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, Math.Round(a * 255)));
        N.POINT p = new N.POINT(x, y);
        N.UpdateLayeredWindow(w.Handle, IntPtr.Zero, ref p, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref bf, 2);
    }

    // ritaglia lo strato con una forma (il sistema si prende la proprietà della regione)
    public void Region(IntPtr rgn) { N.SetWindowRgn(w.Handle, rgn, true); }

    public void Dispose() { if (w.Handle != IntPtr.Zero) w.DestroyHandle(); }
}

static class Fader
{
    // curve: 0 = entra dolce, 1 = lineare, 2 = esce dolce, 3 = dolce/dolce
    static double Curve(double t, int kind)
    {
        switch (kind)
        {
            case 0: return 1 - Math.Cos(t * Math.PI / 2);
            case 1: return t;
            case 2: return Math.Sin(t * Math.PI / 2);
            default: return 0.5 - 0.5 * Math.Cos(t * Math.PI);
        }
    }

    // Animazione sincronizzata al refresh dello schermo (DwmFlush): fluida a 60/120/144 Hz.
    static void Animate(Layer l, double from, double to, int ms, int curve)
    {
        if (ms <= 0) { l.Alpha(to); return; }
        Stopwatch sw = Stopwatch.StartNew();
        while (true)
        {
            double t = Math.Min(1, sw.Elapsed.TotalMilliseconds / ms);
            l.Alpha(from + (to - from) * Curve(t, curve));
            if (t >= 1) break;
            N.DwmFlush();
        }
    }

    // Aspetta che Explorer abbia davvero disegnato le icone.
    static void WaitPainted(IntPtr dv)
    {
        IntPtr lv = N.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
        foreach (IntPtr h in new IntPtr[] { dv, lv })
            if (h != IntPtr.Zero && !N.IsHungAppWindow(h))
                N.RedrawWindow(h, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0080 | 0x0100);   // INVALIDATE | ALLCHILDREN | UPDATENOW
        N.DwmFlush();
    }

    // Modalità registrazione (DP_RECORD=1): gli strati restano visibili ai registratori di schermo.
    // Per "fotografare sotto" senza escluderli si chiede direttamente al desktop di disegnarsi (PrintWindow).
    public static readonly bool Recording = Environment.GetEnvironmentVariable("DP_RECORD") == "1";

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out N.RECT r);

    static Dib PrintDesktop(IntPtr scr, IntPtr host, Rectangle r)
    {
        N.RECT wr;
        GetWindowRect(host, out wr);
        Dib d = new Dib(scr, wr.R - wr.L, wr.B - wr.T);
        PrintWindow(host, d.Dc, 2);   // PW_RENDERFULLCONTENT
        N.GdiFlush();
        Dib o = new Dib(scr, r.Width, r.Height);
        N.BitBlt(o.Dc, wr.L - r.X, wr.T - r.Y, d.W, d.H, d.Dc, 0, 0, 0x00CC0020);
        N.GdiFlush();
        d.Dispose();
        return o;
    }

    static readonly bool trace = Environment.GetEnvironmentVariable("DP_TRACE") == "1";
    static Stopwatch tw;
    static void Mark(string what) { if (trace) System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dp_trace.txt"), tw.ElapsedMilliseconds + " ms  " + what + Environment.NewLine); }

    // Diagnostica: salva le due sfocature di una zona dello schermo e i tempi.
    public static void BlurTest(string prefix)
    {
        Rectangle r = new Rectangle(N.GetSystemMetrics(76), N.GetSystemMetrics(77), N.GetSystemMetrics(78), N.GetSystemMetrics(79));
        IntPtr scr = N.GetDC(IntPtr.Zero);
        Stopwatch sw = Stopwatch.StartNew();
        using (Dib s = Dib.Capture(scr, r))
        {
            long tc = sw.ElapsedMilliseconds; sw.Restart();
            using (Dib b1 = s.Blur(scr, 4, 2))
            {
                long t1 = sw.ElapsedMilliseconds; sw.Restart();
                using (Dib b2 = s.Blur(scr, 12, 3))
                {
                    long t2 = sw.ElapsedMilliseconds;
                    foreach (Dib d in new Dib[] { b1, b2 })
                        using (Bitmap bm = Image.FromHbitmap(d.Bmp))
                        using (Bitmap crop = bm.Clone(new Rectangle(0, 0, Math.Min(900, bm.Width), Math.Min(500, bm.Height)), System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                            crop.Save(prefix + (d == b1 ? "_light.png" : "_strong.png"));
                    System.IO.File.WriteAllText(prefix + "_times.txt", "schermo " + r.Width + "x" + r.Height + "  cattura " + tc + " ms  leggera " + t1 + " ms  forte " + t2 + " ms");
                }
            }
        }
        N.ReleaseDC(IntPtr.Zero, scr);
    }

    // ───── Cerchio e Tendina: la "foto" si ritaglia con una forma che si allarga ─────
    // Bordo morbido: due strati semitrasparenti sottili seguono il bordo (tre gradini di trasparenza).
    // A ogni fotogramma cambia solo la forma (regione): nessun pixel ricalcolato.
    static void Shape(IntPtr scr, Dib s0, Layer a, Rectangle r, IntPtr after, int ms, bool circle)
    {
        int band = Math.Max(6, (int)(10 * N.SystemDpi() / 96f));
        Layer e1 = new Layer(scr, s0, r, 0.55, after), e2 = new Layer(scr, s0, r, 0.22, after);
        e1.Region(N.CreateRectRgn(0, 0, 0, 0)); e2.Region(N.CreateRectRgn(0, 0, 0, 0));
        N.POINT cp; N.GetCursorPos(out cp);
        int ox = Math.Max(0, Math.Min(r.Width, cp.X - r.X)), oy = Math.Max(0, Math.Min(r.Height, cp.Y - r.Y));
        double R = 0;
        foreach (Point c in new Point[] { new Point(0, 0), new Point(r.Width, 0), new Point(0, r.Height), new Point(r.Width, r.Height) })
            R = Math.Max(R, Math.Sqrt((c.X - ox) * (double)(c.X - ox) + (c.Y - oy) * (double)(c.Y - oy)));
        R += band * 3;
        try
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                double t = Math.Min(1, sw.Elapsed.TotalMilliseconds / ms);
                double e = circle ? Curve(t, 3) : 1 - Math.Pow(1 - t, 3);
                if (circle)
                {
                    int rad = (int)(e * R);
                    a.Region(Annulus(r, ox, oy, rad, int.MaxValue));
                    e1.Region(Annulus(r, ox, oy, rad - band, rad));
                    e2.Region(Annulus(r, ox, oy, rad - 2 * band, rad - band));
                }
                else
                {
                    int x = (int)(e * (r.Width + band * 2));
                    a.Region(N.CreateRectRgn(x, 0, r.Width, r.Height));
                    e1.Region(N.CreateRectRgn(Math.Max(0, x - band), 0, Math.Max(0, x), r.Height));
                    e2.Region(N.CreateRectRgn(Math.Max(0, x - 2 * band), 0, Math.Max(0, x - band), r.Height));
                }
                if (t >= 1) break;
                N.DwmFlush();
            }
        }
        finally { e1.Dispose(); e2.Dispose(); }
    }

    // anello tra due raggi (outer = int.MaxValue: tutto ciò che sta fuori dal raggio interno)
    static IntPtr Annulus(Rectangle r, int ox, int oy, int inner, int outer)
    {
        IntPtr res = outer == int.MaxValue ? N.CreateRectRgn(0, 0, r.Width, r.Height) : N.CreateEllipticRgn(ox - outer, oy - outer, ox + outer, oy + outer);
        if (inner > 0)
        {
            IntPtr hole = N.CreateEllipticRgn(ox - inner, oy - inner, ox + inner, oy + inner);
            N.CombineRgn(res, res, hole, 4);   // RGN_DIFF
            N.DeleteObject(hole);
        }
        return res;
    }

    // ───── Gravità, Vola via, Onda: le icone ritagliate si muovono davvero ─────
    // Le sagome delle icone si ricavano confrontando il desktop con e senza icone, solo dentro i rettangoli
    // delle icone (così uno sfondo animato non disturba). Poi gli strati si spostano: lo fa la GPU.
    static void IconFx(IntPtr scr, Dib s0, ref Layer a, Rectangle r, IntPtr after, IntPtr dv, int ms, int style, bool show)
    {
        if (show) WaitPainted(dv); else N.DwmFlush();
        Dib s1 = Dib.Capture(scr, r);                       // sotto lo strato "a" (escluso dalle catture)
        Dib with = show ? s1 : s0, without = show ? s0 : s1;

        List<Rectangle> rects = new List<Rectangle>();
        foreach (Rectangle ir in Desk.IconRects)
        {
            Rectangle q = new Rectangle(ir.X - r.X - 2, ir.Y - r.Y - 2, ir.Width + 4, ir.Height + 4);
            q.Intersect(new Rectangle(0, 0, r.Width, r.Height));
            if (q.Width > 0 && q.Height > 0) rects.Add(q);
        }
        Dib sprite = new Dib(scr, r.Width, r.Height);       // nasce trasparente
        Extract(with, without, sprite, rects);
        s1.Dispose();
        Mark("sagome icone: " + rects.Count);
        if (trace)   // diagnostica: salva le sagome ritagliate
            try
            {
                using (Bitmap bm = Bitmap.FromHbitmap(sprite.Bmp))
                using (Bitmap crop = bm.Clone(new Rectangle(0, 0, Math.Min(900, bm.Width), Math.Min(560, bm.Height)), System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                    crop.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dp_sprite.png"));
            }
            catch { }

        // gruppi che si muovono insieme: tutte le icone, oppure (Onda) una colonna alla volta
        List<Rectangle> groups = new List<Rectangle>();
        if (style == 5)
        {
            rects.Sort(delegate(Rectangle p, Rectangle q) { return (p.X + p.Width / 2).CompareTo(q.X + q.Width / 2); });
            foreach (Rectangle q in rects)
            {
                int last = groups.Count - 1;
                if (last >= 0 && Math.Abs((q.X + q.Width / 2) - (groups[last].X + groups[last].Width / 2)) < q.Width / 2)
                    groups[last] = Rectangle.Union(groups[last], q);
                else groups.Add(q);
            }
        }
        else
        {
            Rectangle u = rects[0];
            foreach (Rectangle q in rects) u = Rectangle.Union(u, q);
            groups.Add(u);
        }

        N.POINT cp; N.GetCursorPos(out cp);
        int cx = cp.X - r.X;
        double maxD = 1;
        foreach (Rectangle gr in groups) maxD = Math.Max(maxD, Math.Abs(gr.X + gr.Width / 2 - cx));
        double spread = style == 5 ? 0.5 : 0;               // quota del tempo usata per l'effetto a cascata
        float sc = N.SystemDpi() / 96f;
        int dist = (int)((style == 6 ? 46 : 64) * sc);

        Layer[] ls = new Layer[groups.Count];
        double[] delay = new double[groups.Count];
        try
        {
            for (int i = 0; i < groups.Count; i++)
            {
                Rectangle gr = groups[i];
                ls[i] = new Layer(scr, sprite, gr.Location, new Rectangle(r.X + gr.X, r.Y + gr.Y, gr.Width, gr.Height), show ? 0 : 1, after, true);
                delay[i] = spread * Math.Abs(gr.X + gr.Width / 2 - cx) / maxD;
            }
            sprite.Dispose(); sprite = null;
            s0.Dispose();
            if (!show) { a.Dispose(); a = null; }           // sotto ci sono già le icone sparite

            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                double T = Math.Min(1, sw.Elapsed.TotalMilliseconds / ms);
                for (int i = 0; i < ls.Length; i++)
                {
                    double t = Math.Max(0, Math.Min(1, (T - delay[i]) / (1 - spread)));
                    double dy, al;
                    if (!show)
                    {
                        if (style == 6) { dy = -dist * (1 - Math.Pow(1 - t, 2)); al = 1 - t * t; }          // salgono e svaniscono
                        else { dy = dist * t * t; al = 1 - Math.Pow(t, 1.6); }                                 // cadono, accelerando
                    }
                    else
                    {
                        if (style == 6) { dy = dist * Math.Pow(1 - t, 3); al = Math.Min(1, t * 1.6); }          // risalgono al loro posto
                        else { dy = -dist * Bounce(t); al = Math.Min(1, t * 2); }                            // cadono e rimbalzano
                    }
                    ls[i].Place(r.X + groups[i].X, r.Y + groups[i].Y + (int)Math.Round(dy), al);
                }
                if (T >= 1) break;
                N.DwmFlush();
            }
        }
        finally
        {
            if (sprite != null) sprite.Dispose();
            foreach (Layer l in ls) if (l != null) l.Dispose();
        }
    }

    // spostamento residuo per la caduta con rimbalzo (1 → 0, con un piccolo rimbalzo verso la fine)
    static double Bounce(double t)
    {
        double e;   // easeOutBounce standard
        double n = 7.5625, d = 2.75;
        if (t < 1 / d) e = n * t * t;
        else if (t < 2 / d) { t -= 1.5 / d; e = n * t * t + 0.75; }
        else if (t < 2.5 / d) { t -= 2.25 / d; e = n * t * t + 0.9375; }
        else { t -= 2.625 / d; e = n * t * t + 0.984375; }
        return 1 - e;
    }

    // Sagome delle icone: dove "con" e "senza" differiscono, dentro i rettangoli delle icone (alfa premoltiplicato).
    static unsafe void Extract(Dib with, Dib without, Dib dst, List<Rectangle> rects)
    {
        IntPtr pw = with.Bits, po = without.Bits, pd = dst.Bits;
        int W = with.W;
        Parallel.ForEach(rects, delegate(Rectangle q)
        {
            uint* a = (uint*)pw, b = (uint*)po, d = (uint*)pd;
            for (int y = q.Y; y < q.Bottom; y++)
            {
                long row = (long)y * W;
                for (int x = q.X; x < q.Right; x++)
                {
                    uint c = a[row + x], o = b[row + x];
                    int dr = Math.Abs((int)((c >> 16) & 255) - (int)((o >> 16) & 255));
                    int dg = Math.Abs((int)((c >> 8) & 255) - (int)((o >> 8) & 255));
                    int db = Math.Abs((int)(c & 255) - (int)(o & 255));
                    int diff = Math.Max(dr, Math.Max(dg, db));
                    int al = (diff - 14) * 6;
                    if (al <= 0) continue;
                    if (al > 255) al = 255;
                    uint R = ((c >> 16) & 255) * (uint)al / 255, G = ((c >> 8) & 255) * (uint)al / 255, B = (c & 255) * (uint)al / 255;
                    d[row + x] = ((uint)al << 24) | (R << 16) | (G << 8) | B;
                }
            }
        });
    }

    // Effetti sulla scheda video: cattura del "dopo" sotto la copertura, poi lo shader fa il resto.
    static bool Gpu(IntPtr scr, Dib s0, ref Layer a, Rectangle r, IntPtr after, IntPtr dv, int ms, int style, bool show)
    {
        if (show) WaitPainted(dv); else N.DwmFlush();
        using (Dib s1 = Dib.Capture(scr, r))
        {
            N.POINT cp; N.GetCursorPos(out cp);
            PointF origin = new PointF(Math.Max(0, Math.Min(1, (cp.X - r.X) / (float)r.Width)), Math.Max(0, Math.Min(1, (cp.Y - r.Y) / (float)r.Height)));
            Layer cover = a;
            try
            {
                Mark("gpu: avvio");
                // la copertura resta sotto fino alla fine: riempie i pixel lasciati scoperti per gli sfondi animati
                GpuTransition.Play(r, after, s0.Bits, s1.Bits, ms, style - 7, origin, delegate { Mark("gpu: primo fotogramma"); });
                cover.Dispose();
                a = null;
                Mark("gpu: fine");
                return true;
            }
            catch (Exception ex) { Mark("gpu non disponibile: " + ex.Message); return false; }
        }
    }

    // style: 0 liquida, 1 dissolvenza, 2 cerchio, 3 tendina, 4 gravità, 5 onda, 6 vola via,
    //        7 bruciatura, 8 pixel, 9 glitch, 10 increspatura, 11 inchiostro (GPU)
    // change: il cambiamento da nascondere (predefinito: mostra/nascondi icone)
    public static void Run(bool show, int ms, int style) { Run(show, ms, style, null, true); }

    public static void Run(bool show, int ms, int style, Action change, bool iconsOnly)
    {
        if (change == null) change = Desk.ToggleRaw;
        if (!iconsOnly && style >= 4 && style <= 6) style = 0;     // gli effetti "sulle icone" non hanno senso se cambia lo sfondo
        IntPtr dv = Desk.DefView();
        if (dv == IntPtr.Zero || ms <= 0) { change(); return; }
        IntPtr host = N.GetParent(dv);
        Rectangle r = new Rectangle(N.GetSystemMetrics(76), N.GetSystemMetrics(77), N.GetSystemMetrics(78), N.GetSystemMetrics(79));

        // subito sopra il desktop, sotto tutte le finestre aperte
        IntPtr after = N.GetWindow(host, 3);
        if (after != IntPtr.Zero && (N.GetWindowLong(after, -20) & 0x8) != 0) after = (IntPtr)(-2);

        tw = Stopwatch.StartNew(); Mark(show ? "--- mostra" : "--- nascondi");
        IntPtr scr = N.GetDC(IntPtr.Zero);
        Dib s0 = null;
        Layer a = null, l1 = null, l2 = null, m2 = null, m1 = null;
        bool toggled = false;
        try
        {
            s0 = Dib.Capture(scr, r); Mark("cattura");
            a = new Layer(scr, s0, r, 1, after);          // foto nitida: nasconde il cambio
            N.DwmFlush();
            change(); toggled = true; Mark("cambio");

            if (style >= 7 && !Recording && a.Hidden && Gpu(scr, s0, ref a, r, after, dv, ms, style, show)) return;
            if (style >= 7) style = 0;                                   // GPU non disponibile: effetto liquido

            if (style == 2 || style == 3)
            {
                Shape(scr, s0, a, r, after, ms, style == 2);
                return;
            }
            if (style >= 4 && style <= 6 && a.Hidden && Desk.IconRects != null && Desk.IconRects.Count > 0)
            {
                IconFx(scr, s0, ref a, r, after, dv, ms, style, show);
                return;
            }

            if (style != 0 || (!a.Hidden && !Recording))
            {
                s0.Dispose(); s0 = null;
                if (show) WaitPainted(dv);
                Animate(a, 1, 0, ms, 3);
                return;
            }

            // Icone che spariscono (e registrazione): non serve fotografare il "dopo".
            // Nitido → sfocato leggero → sfocato forte, poi la sfocatura si dissolve direttamente nel desktop
            // reale: le icone si sciolgono nel vetro mentre tutto torna nitido. Una cattura sola.
            if (!show || Recording)
            {
                Dib c1 = s0.Blur(scr, 4, 2), c2 = s0.Blur(scr, 12, 3);
                s0.Dispose(); s0 = null; Mark("sfocature");
                l1 = new Layer(scr, c1, r, 0, after); c1.Dispose();
                l2 = new Layer(scr, c2, r, 0, after); c2.Dispose();
                Animate(l1, 0, 1, (int)(ms * 0.22), 0);
                a.Dispose(); a = null;
                Animate(l2, 0, 1, (int)(ms * 0.24), 1);
                l1.Dispose(); l1 = null;
                if (show) N.DwmFlush();
                Animate(l2, 1, 0, (int)(ms * 0.54), 3);
                Mark("fine");
                return;
            }

            // Icone che tornano: il "dopo" (desktop con le icone) si prepara in parallelo mentre l'animazione
            // è già partita, così le icone compaiono sfocate dentro il vetro e poi diventano nitide.
            Dib n1 = null, n2 = null;
            Thread worker = new Thread(delegate()
            {
                IntPtr dc = N.GetDC(IntPtr.Zero);
                try
                {
                    if (show) WaitPainted(dv); else N.DwmFlush();
                    using (Dib s1 = Recording ? PrintDesktop(dc, host, r) : Dib.Capture(dc, r))
                    {
                        n1 = s1.Blur(dc, 4, 2);
                        n2 = s1.Blur(dc, 12, 3);
                    }
                }
                catch { }
                finally { N.ReleaseDC(IntPtr.Zero, dc); }
            });
            worker.IsBackground = true;
            worker.Start();

            // il "prima": due livelli di sfocatura
            Dib b1 = s0.Blur(scr, 4, 2), b2 = s0.Blur(scr, 12, 3);
            s0.Dispose(); s0 = null; Mark("sfocature prima");
            l1 = new Layer(scr, b1, r, 0, after); b1.Dispose();
            l2 = new Layer(scr, b2, r, 0, after); b2.Dispose();

            double T0 = ms;
            Animate(l1, 0, 1, (int)(T0 * 0.18), 0);       // nitido → sfocato leggero
            a.Dispose(); a = null;
            Animate(l2, 0, 1, (int)(T0 * 0.18), 1);       // → sfocato forte
            l1.Dispose(); l1 = null;
            worker.Join(2000); Mark("dopo pronto");

            if (n1 != null && n2 != null)
            {
                // sotto la sfocatura forte "di prima" mettiamo le sfocature "di dopo"
                m2 = new Layer(scr, n2, r, 1, l2.Handle); n2.Dispose(); n2 = null;
                m1 = new Layer(scr, n1, r, 1, m2.Handle); n1.Dispose(); n1 = null;
                Animate(l2, 1, 0, (int)(T0 * 0.24), 1);   // le icone si sciolgono / si condensano nel vetro
                l2.Dispose(); l2 = null;
                Animate(m2, 1, 0, (int)(T0 * 0.18), 1);   // la sfocatura si alleggerisce
                m2.Dispose(); m2 = null;
                Animate(m1, 1, 0, (int)(T0 * 0.22), 2);   // e torna tutto nitido
            }
            else
            {
                if (n1 != null) n1.Dispose();
                if (n2 != null) n2.Dispose();
                Animate(l2, 1, 0, (int)(T0 * 0.4), 2);
            }
            Mark("fine");
        }
        finally
        {
            if (!toggled) change();
            foreach (Layer l in new Layer[] { a, l1, l2, m2, m1 }) if (l != null) l.Dispose();
            if (s0 != null) s0.Dispose();
            N.ReleaseDC(IntPtr.Zero, scr);
        }
    }
}
