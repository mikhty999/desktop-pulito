using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopPulito.Application;
using DesktopPulito.Domain;
using Microsoft.Win32;

namespace DesktopPulito.Infrastructure.Storage;

public static class AppData
{
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopPulito");
}

/// <summary>
/// Impostazioni in %APPDATA%\DesktopPulito\settings.json (leggibile, facile da copiare o condividere).
/// Al primo avvio migra le impostazioni delle versioni precedenti (registro di Windows).
/// Tiene in memoria l'ultima versione: leggere costa zero, si scrive solo quando qualcosa cambia.
/// </summary>
public sealed class JsonSettingsRepository : ISettingsRepository
{
    readonly string file;
    DesktopSettings? cache;

    public JsonSettingsRepository(string? folder = null) => file = Path.Combine(folder ?? AppData.Folder, "settings.json");

    public DesktopSettings Load()
    {
        if (cache != null) return cache;
        if (File.Exists(file))
        {
            try { cache = SettingsDto.ToDomain(JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.SettingsDto)!); }
            catch { cache = new DesktopSettings(); }
        }
        else
        {
            cache = LegacyRegistry.TryRead() ?? new DesktopSettings();
            Save(cache);
        }
        return cache;
    }

    public void Save(DesktopSettings settings)
    {
        cache = settings;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(SettingsDto.FromDomain(settings), JsonCtx.Default.SettingsDto));
        File.Move(tmp, file, true);   // scrittura atomica: mai un file a metà
    }
}

public sealed class JsonSceneRepository : ISceneRepository
{
    readonly string file;
    public JsonSceneRepository(string? folder = null) => file = Path.Combine(folder ?? AppData.Folder, "scenes.json");

    public SceneLibrary? Load()
    {
        if (!File.Exists(file)) return null;
        try { return ScenesDto.ToDomain(JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.ScenesDto)!); }
        catch { return null; }
    }

    public void Save(SceneLibrary library)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(ScenesDto.FromDomain(library), JsonCtx.Default.ScenesDto));
        File.Move(tmp, file, true);
    }
}

// ───── formato su disco (separato dal dominio: il dominio può evolvere senza rompere i file) ─────

public sealed class SettingsDto
{
    public int Version { get; set; } = 1;
    public bool Transition { get; set; } = true;
    public TransitionStyle TransitionStyle { get; set; }
    public TransitionSpeed TransitionSpeed { get; set; } = TransitionSpeed.Normal;
    public bool AutoHide { get; set; } = true;
    public int AutoHideDelay { get; set; } = 30;
    public bool DoubleClick { get; set; }
    public bool Zen { get; set; }
    public int HotToggle { get; set; }
    public int HotSearch { get; set; }
    public int HotNextScene { get; set; }
    public string Palette { get; set; } = "aurora";
    public bool Glass { get; set; } = true;
    public ThemeMode Theme { get; set; }
    public TrayStyle Tray { get; set; }
    public bool Motion { get; set; } = true;
    public Language Language { get; set; }
    public List<string> Recent { get; set; } = [];
    public int[]? SearchPosition { get; set; }
    public string? WeatherCity { get; set; }
    public string? CalendarIcsUrl { get; set; }

    public static SettingsDto FromDomain(DesktopSettings s) => new()
    {
        Transition = s.Transition.Enabled, TransitionStyle = s.Transition.Style, TransitionSpeed = s.Transition.Speed,
        AutoHide = s.AutoHide.Enabled, AutoHideDelay = s.AutoHide.DelaySeconds,
        DoubleClick = s.DoubleClickToggles, Zen = s.ZenMode,
        HotToggle = s.ToggleHotkey.Packed, HotSearch = s.SearchHotkey.Packed, HotNextScene = s.NextSceneHotkey.Packed,
        Palette = s.Appearance.PaletteId, Glass = s.Appearance.Glass, Theme = s.Appearance.Theme, Tray = s.Appearance.Tray,
        Motion = s.Appearance.Motion, Language = s.Appearance.Language,
        Recent = s.Recent.Items.ToList(),
        SearchPosition = s.SearchPosition is { } p ? [p.X, p.Y] : null,
        WeatherCity = s.WeatherCity, CalendarIcsUrl = s.CalendarIcsUrl,
    };

    public static DesktopSettings ToDomain(SettingsDto d)
    {
        DesktopSettings s = new()
        {
            Transition = new TransitionSpec(d.TransitionStyle, d.TransitionSpeed, d.Transition),
            AutoHide = AutoHidePolicy.Create(d.AutoHide, d.AutoHideDelay),
            DoubleClickToggles = d.DoubleClick,
            ZenMode = d.Zen,
            Appearance = new Appearance(string.IsNullOrEmpty(d.Palette) ? "aurora" : d.Palette, d.Glass, d.Theme, d.Tray, d.Motion, d.Language),
            Recent = new RecentItems(d.Recent),
            SearchPosition = d.SearchPosition is { Length: 2 } p ? (p[0], p[1]) : null,
            WeatherCity = d.WeatherCity,
        };
        // le scorciatoie si impostano solo se valide e senza conflitti; altrimenti restano quelle predefinite
        TrySet(s, HotkeyAction.ToggleIcons, d.HotToggle);
        TrySet(s, HotkeyAction.Search, d.HotSearch);
        TrySet(s, HotkeyAction.NextScene, d.HotNextScene);
        try { s.SetCalendarUrl(d.CalendarIcsUrl); } catch (DomainException) { }
        return s;
    }

    static void TrySet(DesktopSettings s, HotkeyAction a, int packed)
    {
        if (packed < 0) return;
        try { s.SetHotkey(a, Hotkey.FromPacked(packed)); } catch (DomainException) { }
    }
}

public sealed class ScenesDto
{
    public string? Active { get; set; }
    public List<SceneDto> Scenes { get; set; } = [];

    public sealed class SceneDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Wallpaper { get; set; }
        public bool ShowIcons { get; set; }
        public TransitionStyle? EnterStyle { get; set; }
        public TransitionSpeed? EnterSpeed { get; set; }
        public List<WidgetDto> Widgets { get; set; } = [];
    }

    public sealed class WidgetDto
    {
        public WidgetKind Kind { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public static ScenesDto FromDomain(SceneLibrary lib) => new()
    {
        Active = lib.ActiveId.ToString(),
        Scenes = lib.Scenes.Select(s => new SceneDto
        {
            Id = s.Id.ToString(), Name = s.Name, Wallpaper = s.WallpaperPath, ShowIcons = s.ShowIcons,
            EnterStyle = s.EnterWith?.Style, EnterSpeed = s.EnterWith?.Speed,
            Widgets = s.Widgets.Select(w => new WidgetDto { Kind = w.Kind, X = w.X, Y = w.Y }).ToList(),
        }).ToList(),
    };

    public static SceneLibrary ToDomain(ScenesDto d)
    {
        List<Scene> list = [];
        foreach (SceneDto sd in d.Scenes)
        {
            Scene s = new(new SceneId(Guid.TryParse(sd.Id, out Guid g) ? g : Guid.NewGuid()), sd.Name)
            {
                WallpaperPath = sd.Wallpaper, ShowIcons = sd.ShowIcons,
                EnterWith = sd.EnterStyle is { } st ? new TransitionSpec(st, sd.EnterSpeed ?? TransitionSpeed.Normal) : null,
            };
            foreach (WidgetDto w in sd.Widgets) s.PlaceWidget(WidgetPlacement.Create(w.Kind, w.X, w.Y));
            list.Add(s);
        }
        SceneId? active = Guid.TryParse(d.Active, out Guid a) ? new SceneId(a) : null;
        return new SceneLibrary(list, active);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SettingsDto))]
[JsonSerializable(typeof(ScenesDto))]
internal partial class JsonCtx : JsonSerializerContext;

/// <summary>Lettura delle impostazioni salvate dalle versioni 1.x nel registro.</summary>
static class LegacyRegistry
{
    public static DesktopSettings? TryRead()
    {
        using RegistryKey? k = Registry.CurrentUser.OpenSubKey(@"Software\DesktopPulito");
        if (k == null || k.GetValue("Fade") == null) return null;
        int I(string n, int def) => k.GetValue(n) is int v ? v : def;
        SettingsDto d = new()
        {
            Transition = I("Fade", 1) != 0,
            TransitionStyle = (TransitionStyle)Math.Clamp(I("FadeStyle", 0), 0, 6),
            TransitionSpeed = (TransitionSpeed)Math.Clamp(I("FadeSpeed", 1), 0, 2),
            AutoHide = I("AutoHide", 1) != 0,
            AutoHideDelay = I("Delay", 30),
            DoubleClick = I("DoubleClick", 0) != 0,
            Zen = I("Zen", 0) != 0,
            HotToggle = I("HotToggle", new Hotkey(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x44).Packed),
            HotSearch = I("HotSearch", new Hotkey(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x46).Packed),
            HotNextScene = new Hotkey(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x53).Packed,
            Palette = k.GetValue("Palette") as string ?? "aurora",
            Glass = I("Glass", 1) != 0,
            Theme = (ThemeMode)Math.Clamp(I("ThemeMode", 0), 0, 2),
            Tray = (TrayStyle)Math.Clamp(I("TrayStyle", 0), 0, 1),
            Motion = I("Motion", 1) != 0,
            Language = (Language)Math.Clamp(I("Language", 0), 0, 2),
            Recent = (k.GetValue("Recent") as string[])?.ToList() ?? [],
            SearchPosition = I("SearchX", int.MinValue) == int.MinValue ? null : [I("SearchX", 0), I("SearchY", 0)],
        };
        return SettingsDto.ToDomain(d);
    }
}
