#nullable disable
// Temi a gradiente, colori, caratteri e disegno (gradienti "mesh", icone, glifi).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using Microsoft.Win32;

class Palette
{
    public string Id; string it, en;
    public string Name { get { return Lang.En ? en : it; } }
    public Color[] C;
    public Palette(string id, string name, params int[] rgb)
    {
        Id = id; string[] nm = name.Split('|'); it = nm[0]; en = nm.Length > 1 ? nm[1] : nm[0];
        C = new Color[rgb.Length];
        for (int i = 0; i < rgb.Length; i++) C[i] = Color.FromArgb(255, (rgb[i] >> 16) & 255, (rgb[i] >> 8) & 255, rgb[i] & 255);
    }

    public static readonly Palette[] All = {
        new Palette("aurora",   "Aurora",   0x2BF0C0, 0x7F5CFF, 0x0A84FF, 0x14C98F),
        new Palette("tramonto", "Tramonto|Sunset", 0xFF6A2E, 0xFF2E88, 0x8E2DE2, 0xFFC24B),
        new Palette("lime",     "Lime",     0xC6FF3D, 0x2FD86A, 0x00A37A, 0xF5E663),
        new Palette("oceano",   "Oceano|Ocean",   0x2E8BFF, 0x00D4FF, 0x5B3CFF, 0x0FF0B3),
        new Palette("neon",     "Neon",     0xFF3CAC, 0x5B4BFF, 0x2B86C5, 0x00F5D4),
        new Palette("prisma",   "Prisma|Prism",   0xFF4D6D, 0xFFD23F, 0x3EE89B, 0x4D7CFF),
        new Palette("grafite",  "Grafite|Graphite",  0xC9CDD3, 0x6B7079, 0xF2F3F5, 0x3C4048),
        null };   // l'ultimo è "Sistema", costruito dal colore d'accento di Windows

    public static Palette System()
    {
        Color a = Color.FromArgb(255, 0, 120, 212);
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
            {
                byte[] b = k == null ? null : k.GetValue("AccentPalette") as byte[];
                if (b != null && b.Length >= 32) a = Color.FromArgb(255, b[12], b[13], b[14]);
            }
        }
        catch { }
        Palette p = new Palette("sistema", "Sistema|System");
        p.C = new Color[] { T.Mix(a, Color.White, 0.25f), a, T.Mix(a, Color.Black, 0.35f), T.Mix(a, Color.FromArgb(255, 255, 255, 255), 0.55f) };
        return p;
    }

    public static Palette[] List()
    {
        Palette[] l = (Palette[])All.Clone();
        l[l.Length - 1] = System();
        return l;
    }

    public static Palette Find(string id)
    {
        foreach (Palette p in List()) if (p.Id == id) return p;
        return All[0];
    }
}

// ───────────── Colori dell'interfaccia ─────────────
static class T
{
    public static bool Dark, SysDark, Motion;
    public static Palette P;
    public static Color Bg, Tint, Card, CardHover, Card2, Stroke, Text1, Text2, Text3, Accent, AccentText, Track, Thumb, SwitchOff, Danger;
    public static Color EdgeTop, EdgeBottom;

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static Color A(Color c, float alpha) { return Color.FromArgb((int)(Math.Max(0, Math.Min(1, alpha)) * c.A), c); }
    public static Color A(int alpha, int rgb) { return Color.FromArgb(alpha, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

    public static float Lum(Color c) { return (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f; }

    static int Dword(string key, string name, int def)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key))
            {
                object v = k == null ? null : k.GetValue(name);
                return v is int ? (int)v : def;
            }
        }
        catch { return def; }
    }

    public static void Load(Config c)
    {
        const string PK = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        bool sysAppDark = Dword(PK, "AppsUseLightTheme", 1) == 0;
        SysDark = Dword(PK, "SystemUsesLightTheme", 0) == 0;
        Dark = c.ThemeMode == 2 || (c.ThemeMode == 0 && sysAppDark);
        // Decide l'interruttore "Animazioni" dell'app (molti PC hanno le animazioni di Windows spente
        // per prestazioni: le nostre costano pochissimo, quindi restano attive finché non le spegni tu).
        Motion = c.Motion;
        P = Palette.Find(c.Palette);

        // accento leggibile: più chiaro sul tema scuro, più scuro sul tema chiaro
        Color a = P.C[0];
        if (Dark) { while (Lum(a) < 0.55f) a = Mix(a, Color.White, 0.2f); }
        else { while (Lum(a) > 0.42f) a = Mix(a, Color.Black, 0.18f); }
        Accent = a;
        AccentText = Lum(a) > 0.55f ? A(255, 0x101014) : Color.White;
        Danger = A(255, 0xE5484D);

        // Vetro: tinta leggera (si vede lo sfondo sfocato), schede quasi trasparenti con bordi di luce.
        bool glass = c.Glass && N.HasBackdrop;
        if (Dark)
        {
            Bg = A(255, 0x17171C); Tint = A(glass ? 38 : 255, 0x0E0E12);
            Card = A(9, 0xFFFFFF); CardHover = A(18, 0xFFFFFF); Card2 = A(6, 0xFFFFFF); Stroke = A(20, 0xFFFFFF);
            EdgeTop = A(58, 0xFFFFFF); EdgeBottom = A(8, 0xFFFFFF);
            Text1 = A(255, 0xFFFFFF); Text2 = A(255, 0xCACAD4); Text3 = A(255, 0x90909C);
            Track = A(55, 0xFFFFFF); Thumb = A(255, 0xF4F4F8); SwitchOff = A(16, 0xFFFFFF);
        }
        else
        {
            Bg = A(255, 0xF3F3F6); Tint = A(glass ? 55 : 255, 0xFAFAFC);
            Card = A(85, 0xFFFFFF); CardHover = A(150, 0xFFFFFF); Card2 = A(12, 0x000000); Stroke = A(18, 0x000000);
            EdgeTop = A(230, 0xFFFFFF); EdgeBottom = A(16, 0x000000);
            Text1 = A(255, 0x111116); Text2 = A(255, 0x4E4E58); Text3 = A(255, 0x84848E);
            Track = A(45, 0x000000); Thumb = A(255, 0xFFFFFF); SwitchOff = A(8, 0x000000);
        }
    }

    // Pennellata a gradiente con i colori del tema.
    public static LinearGradientBrush Grad(RectangleF r, int alpha, float angle)
    {
        if (r.Width < 1) r.Width = 1;
        if (r.Height < 1) r.Height = 1;
        LinearGradientBrush b = new LinearGradientBrush(r, Color.Black, Color.Black, angle);
        ColorBlend cb = new ColorBlend(3);
        cb.Colors = new Color[] { Color.FromArgb(alpha, P.C[0]), Color.FromArgb(alpha, P.C[1]), Color.FromArgb(alpha, P.C[2]) };
        cb.Positions = new float[] { 0f, 0.55f, 1f };
        b.InterpolationColors = cb;
        return b;
    }

    // Testo leggibile sopra il gradiente.
    public static Color OnGrad()
    {
        float l = (Lum(P.C[0]) + Lum(P.C[1])) / 2f;
        return l > 0.62f ? A(255, 0x101014) : Color.White;
    }
}

// ───────────── Caratteri ─────────────
static class Fx
{
    static float sc;
    public static Font Title, Hero, H, Body, BodyB, Small, Tiny, Key, Search;

    public static void Init(float s)
    {
        if (Title != null && Math.Abs(s - sc) < 0.01f) return;
        sc = s;
        Title = F("Segoe UI Semibold", 26);
        Hero = F("Segoe UI Semibold", 24);
        H = F("Segoe UI Semibold", 13.5f);
        Body = F("Segoe UI", 15);
        BodyB = F("Segoe UI Semibold", 15);
        Small = F("Segoe UI", 12.5f);
        Tiny = F("Segoe UI", 11);
        Key = F("Segoe UI Semibold", 11);
        Search = F("Segoe UI", 20);
    }

    static Font F(string fam, float px) { return new Font(fam, px * sc, GraphicsUnit.Pixel); }
}

static class Tx
{
    public static void Draw(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h, StringAlignment v)
    {
        using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
        using (SolidBrush b = new SolidBrush(c))
        {
            sf.Alignment = h; sf.LineAlignment = v;
            sf.Trimming = StringTrimming.EllipsisCharacter;
            sf.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            g.DrawString(s, f, b, r, sf);
        }
    }

    public static void Draw(Graphics g, string s, Font f, Color c, RectangleF r)
    {
        Draw(g, s, f, c, r, StringAlignment.Near, StringAlignment.Center);
    }

    public static void Wrap(Graphics g, string s, Font f, Color c, RectangleF r)
    {
        using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
        using (SolidBrush b = new SolidBrush(c))
        {
            sf.Trimming = StringTrimming.EllipsisWord;
            g.DrawString(s, f, b, r, sf);
        }
    }

    // Testo con un'ombra morbida di 1 px: resta leggibile sopra qualsiasi cosa ci sia dietro il vetro.
    public static void DrawS(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h, StringAlignment v)
    {
        if (T.Dark || T.Lum(c) > 0.6f)
        {
            RectangleF sr = r; sr.Offset(0, Math.Max(1f, f.Size / 14f));
            Draw(g, s, f, Color.FromArgb((int)(c.A * 0.45f), 0, 0, 0), sr, h, v);
        }
        Draw(g, s, f, c, r, h, v);
    }

    public static void DrawS(Graphics g, string s, Font f, Color c, RectangleF r)
    {
        DrawS(g, s, f, c, r, StringAlignment.Near, StringAlignment.Center);
    }

    public static float Width(Graphics g, string s, Font f)
    {
        using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
        {
            sf.FormatFlags = StringFormatFlags.MeasureTrailingSpaces;
            return g.MeasureString(s, f, 10000, sf).Width;
        }
    }
}

// ───────────── Gradiente "mesh" animato ─────────────
// Trucco di ottimizzazione: le macchie di colore si disegnano in una bitmap minuscola (40 px)
// e poi si ingrandiscono con filtro bilineare: la sfocatura è gratis.
class MeshCanvas : IDisposable
{
    Bitmap tiny, big;

    public void Draw(Graphics g, RectangleF r, Palette p, float t, float radius) { Draw(g, r, p, t, radius, 1f); }

    // alpha < 1: vetro colorato, lo sfondo si vede attraverso il gradiente
    public void Draw(Graphics g, RectangleF r, Palette p, float t, float radius, float alpha)
    {
        int bw = 40, bh = Math.Max(8, (int)(40 * r.Height / Math.Max(1, r.Width)));
        if (tiny == null || tiny.Width != bw || tiny.Height != bh)
        {
            if (tiny != null) tiny.Dispose();
            tiny = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb);
        }
        int W = Math.Max(1, (int)Math.Ceiling(r.Width)), H = Math.Max(1, (int)Math.Ceiling(r.Height));
        if (big == null || big.Width != W || big.Height != H)
        {
            if (big != null) big.Dispose();
            big = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
        }
        Art.MeshInto(tiny, p, t);
        using (Graphics bg = Graphics.FromImage(big))
        {
            bg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            bg.PixelOffsetMode = PixelOffsetMode.Half;
            if (alpha >= 0.999f) bg.DrawImage(tiny, new RectangleF(-W * 0.06f, -H * 0.06f, W * 1.12f, H * 1.12f));
            else
            {
                bg.Clear(Color.Transparent);
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = alpha;
                    ia.SetColorMatrix(cm);
                    bg.DrawImage(tiny, Rectangle.Round(new RectangleF(-W * 0.06f, -H * 0.06f, W * 1.12f, H * 1.12f)), 0, 0, tiny.Width, tiny.Height, GraphicsUnit.Pixel, ia);
                }
            }
        }
        using (GraphicsPath path = Art.Round(r, radius))
        using (TextureBrush tb = new TextureBrush(big, WrapMode.Clamp))
        {
            tb.TranslateTransform(r.X, r.Y);
            g.FillPath(tb, path);
        }
    }

    public void Dispose()
    {
        if (tiny != null) tiny.Dispose();
        if (big != null) big.Dispose();
        tiny = big = null;
    }
}

static class Art
{
    public static GraphicsPath Round(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        rad = Math.Max(0.1f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
    {
        using (GraphicsPath p = Round(r, rad)) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
    }

    public static void FillRound(Graphics g, Brush b, RectangleF r, float rad)
    {
        using (GraphicsPath p = Round(r, rad)) g.FillPath(b, p);
    }

    public static void DrawRound(Graphics g, Color c, RectangleF r, float rad, float w)
    {
        using (GraphicsPath p = Round(r, rad)) using (Pen pen = new Pen(c, w)) g.DrawPath(pen, p);
    }

    // Disegna le macchie di colore nella bitmap minuscola.
    public static void MeshInto(Bitmap tiny, Palette p, float t)
    {
        int bw = tiny.Width, bh = tiny.Height;
        using (Graphics tg = Graphics.FromImage(tiny))
        {
            tg.SmoothingMode = SmoothingMode.AntiAlias;
            tg.Clear(T.Mix(p.C[2], Color.Black, 0.3f));
            for (int i = 0; i < p.C.Length; i++)
            {
                float ph = i * 1.9f;
                float cx = bw * (0.15f + 0.7f * (0.5f + 0.5f * (float)Math.Sin(t * 0.55f + ph)));
                float cy = bh * (0.5f + 0.42f * (float)Math.Cos(t * 0.45f + ph * 1.3f));
                float rad = bw * (0.34f + 0.07f * (float)Math.Sin(t * 0.8f + ph * 0.7f));
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                    using (PathGradientBrush b = new PathGradientBrush(gp))
                    {
                        b.CenterColor = p.C[i];
                        b.SurroundColors = new Color[] { Color.FromArgb(0, p.C[i]) };
                        tg.FillPath(b, gp);
                    }
                }
            }
        }
    }

    // Anteprime statiche dei temi (in cache finché il tema non cambia).
    static readonly Dictionary<string, Bitmap> previews = new Dictionary<string, Bitmap>();

    public static Bitmap Preview(Palette p, int w, int h)
    {
        string key = p.Id + w + "x" + h + p.C[0].ToArgb();
        Bitmap b;
        if (previews.TryGetValue(key, out b)) return b;
        using (Bitmap tiny = new Bitmap(24, Math.Max(6, 24 * h / Math.Max(1, w)), PixelFormat.Format32bppPArgb))
        {
            MeshInto(tiny, p, 1.4f);
            b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(tiny, new RectangleF(-w * 0.08f, -h * 0.08f, w * 1.16f, h * 1.16f));
            }
        }
        previews[key] = b;
        return b;
    }

    public static void ClearCache()
    {
        foreach (Bitmap b in previews.Values) b.Dispose();
        previews.Clear();
        ClearGlassCache();
    }

    // Scheda di vetro: riempimento quasi trasparente, bordo di luce (chiaro in alto, sfumato in basso)
    // e un riflesso morbido che segue il mouse.
    // ───── Liquid Glass ─────
    // Lastra di vetro: ombra morbida sotto, riempimento chiarissimo, lucentezza in alto,
    // riflesso sull'angolo e un bordo che "rifrange" la luce (chiaro in alto a sinistra e in basso a destra).
    public static void Liquid(Graphics g, RectangleF r, float rad, float hover, float sc, bool shadow, Color tint)
    {
        if (shadow)
            for (int i = 3; i >= 1; i--)
            {
                RectangleF s = r; s.Inflate(i * sc * 0.8f, i * sc * 0.8f); s.Offset(0, i * sc * 1.6f);
                FillRound(g, Color.FromArgb(T.Dark ? 16 / i + 4 : 8 / i + 2, 0, 0, 0), s, rad + i * sc);
            }
        FillRound(g, T.Mix(T.Card, T.CardHover, hover), r, rad);
        if (tint.A > 0) FillRound(g, tint, r, rad);
        using (GraphicsPath p = Round(r, rad))
        {
            Region old = g.Clip;
            g.SetClip(p, CombineMode.Intersect);
            // lucentezza: metà superiore più chiara
            RectangleF top = new RectangleF(r.X, r.Y - 1, r.Width, r.Height * 0.6f + 1);
            using (LinearGradientBrush lg = new LinearGradientBrush(RectangleF.Inflate(top, 0, 2), Color.FromArgb(T.Dark ? 22 : 60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillRectangle(lg, top);
            // riflesso morbido sull'angolo in alto a sinistra
            float gw = Math.Min(r.Width * 0.7f, 260 * sc), gh = Math.Max(r.Height * 1.2f, 40 * sc);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(r.X - gw * 0.25f, r.Y - gh * 0.55f, gw, gh);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Color.FromArgb((int)((T.Dark ? 22 : 45) * (1 + hover * 0.6f)), 255, 255, 255);
                    pb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 255, 255) };
                    g.FillPath(pb, gp);
                }
            }
            // ombra interna in basso: dà spessore alla lastra
            RectangleF bot = new RectangleF(r.X, r.Bottom - r.Height * 0.35f, r.Width, r.Height * 0.35f + 1);
            using (LinearGradientBrush lg = new LinearGradientBrush(RectangleF.Inflate(bot, 0, 2), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(T.Dark ? 28 : 12, 0, 0, 0), 90f))
                g.FillRectangle(lg, bot);
            g.Clip = old;
        }
        Rim(g, r, rad, sc, 1f + hover * 0.4f);
    }

    // Bordo di vetro che cattura la luce sui due lati opposti, come una lente.
    public static void Rim(Graphics g, RectangleF r, float rad, float sc, float strength)
    {
        RectangleF e = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        int a1 = (int)Math.Min(255, (T.Dark ? 150 : 255) * strength), a2 = T.Dark ? 18 : 40, a3 = (int)Math.Min(255, (T.Dark ? 70 : 160) * strength);
        using (GraphicsPath p = Round(e, rad))
        using (LinearGradientBrush lg = new LinearGradientBrush(new RectangleF(e.X - 1, e.Y - 1, e.Width + 2, e.Height + 2), Color.White, Color.White, 35f))
        {
            ColorBlend cb = new ColorBlend(4);
            cb.Colors = new Color[] { Color.FromArgb(a1, 255, 255, 255), Color.FromArgb(a2, 255, 255, 255), Color.FromArgb(a2, 255, 255, 255), Color.FromArgb(a3, 255, 255, 255) };
            cb.Positions = new float[] { 0f, 0.38f, 0.66f, 1f };
            lg.InterpolationColors = cb;
            using (Pen pen = new Pen(lg, Math.Max(1f, sc))) g.DrawPath(pen, p);
        }
        if (!T.Dark) DrawRound(g, Color.FromArgb(14, 0, 0, 0), RectangleF.Inflate(e, 0.5f, 0.5f), rad + 0.5f, 1);
    }

    // Bolla di vetro (manopole, lenti): trasparente, con riflesso, bordo luminoso e caustica.
    public static void Bubble(Graphics g, RectangleF c, Color glow, float glowK, float sc)
    {
        if (glowK > 0.01f)
            for (int i = 3; i >= 1; i--)
            {
                RectangleF gr = RectangleF.Inflate(c, i * 3 * sc, i * 3 * sc);
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(gr);
                    using (PathGradientBrush pb = new PathGradientBrush(gp))
                    {
                        pb.CenterColor = Color.FromArgb((int)(70 * glowK / i), glow);
                        pb.SurroundColors = new Color[] { Color.FromArgb(0, glow) };
                        g.FillPath(pb, gp);
                    }
                }
            }
        float rad = Math.Min(c.Width, c.Height) / 2;
        using (SolidBrush b = new SolidBrush(Color.FromArgb(T.Dark ? 40 : 110, 255, 255, 255))) FillRound(g, b, c, rad);
        using (GraphicsPath p = Round(c, rad))
        {
            Region old = g.Clip;
            g.SetClip(p, CombineMode.Intersect);
            RectangleF bot = new RectangleF(c.X, c.Y + c.Height * 0.45f, c.Width, c.Height * 0.55f + 1);
            using (LinearGradientBrush lg = new LinearGradientBrush(RectangleF.Inflate(bot, 0, 2), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(T.Dark ? 45 : 22, 0, 0, 0), 90f))
                g.FillRectangle(lg, bot);
            // riflesso principale (finestra di luce)
            RectangleF hl = new RectangleF(c.X + c.Width * 0.14f, c.Y + c.Height * 0.08f, c.Width * 0.56f, c.Height * 0.38f);
            using (GraphicsPath gp = Round(hl, hl.Height / 2))
            using (PathGradientBrush pb = new PathGradientBrush(gp))
            {
                pb.CenterColor = Color.FromArgb(170, 255, 255, 255);
                pb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 255, 255) };
                pb.CenterPoint = new PointF(hl.X + hl.Width * 0.4f, hl.Y + hl.Height * 0.45f);
                g.FillPath(pb, gp);
            }
            g.Clip = old;
        }
        // caustica: arco luminoso in basso a destra
        using (Pen cp = new Pen(Color.FromArgb(T.Dark ? 120 : 200, 255, 255, 255), Math.Max(1f, sc * 1.1f)))
        {
            cp.StartCap = cp.EndCap = LineCap.Round;
            RectangleF ci = RectangleF.Inflate(c, -sc * 1.6f, -sc * 1.6f);
            if (ci.Width > 2 && ci.Height > 2)
            {
                RectangleF end = new RectangleF(ci.Right - ci.Height, ci.Y, ci.Height, ci.Height);
                g.DrawArc(cp, end, 15, 70);
            }
        }
        Rim(g, c, rad, sc, 1.2f);
    }

    // ── cache: lastre e bolle si disegnano una volta sola per dimensione/stato, poi sono una copia di pixel ──
    static readonly Dictionary<string, Bitmap> glassCache = new Dictionary<string, Bitmap>();

    static Bitmap Cached(string key, int w, int h, int m, Action<Graphics, RectangleF> draw)
    {
        Bitmap b;
        if (glassCache.TryGetValue(key, out b)) return b;
        if (glassCache.Count > 160) ClearGlassCache();
        b = new Bitmap(w + 2 * m, h + 2 * m, PixelFormat.Format32bppPArgb);
        using (Graphics gg = Graphics.FromImage(b))
        {
            Quality(gg);
            draw(gg, new RectangleF(m, m, w, h));
        }
        glassCache[key] = b;
        return b;
    }

    public static void ClearGlassCache()
    {
        foreach (Bitmap b in glassCache.Values) b.Dispose();
        glassCache.Clear();
    }

    static string ThemeKey() { return (T.Dark ? "d" : "l") + T.P.Id + T.P.C[0].ToArgb(); }

    public static void LiquidCached(Graphics g, RectangleF r, float rad, float hover, float sc, bool shadow, Color tint)
    {
        int w = (int)Math.Round(r.Width), h = (int)Math.Round(r.Height);
        if (w < 2 || h < 2) return;
        int hq = (int)Math.Round(hover * 6);
        int m = (int)Math.Ceiling((shadow ? 7 : 2) * sc);
        string key = "L" + w + "x" + h + "r" + (int)rad + "h" + hq + (shadow ? "s" : "") + tint.ToArgb() + ThemeKey();
        Bitmap b = Cached(key, w, h, m, delegate(Graphics gg, RectangleF rr) { Liquid(gg, rr, rad, hq / 6f, sc, shadow, tint); });
        g.DrawImage(b, new Rectangle((int)Math.Round(r.X) - m, (int)Math.Round(r.Y) - m, b.Width, b.Height));
    }

    public static void BubbleCached(Graphics g, RectangleF c, Color glow, float glowK, float sc)
    {
        int w = (int)Math.Round(c.Width), h = (int)Math.Round(c.Height);
        if (w < 2 || h < 2) return;
        int gq = (int)Math.Round(glowK * 8);
        int m = (int)Math.Ceiling(11 * sc);
        string key = "B" + w + "x" + h + "g" + gq + glow.ToArgb() + ThemeKey();
        Bitmap b = Cached(key, w, h, m, delegate(Graphics gg, RectangleF rr) { Bubble(gg, rr, glow, gq / 8f, sc); });
        g.DrawImage(b, new Rectangle((int)Math.Round(c.X) - m, (int)Math.Round(c.Y) - m, b.Width, b.Height));
    }

    public static void GlassCard(Graphics g, RectangleF r, float rad, float hover, Point mouse, float sc)
    {
        Liquid(g, r, rad, hover, sc, true, Color.Transparent);
        if (hover > 0.01f && r.Contains(mouse))
        {
            float R = 150 * sc;
            using (GraphicsPath clip = Round(r, rad))
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(mouse.X - R, mouse.Y - R, 2 * R, 2 * R);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Color.FromArgb((int)((T.Dark ? 26 : 70) * hover), 255, 255, 255);
                    pb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 255, 255) };
                    Region old = g.Clip;
                    g.SetClip(clip, CombineMode.Intersect);
                    g.FillEllipse(pb, mouse.X - R, mouse.Y - R, 2 * R, 2 * R);
                    g.Clip = old;
                }
            }
        }
    }

    public static void Edge(Graphics g, RectangleF r, float rad, float w) { Rim(g, r, rad, w, 1f); }

    // Bagliore del tema dietro al vetro: tre macchie di colore sfumate, disegnate una volta sola e messe in cache.
    public static string AmbientKey(int w, int h) { return w + "x" + h + "|" + T.P.Id + T.P.C[0].ToArgb() + T.Dark; }

    public static Bitmap MakeAmbient(int w, int h) { return MakeAmbient(w, h, 1f); }

    public static Bitmap MakeAmbient(int w, int h, float strength)
    {
        Bitmap ambient;
        int alpha = (int)((T.Dark ? 70 : 60) * strength);
        using (Bitmap tiny = new Bitmap(32, Math.Max(8, 32 * h / Math.Max(1, w)), PixelFormat.Format32bppPArgb))
        {
            using (Graphics tg = Graphics.FromImage(tiny))
            {
                tg.SmoothingMode = SmoothingMode.AntiAlias;
                tg.Clear(Color.Transparent);
                int tw = tiny.Width, th = tiny.Height;
                Blob(tg, tw * 0.05f, th * 0.05f, tw * 0.42f, Color.FromArgb(alpha, T.P.C[0]));
                Blob(tg, tw * 1.0f, th * 0.95f, tw * 0.48f, Color.FromArgb(alpha, T.P.C[1]));
                Blob(tg, tw * 0.95f, th * 0.1f, tw * 0.3f, Color.FromArgb(alpha * 2 / 3, T.P.C[2]));
            }
            ambient = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(ambient))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(tiny, new Rectangle(0, 0, w, h));
            }
        }
        return ambient;
    }

    static void Blob(Graphics g, float cx, float cy, float r, Color c)
    {
        using (GraphicsPath gp = new GraphicsPath())
        {
            gp.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
            using (PathGradientBrush b = new PathGradientBrush(gp))
            {
                b.CenterColor = c;
                b.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                g.FillPath(b, gp);
            }
        }
    }

    public static void Gloss(Graphics g, RectangleF r, float rad, int topAlpha)
    {
        RectangleF top = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.55f);
        using (GraphicsPath p = Round(r, rad))
        using (LinearGradientBrush lg = new LinearGradientBrush(new RectangleF(top.X, top.Y - 2, top.Width, top.Height + 4), Color.FromArgb(topAlpha, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
        {
            Region old = g.Clip;
            g.SetClip(top, CombineMode.Intersect);
            g.FillPath(lg, p);
            g.Clip = old;
        }
    }

    // Il simbolo dell'app: 4 quadratini. t=0 pieni (icone visibili) → t=1 solo contorno (nascoste).
    public static void Glyph(Graphics g, RectangleF a, Brush fill, Color line, float t)
    {
        float gap = a.Width * 0.16f, q = (a.Width - gap) / 2f;
        float lw = Math.Max(1f, a.Width / 13f), rad = q * 0.26f;
        using (Pen pen = new Pen(Color.FromArgb((int)(line.A * (1 - 0.2f * t)), line), lw))
        {
            for (int r = 0; r < 2; r++)
                for (int k = 0; k < 2; k++)
                {
                    RectangleF sq = new RectangleF(a.X + k * (q + gap), a.Y + r * (q + gap), q, q);
                    RectangleF ol = sq; ol.Inflate(-lw / 2f, -lw / 2f);
                    using (GraphicsPath p = Round(ol, rad)) g.DrawPath(pen, p);
                    float ins = t * (q / 2f);
                    RectangleF f = sq; f.Inflate(-ins, -ins);
                    if (f.Width > 0.6f) using (GraphicsPath p = Round(f, rad)) g.FillPath(fill, p);
                }
        }
    }

    public static Icon ToIcon(Bitmap b)
    {
        IntPtr h = b.GetHicon();
        Icon ic = (Icon)Icon.FromHandle(h).Clone();
        N.DestroyIcon(h);
        return ic;
    }

    public static Icon TrayFrame(int n, bool colored, float t)
    {
        using (Bitmap b = new Bitmap(n, n, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float m = n * 0.07f;
            RectangleF a = new RectangleF(m, m, n - 2 * m, n - 2 * m);
            Color mono = T.SysDark ? Color.White : Color.FromArgb(255, 0x1B, 0x1B, 0x1B);
            if (colored)
            {
                using (LinearGradientBrush lg = T.Grad(a, 255, 45f))
                    Glyph(g, a, lg, T.Mix(T.P.C[0], T.P.C[1], 0.5f), t);
            }
            else
            {
                using (SolidBrush sb = new SolidBrush(mono)) Glyph(g, a, sb, mono, t);
            }
            return ToIcon(b);
        }
    }

    // Icona dell'app (exe, finestra, barra delle applicazioni).
    public static Bitmap AppArt(int n)
    {
        Bitmap b = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            RectangleF r = new RectangleF(n * 0.03f, n * 0.03f, n * 0.94f, n * 0.94f);
            Palette p = Palette.All[0];
            using (Bitmap tiny = new Bitmap(12, 12, PixelFormat.Format32bppPArgb))
            {
                MeshInto(tiny, new Palette("x", "x", 0x4F9BFF, 0x7A52F4, 0x2BF0C0, 0xFF3CAC), 2.2f);
                using (Bitmap big = new Bitmap(n, n, PixelFormat.Format32bppPArgb))
                {
                    using (Graphics bg = Graphics.FromImage(big))
                    {
                        bg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        bg.PixelOffsetMode = PixelOffsetMode.Half;
                        bg.DrawImage(tiny, new RectangleF(-n * 0.1f, -n * 0.1f, n * 1.2f, n * 1.2f));
                    }
                    using (GraphicsPath path = Round(r, n * 0.24f))
                    using (TextureBrush tb = new TextureBrush(big, WrapMode.Clamp)) g.FillPath(tb, path);
                }
            }
            Gloss(g, r, n * 0.24f, 70);
            float a = n * 0.27f;
            using (SolidBrush w = new SolidBrush(Color.White))
                Glyph(g, new RectangleF(a, a, n - 2 * a, n - 2 * a), w, Color.White, 0f);
        }
        return b;
    }

    // ICO multi-risoluzione con PNG (solo in fase di build: DesktopPulito.exe /makeicon file.ico)
    public static void WriteIco(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int n in sizes)
            using (Bitmap b = AppArt(n))
            using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int off = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte s = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(s); w.Write(s); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(off);
                off += pngs[i].Length;
            }
            foreach (byte[] p in pngs) w.Write(p);
        }
    }

    // Piccole icone vettoriali per la barra laterale.
    public static void NavIcon(Graphics g, int kind, RectangleF r, Color c, float sc)
    {
        using (Pen p = new Pen(c, 1.5f * sc))
        {
            p.StartCap = p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, u = r.Width / 2;
            switch (kind)
            {
                case 0:   // home: griglia
                    using (SolidBrush b = new SolidBrush(c)) Glyph(g, r, b, c, 0.55f);
                    break;
                case 1:   // aspetto: tavolozza
                    g.DrawEllipse(p, cx - u, cy - u, 2 * u, 2 * u);
                    using (SolidBrush b = new SolidBrush(c))
                    {
                        float d = u * 0.36f;
                        g.FillEllipse(b, cx - u * 0.45f - d / 2, cy - u * 0.25f - d / 2, d, d);
                        g.FillEllipse(b, cx + u * 0.05f - d / 2, cy - u * 0.55f - d / 2, d, d);
                        g.FillEllipse(b, cx + u * 0.45f - d / 2, cy - u * 0.05f - d / 2, d, d);
                    }
                    break;
                case 2:   // comportamento: orologio
                    g.DrawEllipse(p, cx - u, cy - u, 2 * u, 2 * u);
                    g.DrawLines(p, new PointF[] { new PointF(cx, cy - u * 0.55f), new PointF(cx, cy), new PointF(cx + u * 0.4f, cy + u * 0.3f) });
                    break;
                case 3:   // scorciatoie: tastiera
                    {
                        RectangleF k = new RectangleF(r.X, cy - u * 0.65f, r.Width, u * 1.3f);
                        using (GraphicsPath gp = Round(k, u * 0.25f)) g.DrawPath(p, gp);
                        using (SolidBrush b = new SolidBrush(c))
                            for (int i = 0; i < 4; i++) g.FillEllipse(b, k.X + k.Width * (0.2f + i * 0.2f) - sc, k.Y + k.Height * 0.32f - sc, 2 * sc, 2 * sc);
                        g.DrawLine(p, k.X + k.Width * 0.3f, k.Y + k.Height * 0.68f, k.Right - k.Width * 0.3f, k.Y + k.Height * 0.68f);
                    }
                    break;
                case 4:   // sistema: info
                    g.DrawEllipse(p, cx - u, cy - u, 2 * u, 2 * u);
                    g.DrawLine(p, cx, cy - u * 0.05f, cx, cy + u * 0.5f);
                    using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, cx - sc * 1.1f, cy - u * 0.5f - sc * 1.1f, sc * 2.2f, sc * 2.2f);
                    break;
                case 6:   // transizioni: onde concentriche
                    using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, cx - u * 0.22f, cy - u * 0.22f, u * 0.44f, u * 0.44f);
                    g.DrawArc(p, cx - u * 0.62f, cy - u * 0.62f, u * 1.24f, u * 1.24f, 200, 140);
                    g.DrawArc(p, cx - u * 0.62f, cy - u * 0.62f, u * 1.24f, u * 1.24f, 20, 140);
                    g.DrawArc(p, cx - u, cy - u, u * 2, u * 2, 215, 110);
                    g.DrawArc(p, cx - u, cy - u, u * 2, u * 2, 35, 110);
                    break;
                case 5:   // cerca: lente
                    g.DrawEllipse(p, r.X, r.Y, u * 1.35f, u * 1.35f);
                    g.DrawLine(p, r.X + u * 1.2f, r.Y + u * 1.2f, r.Right, r.Bottom);
                    break;
            }
        }
    }

    public static void Check(Graphics g, RectangleF r, Color c, float w)
    {
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, u = Math.Min(r.Width, r.Height) / 2f * 0.8f;
        using (Pen p = new Pen(c, w))
        {
            p.StartCap = p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            g.DrawLines(p, new PointF[] {
                new PointF(cx - u * 0.75f, cy + u * 0.05f),
                new PointF(cx - u * 0.2f,  cy + u * 0.6f),
                new PointF(cx + u * 0.8f,  cy - u * 0.55f) });
        }
    }

    public static void Quality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }
}
