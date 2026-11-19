using DesktopPulito.Domain;

namespace DesktopPulito.Tests;

public class DomainTests
{
    [Theory]
    [InlineData(TransitionStyle.Liquid, TransitionSpeed.Normal, false, 560)]
    [InlineData(TransitionStyle.Liquid, TransitionSpeed.Normal, true, 476)]
    [InlineData(TransitionStyle.Fade, TransitionSpeed.Fast, false, 170)]
    [InlineData(TransitionStyle.Wave, TransitionSpeed.Slow, false, 1150)]
    public void Transition_durations_follow_the_table(TransitionStyle s, TransitionSpeed v, bool appearing, int expected) =>
        Assert.Equal(expected, new TransitionSpec(s, v).DurationMs(appearing));

    [Fact]
    public void Gpu_effects_are_flagged_and_timed()
    {
        TransitionSpec burn = new(TransitionStyle.Burn, TransitionSpeed.Normal);
        Assert.True(burn.IsGpu);
        Assert.True(burn.StartsFromPointer);
        Assert.Equal(1100, burn.DurationMs(false));
        Assert.False(new TransitionSpec(TransitionStyle.Liquid, TransitionSpeed.Normal).IsGpu);
        foreach (TransitionStyle s in Enum.GetValues<TransitionStyle>())          // ogni effetto ha i suoi tempi
            Assert.True(new TransitionSpec(s, TransitionSpeed.Fast).DurationMs(false) > 0);
    }

    [Fact]
    public void Disabled_transition_lasts_zero() =>
        Assert.Equal(0, new TransitionSpec(TransitionStyle.Circle, TransitionSpeed.Slow, Enabled: false).DurationMs(false));

    [Fact]
    public void Only_icon_effects_need_icon_positions()
    {
        Assert.True(new TransitionSpec(TransitionStyle.Gravity, TransitionSpeed.Normal).NeedsIconPositions);
        Assert.False(new TransitionSpec(TransitionStyle.Circle, TransitionSpeed.Normal).NeedsIconPositions);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(45, 45)]
    [InlineData(9999, 300)]
    public void AutoHide_delay_is_clamped_between_5s_and_5min(int input, int expected) =>
        Assert.Equal(expected, AutoHidePolicy.Create(true, input).DelaySeconds);

    [Fact]
    public void Hotkey_needs_a_modifier_unless_function_key()
    {
        Assert.False(new Hotkey(KeyModifiers.None, 0x44).IsValid);           // D da solo
        Assert.True(new Hotkey(KeyModifiers.None, 0x70).IsValid);            // F1
        Assert.True(new Hotkey(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x44).IsValid);
    }

    [Fact]
    public void Hotkey_packed_roundtrip()
    {
        Hotkey k = new(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x5A);
        Assert.Equal(k, Hotkey.FromPacked(k.Packed));
    }

    [Fact]
    public void Two_actions_cannot_share_a_hotkey()
    {
        DesktopSettings s = new();
        DomainException e = Assert.Throws<DomainException>(() => s.SetHotkey(HotkeyAction.Search, s.ToggleHotkey));
        Assert.Contains("già usata", e.Message);
    }

    [Fact]
    public void Webcal_links_become_https()
    {
        DesktopSettings s = new();
        s.SetCalendarUrl("webcal://calendar.example.org/x.ics");
        Assert.Equal("https://calendar.example.org/x.ics", s.CalendarIcsUrl);
        Assert.Throws<DomainException>(() => s.SetCalendarUrl("non un link"));
    }

    [Fact]
    public void Recent_items_keep_most_recent_first_without_duplicates()
    {
        RecentItems r = new();
        r.Touch("a"); r.Touch("b"); r.Touch("A");
        Assert.Equal(["A", "b"], r.Items);
        for (int i = 0; i < 30; i++) r.Touch("x" + i);
        Assert.Equal(RecentItems.Capacity, r.Items.Count);
    }

    [Fact]
    public void Scene_library_cycles_and_protects_last_scene()
    {
        SceneLibrary lib = SceneLibrary.CreateDefault(italian: true);
        Scene first = lib.Active;
        Scene next = lib.Next();
        Assert.NotEqual(first.Id, next.Id);
        lib.Activate(next.Id);
        Assert.Equal(first.Id, lib.Next().Id);                 // in ciclo
        lib.Remove(first.Id);
        Assert.Throws<DomainException>(() => lib.Remove(lib.Active.Id));
    }

    [Fact]
    public void Scene_names_are_unique_and_not_empty()
    {
        SceneLibrary lib = SceneLibrary.CreateDefault(italian: true);
        Assert.Throws<DomainException>(() => lib.Add(new Scene(SceneId.New(), "lavoro")));
        Assert.Throws<DomainException>(() => new Scene(SceneId.New(), "   "));
    }

    [Fact]
    public void One_widget_per_kind_per_scene()
    {
        Scene s = new(SceneId.New(), "Relax");
        s.PlaceWidget(WidgetPlacement.Create(WidgetKind.Clock, 0.1, 0.1));
        s.PlaceWidget(WidgetPlacement.Create(WidgetKind.Clock, 2, -1));   // fuori range: viene limitato
        Assert.Single(s.Widgets);
        Assert.Equal((1.0, 0.0), (s.Widgets[0].X, s.Widgets[0].Y));
    }

    [Fact]
    public void Search_ranks_prefix_then_word_then_contains_then_recent()
    {
        DesktopItem[] items =
        [
            new("c:/a.lnk", "Visual Studio Code", DesktopItemKind.Shortcut),
            new("c:/b.lnk", "Spotify", DesktopItemKind.Shortcut),
            new("c:/c", "from spotify to soulseek", DesktopItemKind.Folder),
            new("c:/d.lnk", "SpotiFLAC", DesktopItemKind.Shortcut),
            new("c:/e.txt", "nota", DesktopItemKind.File),
        ];
        RecentItems recent = new(["c:/d.lnk"]);
        IReadOnlyList<DesktopItem> r = DesktopSearch.Rank(items, "spot", recent);
        Assert.Equal(["SpotiFLAC", "Spotify", "from spotify to soulseek"], r.Select(i => i.Name));
    }

    [Fact]
    public void Search_ignores_accents_and_case() =>
        Assert.Equal(4, DesktopSearch.Score(new DesktopItem("p", "Città Ùnica", DesktopItemKind.File), SearchText.Normalize("CITTA")));

    [Theory]
    [InlineData(0, WeatherCondition.Clear)]
    [InlineData(2, WeatherCondition.PartlyCloudy)]
    [InlineData(63, WeatherCondition.Rain)]
    [InlineData(95, WeatherCondition.Thunderstorm)]
    [InlineData(1234, WeatherCondition.Unknown)]
    public void Wmo_codes_map_to_conditions(int code, WeatherCondition expected) =>
        Assert.Equal(expected, WeatherSnapshot.FromWmo(code));

    [Fact]
    public void Agenda_keeps_running_and_future_events_in_order()
    {
        DateTimeOffset now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        AgendaEvent past = new("ieri", now.AddDays(-1), now.AddDays(-1).AddHours(1), false, null);
        AgendaEvent running = new("in corso", now.AddMinutes(-30), now.AddMinutes(30), false, null);
        AgendaEvent later = new("dopo", now.AddHours(3), now.AddHours(4), false, null);
        Assert.Equal(["in corso", "dopo"], Agenda.Upcoming([later, past, running], now, 5).Select(e => e.Title));
    }

    [Fact]
    public void Invalid_coordinates_are_rejected() =>
        Assert.Throws<DomainException>(() => GeoLocation.Create(120, 0, "x"));
}
