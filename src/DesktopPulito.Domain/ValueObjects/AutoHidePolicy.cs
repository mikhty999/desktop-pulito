namespace DesktopPulito.Domain;

/// <summary>Regola dell'auto-nascondi: tempo di inattività tra 5 secondi e 5 minuti.</summary>
public sealed record AutoHidePolicy(bool Enabled, int DelaySeconds)
{
    public const int MinDelay = 5, MaxDelay = 300;
    public static AutoHidePolicy Default { get; } = new(true, 30);

    public static AutoHidePolicy Create(bool enabled, int delaySeconds) =>
        new(enabled, Math.Clamp(delaySeconds, MinDelay, MaxDelay));
}

