# Roadmap

Goal: make the whole Windows desktop experience customizable — beautiful (liquid glass), simple, free and open source,
and extremely light on resources.

## ✅ Phase 0 — Foundations (done)
- .NET 10 solution with Clean Architecture: Domain / Application / Infrastructure / App + 37 automated tests
- All 1.x features ported: 7 transitions, liquid-glass UI, desktop search, hotkeys, auto-hide, Zen, IT/EN
- Settings moved to `%APPDATA%\DesktopPulito\settings.json` with automatic import from 1.x
- Domain model for **scenes** and **widgets**; Open-Meteo weather and iCal calendar adapters (tested)
- `Ctrl+Alt+S` switches to the next scene (default scenes: *Work* and *Clean*)
- Single-file 1 MB build, ~0 CPU and ~1 MB working set when idle

## Phase 1 — Scenes & GPU transitions
- Scenes page: create/rename scenes, pick wallpaper, icons on/off, entry transition
- **GPU renderer** (Direct3D 11 + HLSL via [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows), MIT):
  *Burn*, *Pixel dissolve*, *Ink*, *Glitch*, *Shatter*, *Ripple* between any two desktops/themes
- Transitions that work in screen recordings (for sharing videos)

## Phase 2 — Glass widgets
- Clock, Weather (Open-Meteo), Agenda (iCal) pinned to the desktop layer, zero CPU while static
- Per-scene widget layout, drag to place

## Phase 3 — Theme gallery
- Free community gallery hosted on GitHub (JSON index + assets): one click to install a scene/theme
- Export/import themes as a single `.dptheme` file

## Phase 4 — More integrations
- Unread mail counter (privacy-friendly), music now-playing, system stats
- Plugin API for third-party transitions and widgets

## Always
- Performance budget: 0 % CPU idle, < 2 MB working set idle, transitions GPU-composited, start < 400 ms
- Accessibility, English/Italian, no telemetry
