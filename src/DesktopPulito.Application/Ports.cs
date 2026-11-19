using DesktopPulito.Domain;

namespace DesktopPulito.Application;

// Porte: ciò di cui i casi d'uso hanno bisogno dal mondo esterno.
// Le implementazioni stanno in Infrastructure; i test usano versioni finte.

/// <summary>Le icone del desktop di Windows.</summary>
public interface IDesktopIcons
{
    bool AreVisible { get; }
    /// <summary>Mostra o nasconde le icone con la transizione indicata (sincrono: ritorna a transizione finita).</summary>
    void SetVisible(bool visible, TransitionSpec transition);
    /// <summary>Cambio immediato, senza effetto (da usare sotto copertura).</summary>
    void SetVisibleNow(bool visible);
}

/// <summary>
/// Cambia il desktop "sotto copertura": mostra lo stato attuale, esegue il cambiamento (sfondo, icone...)
/// senza che si veda, poi passa al nuovo stato con la transizione.
/// </summary>
public interface IDesktopTransitions
{
    void Play(TransitionSpec transition, bool iconsVisibleAfter, Action change);
}

/// <summary>Lo sfondo del desktop.</summary>
public interface IWallpaper
{
    string? Current { get; }
    void Set(string path);
}

/// <summary>Gli elementi presenti sul desktop (file, cartelle, collegamenti).</summary>
public interface IDesktopItemSource
{
    IReadOnlyList<DesktopItem> Load();
}

public interface ISettingsRepository
{
    DesktopSettings Load();
    void Save(DesktopSettings settings);
}

public interface ISceneRepository
{
    SceneLibrary? Load();
    void Save(SceneLibrary library);
}

/// <summary>Meteo (implementazione: Open-Meteo, gratuito e senza chiave).</summary>
public interface IWeatherProvider
{
    Task<GeoLocation?> FindPlaceAsync(string name, string language, CancellationToken ct);
    Task<WeatherSnapshot> GetCurrentAsync(GeoLocation place, CancellationToken ct);
}

/// <summary>Calendario da un link iCal (.ics): Google, Outlook, Apple, Proton... gratuito, senza account sviluppatore.</summary>
public interface ICalendarFeed
{
    Task<IReadOnlyList<AgendaEvent>> LoadAsync(Uri ics, CancellationToken ct);
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
