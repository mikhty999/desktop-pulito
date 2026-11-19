#nullable disable
// Composizione delle dipendenze (Composition Root): l'unico punto che conosce le implementazioni concrete.
// Tutto il resto dell'app parla con i casi d'uso e con le porte.
using DesktopPulito.Application;
using DesktopPulito.Infrastructure.Online;
using DesktopPulito.Infrastructure.Storage;
using DesktopPulito.Infrastructure.Windows;

static class Services
{
    public static readonly ISettingsRepository Settings = new JsonSettingsRepository();
    public static readonly ISceneRepository Scenes = new JsonSceneRepository();
    public static readonly IDesktopIcons Icons = new WindowsDesktopIcons();
    public static readonly IWallpaper Wallpaper = new WindowsWallpaper();
    public static readonly IClock Clock = new SystemClock();

    public static readonly ToggleIcons ToggleIcons = new(Icons, Settings);
    public static readonly IDesktopTransitions Transitions = new WindowsDesktopTransitions();
    public static readonly SwitchScene SwitchScene = new(Scenes, Icons, Wallpaper, Transitions, Settings);
    public static readonly SearchDesktop Search = new(new WindowsDesktopItems(), Settings);
    public static readonly GetWeather Weather = new(new OpenMeteoWeather(), Settings, Clock);
    public static readonly GetAgenda Agenda = new(new IcsCalendarFeed(), Settings, Clock);
}
