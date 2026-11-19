namespace DesktopPulito.Domain;

public enum WidgetKind { Clock, Weather, Agenda }

/// <summary>Posizione di un widget in percentuale dello schermo principale (indipendente dalla risoluzione).</summary>
public sealed record WidgetPlacement(WidgetKind Kind, double X, double Y)
{
    public static WidgetPlacement Create(WidgetKind kind, double x, double y) =>
        new(kind, Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
}

