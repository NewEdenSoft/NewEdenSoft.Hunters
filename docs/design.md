# Design

Hunters looks like a futuristic sci-fi HUD: a tactical tool that sits on top of the EVE client. Users pick a colour theme based on the EVE client's race, faction and corporation colour schemes.

Interactive reference: [Hunters HUD Style Guide](https://claude.ai/artifact/3uqxWYF68JsraDdLRmqKMP) (theme switcher, controls, map, overlay widgets).

## Principles

| Principle | Meaning |
|---|---|
| Readable over EVE | Translucent dark panels, text shadows on overlays, high-contrast numbers. Dark only. |
| Thin lines, cut corners | 1px rules, corner brackets on panels, chamfered corners on buttons and chips. No heavy chrome. |
| Glow means live | Only active, selected or live elements glow. Everything else stays flat. |
| Numbers in mono | Jumps, kills, security status and times use tabular monospace digits. |

## Colour

All colour comes from [`design/themes.json`](../design/themes.json), shared by both apps. See [design/README.md](../design/README.md) for the format.

- **Themes** set only three values: `primary`, `secondary` and a background `tint`. Every other theme colour (backgrounds, surfaces, lines, glow, text levels) is derived from them with OKLab mixing rules stored in the same file.
- **Fixed colours** never change with the theme:
  - Security status: the in-game scale from 1.0 (cyan) to 0.0 (red), and a deep red for null-sec.
  - Activity heat: violet → magenta → white, kept clear of security hues. On the map, activity is also shown as ring size so it reads without colour.
  - Alerts: danger, warning, ok, info.
- Faction colours are first approximations, to be tuned against the EVE client.

Initial themes: Carbon (neutral default); Amarr, Caldari, Gallente, Minmatar; Guristas, Serpentis, Angel Cartel, Blood Raiders, Sansha; ORE, CONCORD, Sisters of EVE, Triglavian, EDENCOM.

## Typography

| Role | Font | Use |
|---|---|---|
| Display | Oxanium | Headings, labels (uppercase, letter-spaced), buttons |
| Body | Saira | UI text |
| Mono | JetBrains Mono | Numbers, security status, timers |

All are bundled with the apps (no external font requests), under the SIL Open Font License.

## Components

| Component | Desktop | Web |
|---|---|---|
| Panel with corner brackets | `HudPanel` | `.panel` |
| Chamfered button | `Button.hud` (+ `primary`, `ghost`, `danger`), drawn by `ChamferBorder` | `.btn` (+ `primary`, `ghost`, `danger`) |
| Theme swatch | `Button.swatch` | `.swatch` |
| Section label | `TextBlock.label` | `.label` |

## Implementation

| | Desktop (Avalonia) | Web (React) |
|---|---|---|
| Theme source | `themes.json` embedded as a resource | `themes.json` imported at build time |
| Derived colours | `ThemePalette` computes OKLab mixes in C# | `installTokens()` emits CSS `color-mix(in oklab, …)` |
| Applying a theme | `ThemeService.Apply` writes `Hud.{Name}Color` / `Hud.{Name}Brush` resources; views use `DynamicResource` | `applyTheme` sets `--primary`, `--secondary`, `--tint` |
| Fluent controls | `SystemAccentColor*` follow the theme | — |
| Saved choice | `desktop-settings.json` in local app data | `localStorage` |
