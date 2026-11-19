using System.Text.Json;
using DesktopPulito.Domain;
using DesktopPulito.Infrastructure.Online;
using DesktopPulito.Infrastructure.Storage;

namespace DesktopPulito.Tests;

public class InfrastructureTests
{
    [Fact]
    public void Ics_parser_reads_events_folding_allday_and_utc()
    {
        string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Riunione con\r\n  il team\\, Ravenna\r\nDTSTART:20261120T090000Z\r\nDTEND:20261120T100000Z\r\nLOCATION:Sala 2\r\nEND:VEVENT\r\n" +
                     "BEGIN:VEVENT\r\nSUMMARY:Ferie\r\nDTSTART;VALUE=DATE:20261224\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        IReadOnlyList<AgendaEvent> ev = IcsCalendarFeed.Parse(ics);
        Assert.Equal(2, ev.Count);
        Assert.Equal("Riunione con il team, Ravenna", ev[0].Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero), ev[0].Start.ToUniversalTime());
        Assert.Equal("Sala 2", ev[0].Location);
        Assert.True(ev[1].AllDay);
        Assert.Equal(TimeSpan.FromDays(1), ev[1].End - ev[1].Start);
    }

    [Fact]
    public void OpenMeteo_responses_are_parsed()
    {
        using JsonDocument geo = JsonDocument.Parse("""{"results":[{"name":"Ravenna","latitude":44.41,"longitude":12.20}]}""");
        GeoLocation? place = OpenMeteoWeather.ParsePlace(geo.RootElement);
        Assert.Equal("Ravenna", place!.Value.Name);

        using JsonDocument cur = JsonDocument.Parse("""{"utc_offset_seconds":3600,"current":{"time":"2026-11-19T18:45","temperature_2m":9.3,"weather_code":61,"is_day":0}}""");
        WeatherSnapshot w = OpenMeteoWeather.ParseCurrent(cur.RootElement, place.Value);
        Assert.Equal(9.3, w.TemperatureC);
        Assert.Equal(WeatherCondition.Rain, w.Condition);
        Assert.False(w.IsDay);
        Assert.Equal(TimeSpan.FromHours(1), w.ObservedAt.Offset);
    }

    [Fact]
    public void OpenMeteo_unknown_city_gives_null()
    {
        using JsonDocument geo = JsonDocument.Parse("""{"generationtime_ms":0.3}""");
        Assert.Null(OpenMeteoWeather.ParsePlace(geo.RootElement));
    }

    [Fact]
    public void Settings_and_scenes_roundtrip_through_json()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dp-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            DesktopSettings s = new()
            {
                Transition = new(TransitionStyle.Gravity, TransitionSpeed.Slow),
                AutoHide = AutoHidePolicy.Create(true, 90),
                WeatherCity = "Ravenna",
                SearchPosition = (2547, 302),
            };
            s.SetHotkey(HotkeyAction.NextScene, new Hotkey(KeyModifiers.Win | KeyModifiers.Shift, 0x53));
            s.Recent.Touch(@"C:\a.lnk");
            new JsonSettingsRepository(dir).Save(s);
            DesktopSettings back = new JsonSettingsRepository(dir).Load();
            Assert.Equal(s.Transition, back.Transition);
            Assert.Equal(90, back.AutoHide.DelaySeconds);
            Assert.Equal(s.NextSceneHotkey, back.NextSceneHotkey);
            Assert.Equal((2547, 302), back.SearchPosition);
            Assert.Equal([@"C:\a.lnk"], back.Recent.Items);

            SceneLibrary lib = SceneLibrary.CreateDefault(italian: false);
            lib.Active.WallpaperPath = @"C:\w.jpg";
            new JsonSceneRepository(dir).Save(lib);
            SceneLibrary lib2 = new JsonSceneRepository(dir).Load()!;
            Assert.Equal(lib.Scenes.Select(x => x.Name), lib2.Scenes.Select(x => x.Name));
            Assert.Equal(@"C:\w.jpg", lib2.Active.WallpaperPath);
            Assert.Equal(lib.Scenes[1].Widgets.Single(), lib2.Scenes[1].Widgets.Single());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Corrupted_settings_file_falls_back_to_defaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ non è json");
            Assert.Equal(TransitionSpec.Default, new JsonSettingsRepository(dir).Load().Transition);
        }
        finally { Directory.Delete(dir, true); }
    }
}
