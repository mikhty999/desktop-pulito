using DesktopPulito.Domain;

namespace DesktopPulito.Application;

/// <summary>Mostra/nasconde le icone con la transizione scelta dall'utente.</summary>
public sealed class ToggleIcons(IDesktopIcons icons, ISettingsRepository settings)
{
    public bool Execute()
    {
        bool show = !icons.AreVisible;
        Set(show);
        return show;
    }

    public void Set(bool visible)
    {
        if (icons.AreVisible == visible) return;
        icons.SetVisible(visible, settings.Load().Transition);
    }
}

/// <summary>
/// Passa a una scena: cambia lo sfondo (se la scena ne ha uno) e porta le icone allo stato della scena,
/// con la transizione della scena o, se manca, quella generale.
/// </summary>
public sealed class SwitchScene(ISceneRepository scenes, IDesktopIcons icons, IWallpaper wallpaper, IDesktopTransitions transitions, ISettingsRepository settings)
{
    public event Action<Scene>? SceneChanged;

    public SceneLibrary Library => library ??= scenes.Load() ?? SceneLibrary.CreateDefault(italian: true);
    SceneLibrary? library;

    public Scene Next() => To(Library.Next().Id);

    public Scene To(SceneId id)
    {
        Scene target = Library.Activate(id);
        TransitionSpec t = target.EnterWith ?? settings.Load().Transition;
        bool newWallpaper = !string.IsNullOrEmpty(target.WallpaperPath) && target.WallpaperPath != wallpaper.Current;
        bool newIcons = icons.AreVisible != target.ShowIcons;
        if (newWallpaper || newIcons)
            transitions.Play(t, target.ShowIcons, () =>
            {
                if (newWallpaper) wallpaper.Set(target.WallpaperPath!);
                if (newIcons) icons.SetVisibleNow(target.ShowIcons);
            });
        scenes.Save(Library);
        SceneChanged?.Invoke(target);
        return target;
    }
}

/// <summary>Meteo con cache: al massimo una richiesta ogni 15 minuti (rete e batteria ringraziano).</summary>
public sealed class GetWeather(IWeatherProvider provider, ISettingsRepository settings, IClock clock)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);
    WeatherSnapshot? cached;
    GeoLocation? place;
    string? placeQuery;

    public async Task<WeatherSnapshot?> ExecuteAsync(string language, CancellationToken ct = default)
    {
        string? city = settings.Load().WeatherCity;
        if (string.IsNullOrWhiteSpace(city)) return null;
        if (place is null || !string.Equals(placeQuery, city, StringComparison.OrdinalIgnoreCase))
        {
            place = await provider.FindPlaceAsync(city, language, ct);
            placeQuery = city;
            cached = null;
            if (place is null) return null;
        }
        if (cached is not null && !cached.IsStale(clock.Now, MaxAge)) return cached;
        cached = await provider.GetCurrentAsync(place.Value, ct);
        return cached;
    }
}

/// <summary>Prossimi impegni dal calendario iCal, con cache di 10 minuti.</summary>
public sealed class GetAgenda(ICalendarFeed feed, ISettingsRepository settings, IClock clock)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    IReadOnlyList<AgendaEvent>? cached;
    DateTimeOffset loadedAt;
    string? loadedUrl;

    public async Task<IReadOnlyList<AgendaEvent>> ExecuteAsync(int max, CancellationToken ct = default)
    {
        string? url = settings.Load().CalendarIcsUrl;
        if (string.IsNullOrEmpty(url)) return [];
        DateTimeOffset now = clock.Now;
        if (cached is null || loadedUrl != url || now - loadedAt > MaxAge)
        {
            cached = await feed.LoadAsync(new Uri(url), ct);
            loadedAt = now;
            loadedUrl = url;
        }
        return Agenda.Upcoming(cached, now, max);
    }
}

/// <summary>Ricerca tra gli elementi del desktop, con i recenti in cima.</summary>
public sealed class SearchDesktop(IDesktopItemSource source, ISettingsRepository settings)
{
    IReadOnlyList<DesktopItem>? items;

    public IReadOnlyList<DesktopItem> Execute(string query)
    {
        items ??= source.Load();
        return DesktopSearch.Rank(items, query, settings.Load().Recent);
    }

    public void Opened(DesktopItem item)
    {
        DesktopSettings s = settings.Load();
        s.Recent.Touch(item.Path);
        settings.Save(s);
    }

    public void Refresh() => items = null;
}
