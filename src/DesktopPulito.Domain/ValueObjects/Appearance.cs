namespace DesktopPulito.Domain;

public enum ThemeMode { System, Light, Dark }
public enum TrayStyle { Mono, Color }
public enum Language { Auto, Italian, English }

public sealed record Appearance(string PaletteId, bool Glass, ThemeMode Theme, TrayStyle Tray, bool Motion, Language Language)
{
    public static Appearance Default { get; } = new("aurora", true, ThemeMode.System, TrayStyle.Mono, true, Language.Auto);
}

