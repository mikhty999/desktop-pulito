namespace DesktopPulito.Domain;

/// <summary>Effetti di transizione tra due stati del desktop.</summary>
public enum TransitionStyle { Liquid, Fade, Circle, Wipe, Gravity, Wave, Float, Burn, Pixelate, Glitch, Ripple, Ink }

public enum TransitionSpeed { Fast, Normal, Slow }

/// <summary>Una transizione: quale effetto, a che velocità, e quanto dura.</summary>
public sealed record TransitionSpec(TransitionStyle Style, TransitionSpeed Speed, bool Enabled = true)
{
    static readonly int[][] Table =
    [
        [340, 560, 860],    // Liquid
        [170, 300, 520],    // Fade
        [320, 520, 800],    // Circle
        [300, 480, 740],    // Wipe
        [360, 560, 860],    // Gravity
        [520, 780, 1150],   // Wave
        [360, 560, 860],    // Float
        [700, 1100, 1600],  // Burn
        [420, 650, 950],    // Pixelate
        [380, 600, 900],    // Glitch
        [550, 850, 1250],   // Ripple
        [650, 1000, 1450],  // Ink
    ];

    public static TransitionSpec Default { get; } = new(TransitionStyle.Liquid, TransitionSpeed.Normal);

    /// <summary>Durata in millisecondi. Quando le icone riappaiono la transizione è un po' più rapida.</summary>
    public int DurationMs(bool appearing)
    {
        if (!Enabled) return 0;
        int v = Table[(int)Style][(int)Speed];
        return appearing ? v * 85 / 100 : v;
    }

    /// <summary>Questi effetti muovono le sagome delle icone: richiedono le loro posizioni.</summary>
    public bool NeedsIconPositions => Style is TransitionStyle.Gravity or TransitionStyle.Wave or TransitionStyle.Float;

    /// <summary>Effetti calcolati pixel per pixel dalla scheda video (shader).</summary>
    public bool IsGpu => Style >= TransitionStyle.Burn;

    /// <summary>Gli effetti GPU partono dal puntatore del mouse (bruciatura, increspatura, inchiostro).</summary>
    public bool StartsFromPointer => Style is TransitionStyle.Burn or TransitionStyle.Ripple or TransitionStyle.Ink or TransitionStyle.Circle;
}

