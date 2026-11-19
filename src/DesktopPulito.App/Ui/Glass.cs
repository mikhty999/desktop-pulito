#nullable disable
// Vetro liquido e orologio delle animazioni.
//
// Vetro liquido (simulazione del "Liquid Glass"):
//  - si fotografa ciò che sta dietro la finestra (escludendo la finestra stessa per un istante),
//  - lo si sfoca, si aumenta la saturazione (vibrancy) e si tinge leggermente,
//  - ai bordi della finestra e di ogni lastra lo sfondo viene piegato come da una lente,
//    con una lieve separazione dei colori (aberrazione cromatica) e una luce che corre sul bordo.
// Tutto in parallelo su più core e solo quando serve (apertura, spostamento, ogni pochi secondi):
// tra un aggiornamento e l'altro il disegno è una semplice copia di pixel.
//
// Orologio: un thread che si sveglia a ogni fotogramma dello schermo (DwmFlush) e fa avanzare le
// animazioni solo delle finestre che ne hanno bisogno. A riposo dorme: zero processore.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Clock
{
    static Thread th;
    static readonly object lk = new object();
    static readonly List<GlassForm> subs = new List<GlassForm>();
    static readonly AutoResetEvent wake = new AutoResetEvent(false);

    public static void Want(GlassForm f)
    {
        lock (lk)
        {
            if (!subs.Contains(f)) subs.Add(f);
            if (th == null)
            {
                th = new Thread(Loop);
                th.IsBackground = true;
                th.Name = "FrameClock";
                th.Start();
            }
        }
        wake.Set();
    }

    public static void Drop(GlassForm f) { lock (lk) subs.Remove(f); }

    static void Loop()
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (true)
        {
            GlassForm[] arr;
            lock (lk) arr = subs.ToArray();
            if (arr.Length == 0) { wake.WaitOne(); continue; }
            long t0 = sw.ElapsedTicks;
            if (N.DwmFlush() != 0 || sw.ElapsedTicks - t0 < Stopwatch.Frequency / 2000) Thread.Sleep(7);   // senza composizione
            foreach (GlassForm f in arr) f.PostFrame();
        }
    }
}

static unsafe class GlassFx
{
    public static Dib Copy(IntPtr scr, Dib s)
    {
        Dib d = new Dib(scr, s.W, s.H);
        N.BitBlt(d.Dc, 0, 0, s.W, s.H, s.Dc, 0, 0, 0x00CC0020);
        N.GdiFlush();
        return d;
    }

    // Luminosità media dello sfondo (campionata a griglia: costa pochi microsecondi).
    public static float MeanLum(Dib d)
    {
        uint* p = (uint*)d.Bits;
        long sum = 0; int n = 0;
        int sx = Math.Max(1, d.W / 48), sy = Math.Max(1, d.H / 36);
        for (int y = sy / 2; y < d.H; y += sy)
            for (int x = sx / 2; x < d.W; x += sx)
            {
                uint c = p[(long)y * d.W + x];
                sum += (((c >> 16) & 255) * 54 + ((c >> 8) & 255) * 183 + (c & 255) * 19) >> 8;
                n++;
            }
        return n == 0 ? 0 : sum / (255f * n);
    }

    // Sfocatura + vivacità + tinta: la "materia" del vetro.
    public static Dib Material(IntPtr scr, Dib cap, Color tint, float tintA, float sat, int blurF)
    {
        Dib b = cap.Blur(scr, blurF, 2);
        IntPtr bits = b.Bits;
        int W = b.W;
        int tr = tint.R, tg = tint.G, tb = tint.B;
        int ta = (int)(tintA * 256), ka = 256 - ta, s = (int)(sat * 256);
        Parallel.For(0, b.H, delegate(int y)
        {
            uint* p = (uint*)bits + (long)y * W;
            for (int x = 0; x < W; x++)
            {
                uint c = p[x];
                int r = (int)((c >> 16) & 255), g = (int)((c >> 8) & 255), bl = (int)(c & 255);
                int l = (r * 54 + g * 183 + bl * 19) >> 8;
                r = l + (((r - l) * s) >> 8); g = l + (((g - l) * s) >> 8); bl = l + (((bl - l) * s) >> 8);
                r = (r * ka + tr * ta) >> 8; g = (g * ka + tg * ta) >> 8; bl = (bl * ka + tb * ta) >> 8;
                if (r < 0) r = 0; else if (r > 255) r = 255;
                if (g < 0) g = 0; else if (g > 255) g = 255;
                if (bl < 0) bl = 0; else if (bl > 255) bl = 255;
                p[x] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)bl;
            }
        });
        return b;
    }

    // Bordo a lente di un rettangolo arrotondato: legge da src, scrive su dst (solo la fascia del bordo).
    //  outward = true: lo sfondo appena fuori dalla lastra viene "risucchiato" dentro il bordo (lente convessa).
    public static void Refract(Dib src, Dib dst, RectangleF r, float rad, float band, float shift, float bright, bool outward)
    {
        int W = src.W, H = src.H;
        IntPtr sp = src.Bits, dp = dst.Bits;
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        float hx = r.Width / 2 - rad, hy = r.Height / 2 - rad;
        int y0 = Math.Max(0, (int)r.Y), y1 = Math.Min(H, (int)Math.Ceiling(r.Bottom));
        int x0 = Math.Max(0, (int)r.X), x1 = Math.Min(W, (int)Math.Ceiling(r.Right));
        float sign = outward ? 1f : -1f;
        Parallel.For(y0, y1, delegate(int y)
        {
            uint* s = (uint*)sp;
            uint* d = (uint*)dp + (long)y * W;
            float py = y + 0.5f - cy, qy = Math.Abs(py) - hy;
            int innerA = x1, innerB = x1;   // tratto centrale da saltare (interno, lontano dal bordo)
            if (qy < -band - rad)
            {
                innerA = Math.Max(x0, (int)(cx - (hx - band)) + 1);
                innerB = Math.Min(x1, (int)(cx + (hx - band)) - 1);
            }
            for (int x = x0; x < x1; x++)
            {
                if (x == innerA && innerB > innerA) { x = innerB; continue; }
                float px = x + 0.5f - cx, qx = Math.Abs(px) - hx;
                float ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0;
                float outLen = (float)Math.Sqrt(ox * ox + oy * oy);
                float dist = outLen + Math.Min(Math.Max(qx, qy), 0) - rad;    // < 0 dentro
                if (dist >= 0 || dist < -band) continue;
                float t = 1 + dist / band;                                       // 0 → 1 verso il bordo
                float nx, ny;
                if (qx > 0 && qy > 0) { nx = ox / outLen; ny = oy / outLen; }
                else if (qx > qy) { nx = 1; ny = 0; }
                else { nx = 0; ny = 1; }
                if (px < 0) nx = -nx;
                if (py < 0) ny = -ny;
                float disp = t * t * shift * sign;
                int R = Sample(s, W, H, x + nx * disp * 1.25f, y + ny * disp * 1.25f, 16);
                int G = Sample(s, W, H, x + nx * disp, y + ny * disp, 8);
                int B = Sample(s, W, H, x + nx * disp * 0.75f, y + ny * disp * 0.75f, 0);
                // luce: forte in alto a sinistra, riflessa in basso a destra, filo sottile su tutto il bordo
                float L = Math.Max(0, -nx * 0.55f - ny * 0.83f) + 0.45f * Math.Max(0, nx * 0.55f + ny * 0.83f);
                float t3 = t * t * t, t8 = t3 * t3 * t * t;
                int add = (int)(bright * (t3 * L + t8 * 0.6f));
                R += add; G += add; B += add;
                if (R > 255) R = 255; if (G > 255) G = 255; if (B > 255) B = 255;
                d[x] = 0xFF000000u | ((uint)R << 16) | ((uint)G << 8) | (uint)B;
            }
        });
    }

    static int Sample(uint* s, int W, int H, float fx, float fy, int shiftBits)
    {
        int x = (int)fx, y = (int)fy;
        if (x < 0) x = 0; else if (x >= W) x = W - 1;
        if (y < 0) y = 0; else if (y >= H) y = H - 1;
        return (int)((s[(long)y * W + x] >> shiftBits) & 255);
    }
}
