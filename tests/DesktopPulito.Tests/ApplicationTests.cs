using DesktopPulito.Application;
using DesktopPulito.Domain;

namespace DesktopPulito.Tests;

// Finti adattatori: i casi d'uso si provano senza Windows e senza Internet.
sealed class FakeIcons : IDesktopIcons
{
    public bool AreVisible { get; set; } = true;
    public List<(bool visible, TransitionSpec t)> Calls { get; } = [];
    public void SetVisible(bool visible, TransitionSpec t) { Calls.Add((visible, t)); AreVisible = visible; }
    public void SetVisibleNow(bool visible) => AreVisible = visible;
}

// Esegue subito il cambiamento e ricorda con quale transizione (come farebbe lo schermo, senza animazione).
sealed class FakeTransitions : IDesktopTransitions
{
    public List<(TransitionSpec t, bool iconsAfter)> Played { get; } = [];
    public void Play(TransitionSpec t, bool iconsAfter, Action change) { Played.Add((t, iconsAfter)); change(); }
}

sealed class FakeWallpaper : IWallpaper
{
    public string? Current { get; set; }
    public int Sets;
    public void Set(string path) { Current = path; Sets++; }
}

sealed class MemorySettings(DesktopSettings? s = null) : ISettingsRepository
{
    DesktopSettings s = s ?? new DesktopSettings();
    public int Saves;
    public DesktopSettings Load() => s;
    public void Save(DesktopSettings v) { s = v; Saves++; }
}

sealed class MemoryScenes(SceneLibrary? lib = null) : ISceneRepository
{
    public SceneLibrary? Lib = lib;
    public SceneLibrary? Load() => Lib;
    public void Save(SceneLibrary l) => Lib = l;
}

sealed class FakeClock : IClock { public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero); }

sealed class FakeWeather : IWeatherProvider
{
    public int Geocodes, Fetches;
    public Task<GeoLocation?> FindPlaceAsync(string name, string language, CancellationToken ct)
    { Geocodes++; return Task.FromResult<GeoLocation?>(GeoLocation.Create(44.4, 12.2, name)); }
    public Task<WeatherSnapshot> GetCurrentAsync(GeoLocation place, CancellationToken ct)
    { Fetches++; return Task.FromResult(new WeatherSnapshot(place, 18.5, WeatherCondition.Clear, true, DateTimeOffset.Now)); }
}

sealed class FakeFeed(IReadOnlyList<AgendaEvent> events) : ICalendarFeed
{
    public int Loads;
    public Task<IReadOnlyList<AgendaEvent>> LoadAsync(Uri ics, CancellationToken ct) { Loads++; return Task.FromResult(events); }
}

public class ApplicationTests
{
    [Fact]
    public void ToggleIcons_flips_state_with_the_user_transition()
    {
        FakeIcons icons = new();
        DesktopSettings s = new() { Transition = new(TransitionStyle.Wave, TransitionSpeed.Fast) };
        bool nowVisible = new ToggleIcons(icons, new MemorySettings(s)).Execute();
        Assert.False(nowVisible);
        Assert.Equal((false, s.Transition), icons.Calls.Single());
    }

    [Fact]
    public void SwitchScene_applies_wallpaper_icons_and_scene_transition()
    {
        Scene work = new(SceneId.New(), "Lavoro") { ShowIcons = true };
        Scene relax = new(SceneId.New(), "Relax") { ShowIcons = false, WallpaperPath = @"C:\foresta.jpg", EnterWith = new(TransitionStyle.Circle, TransitionSpeed.Slow) };
        MemoryScenes repo = new(new SceneLibrary([work, relax]));
        FakeIcons icons = new(); FakeWallpaper wp = new(); FakeTransitions tr = new();
        SwitchScene uc = new(repo, icons, wp, tr, new MemorySettings());
        Scene? changed = null;
        uc.SceneChanged += s => changed = s;

        Scene now = uc.Next();

        Assert.Equal("Relax", now.Name);
        Assert.Equal(@"C:\foresta.jpg", wp.Current);
        Assert.Equal((relax.EnterWith!, false), tr.Played.Single());       // un solo passaggio copre sfondo + icone
        Assert.False(icons.AreVisible);
        Assert.Same(relax, changed);
        Assert.Equal(relax.Id, repo.Lib!.ActiveId);                // salvata

        uc.Next();                                                 // torna a "Lavoro": niente sfondo da cambiare
        Assert.Equal(1, wp.Sets);
        Assert.True(icons.AreVisible);
        Assert.Equal(2, tr.Played.Count);
        uc.To(work.Id);                                            // già lì: nessuna transizione inutile
        Assert.Equal(2, tr.Played.Count);
    }

    [Fact]
    public void SwitchScene_creates_default_scenes_on_first_use() =>
        Assert.Equal(2, new SwitchScene(new MemoryScenes(), new FakeIcons(), new FakeWallpaper(), new FakeTransitions(), new MemorySettings()).Library.Scenes.Count);

    [Fact]
    public async Task Weather_is_cached_for_15_minutes()
    {
        FakeWeather w = new(); FakeClock clock = new();
        GetWeather uc = new(w, new MemorySettings(new DesktopSettings { WeatherCity = "Ravenna" }), clock);
        await uc.ExecuteAsync("it");
        clock.Now = clock.Now.AddMinutes(5);
        WeatherSnapshot? again = await uc.ExecuteAsync("it");
        Assert.Equal((1, 1), (w.Geocodes, w.Fetches));
        Assert.Equal(18.5, again!.TemperatureC);
    }

    [Fact]
    public async Task Weather_without_a_city_does_nothing()
    {
        FakeWeather w = new();
        Assert.Null(await new GetWeather(w, new MemorySettings(), new FakeClock()).ExecuteAsync("it"));
        Assert.Equal(0, w.Geocodes);
    }

    [Fact]
    public async Task Agenda_reloads_only_after_10_minutes()
    {
        FakeClock clock = new();
        FakeFeed feed = new([new AgendaEvent("Dentista", clock.Now.AddHours(2), clock.Now.AddHours(3), false, "Ravenna")]);
        DesktopSettings s = new(); s.SetCalendarUrl("https://example.org/basic.ics");
        GetAgenda uc = new(feed, new MemorySettings(s), clock);
        Assert.Single(await uc.ExecuteAsync(3));
        clock.Now = clock.Now.AddMinutes(9); await uc.ExecuteAsync(3);
        clock.Now = clock.Now.AddMinutes(2); await uc.ExecuteAsync(3);
        Assert.Equal(2, feed.Loads);
    }
}

public class EditScenesTests
{
    static (EditScenes edit, MemoryScenes repo) Make()
    {
        MemoryScenes repo = new(SceneLibrary.CreateDefault(italian: true));
        SwitchScene sw = new(repo, new FakeIcons(), new FakeWallpaper(), new FakeTransitions(), new MemorySettings());
        return (new EditScenes(sw, repo), repo);
    }

    [Fact]
    public void New_scenes_get_unique_names_and_are_saved()
    {
        var (edit, repo) = Make();
        Assert.Equal("Scena", edit.Create("Scena").Name);
        Assert.Equal("Scena 2", edit.Create("Scena").Name);
        Assert.Equal(4, repo.Lib!.Scenes.Count);
    }

    [Fact]
    public void Rename_rejects_duplicates_case_insensitively()
    {
        var (edit, _) = Make();
        Scene s = edit.Create("Relax");
        Assert.Throws<DomainException>(() => edit.Rename(s.Id, "LAVORO"));
        edit.Rename(s.Id, "Cinema");
        Assert.Equal("Cinema", edit.Library.Find(s.Id).Name);
    }

    [Fact]
    public void Update_changes_wallpaper_icons_and_transition()
    {
        var (edit, repo) = Make();
        Scene s = edit.Create("Notte");
        edit.Update(s.Id, x => { x.WallpaperPath = @"C:\notte.jpg"; x.ShowIcons = true; x.EnterWith = new(TransitionStyle.Burn, TransitionSpeed.Slow); });
        Scene saved = repo.Lib!.Find(s.Id);
        Assert.Equal((@"C:\notte.jpg", true, TransitionStyle.Burn), (saved.WallpaperPath, saved.ShowIcons, saved.EnterWith!.Style));
    }
}
