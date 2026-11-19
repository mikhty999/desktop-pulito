#nullable disable
// Adattatore di presentazione: espone all'interfaccia le impostazioni del dominio (DesktopSettings)
// come semplici campi, e le riconsegna al repository (JSON) validate dal dominio.
using System;
using System.Collections.Generic;
using DesktopPulito.Domain;
using DesktopPulito.Infrastructure.Windows;

class Config
{
    // transizioni
    public bool Fade = true;
    public int FadeStyle = 0;          // 0 liquida, 1 dissolvenza, 2 cerchio, 3 tendina, 4 gravità, 5 onda, 6 vola via
    public int FadeSpeed = 1;          // 0 veloce, 1 normale, 2 lenta
    // comportamento
    public bool AutoHide = true;
    public int Delay = 30;
    public bool DoubleClick;
    public bool Zen;
    // scorciatoie (formato compatto: modificatori << 16 | tasto)
    public int HotToggle, HotSearch, HotNextScene;
    // aspetto
    public string Palette = "aurora";
    public bool Glass = true;
    public int ThemeMode;              // 0 sistema, 1 chiaro, 2 scuro
    public int TrayStyle;              // 0 monocromo, 1 colore
    public bool Motion = true;
    public int Language;               // 0 automatica, 1 italiano, 2 inglese
    // varie
    public int SearchX = int.MinValue, SearchY = int.MinValue;
    public string WeatherCity, CalendarUrl;
    public List<string> Recent = new List<string>();

    public TransitionSpec Transition => new TransitionSpec((TransitionStyle)FadeStyle, (TransitionSpeed)FadeSpeed, Fade);

    public int FadeMs(bool show) => Transition.DurationMs(show);

    public static Config Load() => From(Services.Settings.Load());

    static Config From(DesktopSettings s) => new Config
    {
        Fade = s.Transition.Enabled, FadeStyle = (int)s.Transition.Style, FadeSpeed = (int)s.Transition.Speed,
        AutoHide = s.AutoHide.Enabled, Delay = s.AutoHide.DelaySeconds,
        DoubleClick = s.DoubleClickToggles, Zen = s.ZenMode,
        HotToggle = s.ToggleHotkey.Packed, HotSearch = s.SearchHotkey.Packed, HotNextScene = s.NextSceneHotkey.Packed,
        Palette = s.Appearance.PaletteId, Glass = s.Appearance.Glass, ThemeMode = (int)s.Appearance.Theme,
        TrayStyle = (int)s.Appearance.Tray, Motion = s.Appearance.Motion, Language = (int)s.Appearance.Language,
        SearchX = s.SearchPosition?.X ?? int.MinValue, SearchY = s.SearchPosition?.Y ?? int.MinValue,
        WeatherCity = s.WeatherCity, CalendarUrl = s.CalendarIcsUrl,
        Recent = new List<string>(s.Recent.Items),
    };

    /// <summary>Riconsegna al dominio (che valida: limiti, scorciatoie in conflitto...) e salva.</summary>
    public void Save()
    {
        DesktopSettings s = Services.Settings.Load();
        s.Transition = Transition;
        s.AutoHide = AutoHidePolicy.Create(AutoHide, Delay);
        Delay = s.AutoHide.DelaySeconds;
        s.DoubleClickToggles = DoubleClick;
        s.ZenMode = Zen;
        TrySet(s, HotkeyAction.ToggleIcons, ref HotToggle);
        TrySet(s, HotkeyAction.Search, ref HotSearch);
        TrySet(s, HotkeyAction.NextScene, ref HotNextScene);
        s.Appearance = new Appearance(Palette, Glass, (ThemeMode)ThemeMode, (TrayStyle)TrayStyle, Motion, (Language)Language);
        s.SearchPosition = SearchX == int.MinValue ? null : (SearchX, SearchY);
        s.WeatherCity = string.IsNullOrWhiteSpace(WeatherCity) ? null : WeatherCity.Trim();
        try { s.SetCalendarUrl(CalendarUrl); } catch (DomainException) { CalendarUrl = s.CalendarIcsUrl; }
        s.Recent = new RecentItems(Recent);
        Services.Settings.Save(s);
    }

    static void TrySet(DesktopSettings s, HotkeyAction a, ref int packed)
    {
        try { s.SetHotkey(a, Hotkey.FromPacked(packed)); }
        catch (DomainException) { packed = s.Get(a).Packed; }   // non valida o in conflitto: resta la precedente
    }

    public void Touch(string path)
    {
        RecentItems r = new RecentItems(Recent);
        r.Touch(path);
        Recent = new List<string>(r.Items);
        Save();
    }

    // Ripristina le impostazioni predefinite mantenendo i "recenti".
    public static Config Defaults(Config old)
    {
        DesktopSettings s = new DesktopSettings { Recent = new RecentItems(old.Recent) };
        Services.Settings.Save(s);
        return From(s);
    }
}

// Lingua: italiano o inglese (automatica dalla lingua di Windows).
static class Lang
{
    public static bool En;
    public static string L(string it, string en) { return En ? en : it; }
    public static void Apply(Config c)
    {
        En = c.Language == 2 || (c.Language == 0 && System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "it");
    }
}
