# Architecture

Desktop Pulito follows **Clean Architecture** (Robert C. Martin) with a small, pragmatic **DDD** core.
Dependencies always point inward: the domain knows nothing about Windows, the UI or the internet.

```
┌──────────────────────────────────────────────────────────────┐
│ DesktopPulito.App            (presentation + composition root)│
│   glass UI, tray icon, settings, search, Services.cs         │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ DesktopPulito.Infrastructure      (adapters)             │ │
│ │   Windows/  desktop icons, transitions (GPU layers),     │ │
│ │             wallpaper, hotkeys, autostart, Win32 interop │ │
│ │   Storage/  settings.json, scenes.json (+ legacy import) │ │
│ │   Online/   Open-Meteo weather, iCal (.ics) calendar     │ │
│ │ ┌──────────────────────────────────────────────────────┐ │ │
│ │ │ DesktopPulito.Application   (use cases + ports)      │ │ │
│ │ │   ToggleIcons, SwitchScene, SearchDesktop,           │ │ │
│ │ │   GetWeather (15 min cache), GetAgenda (10 min cache)│ │ │
│ │ │ ┌──────────────────────────────────────────────────┐ │ │ │
│ │ │ │ DesktopPulito.Domain   (pure rules, no deps)     │ │ │ │
│ │ │ │   DesktopSettings, TransitionSpec, Hotkey,       │ │ │ │
│ │ │ │   AutoHidePolicy, Scene, SceneLibrary, Widget,   │ │ │ │
│ │ │ │   DesktopSearch, Weather, Agenda                 │ │ │ │
│ │ │ └──────────────────────────────────────────────────┘ │ │ │
│ │ └──────────────────────────────────────────────────────┘ │ │
│ └──────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

## Layers

| Project | Target | Depends on | Contains |
|---|---|---|---|
| `Domain` | `net10.0` | — | Entities, value objects, invariants. Runs anywhere, 100 % unit-testable. |
| `Application` | `net10.0` | Domain | Use cases and **ports** (`IDesktopIcons`, `IWallpaper`, `IWeatherProvider`, `ICalendarFeed`, `ISettingsRepository`, …). |
| `Infrastructure` | `net10.0-windows` | Application | Adapters implementing the ports: Win32, DWM layers, JSON files, free online services. |
| `App` | `net10.0-windows` | Infrastructure | WinForms + hand-drawn liquid-glass UI. `Services.cs` is the only place that wires concrete classes. |
| `Tests` | `net10.0-windows` | all | xUnit: domain rules, use cases with fakes, adapter parsing. |

## Rules of thumb

- **Business rules live in the Domain.** Example: two actions can't share a hotkey; auto-hide delay is clamped to 5 s–5 min;
  a scene library always has at least one scene. The UI never re-implements them.
- **Use cases talk to ports, never to Windows.** That's why `SwitchScene` is tested with a fake wallpaper and fake icons.
- **Adapters are swappable.** Weather today is Open-Meteo; another provider is one class implementing `IWeatherProvider`.
- **Performance is a feature.** Nothing polls while idle; network results are cached; transitions and glass are composed by the GPU;
  heavy work (capture, blur) runs in parallel and only on demand.

## Free & open services

| Need | Service | Why |
|---|---|---|
| Weather + city search | [Open-Meteo](https://open-meteo.com) | Free, open source, no API key, no account |
| Calendar | Any **iCal (.ics) link** — Google ("secret address in iCal format"), Outlook, Apple, Proton, Nextcloud | No developer account, no OAuth verification |
| Runtime | .NET 10 (MIT) | Fast, modern, long-term support |

Gmail is planned later: its read scope is classified *restricted* by Google (security assessment required for public apps),
so we'll start with a privacy-friendly unread counter that needs no restricted scope.

## Adding a feature (example: a new transition)

1. Add the value to `TransitionStyle` and its timings in `TransitionSpec` (Domain) + a unit test.
2. Implement the effect in `Infrastructure/Windows/Transitions.cs` (or the upcoming GPU renderer).
3. Add its name/description to the Transitions page (App). No other layer changes.
