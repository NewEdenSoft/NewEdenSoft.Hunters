# Design tokens

[`themes.json`](themes.json) is the single source of colour for the web and desktop apps. Both read it at runtime and compute the same palette from it.

The visual direction is documented in [docs/design.md](../docs/design.md).

## Structure

| Key | Contents |
|---|---|
| `themes` | Selectable themes. Each sets only `primary`, `secondary` and `tint` (dark background hue). |
| `derived` | Colours computed from the theme inputs. Each rule is `{"mix": [a, percent, b]}`. |
| `fixed` | Colours that never change with the theme: alerts, the activity heat scale, `hunt-glow`, the glow behind systems where a shown hunting profile is likely now, and `jump-range`, the blue behind systems a jump drive can reach from the focused system. |
| `profiles` | Per hunting profile id (as the server names them): a two-letter `code`, the `colour` of its badge on the map, and optionally an `icon` drawn instead of the code (`skull` for smartbomb camps; the icons are in `web/hunters-web/src/map/profileIcons.ts`). A profile without an entry gets the first letters of its name and the glow colour. |
| `security` | Security status colours, index 0 = 0.0 up to index 10 = 1.0, and `null` for negative security. |
| `heat` | Per metric, the lower bounds of the activity heat bands `heat-1` … `heat-5`, per hour: `npcKills`, `shipKills`, `jumps`, and `npcChange` (the rise or fall from the hour before). Below the first bound a system is drawn dim. Set from real ESI hours so the top band is roughly the busiest few percent of systems. |

## Mix rules

`{"mix": [a, p, b]}` means CSS `color-mix(in oklab, a p%, b)`:

- `a` and `b` are a theme input (`primary`, `secondary`, `tint`), a hex colour, or `transparent`.
- Interpolation is in OKLab with premultiplied alpha, so mixing with `transparent` keeps the hue of `a` and sets its alpha to `p%`.
- The web app emits the rules as CSS `color-mix()`. The desktop app implements the same OKLab maths in `NewEdenSoft.Hunters.Desktop.Theming`.

## Adding a theme

Add an entry to `themes` with a unique `id`. No code changes are needed.

## Fonts

| Role | Font | Desktop | Web |
|---|---|---|---|
| Display, labels | Oxanium | `src/Hunters.Desktop/Assets/Fonts` (static 400/600/700) | `@fontsource-variable/oxanium` |
| UI text | Saira | static 400/500/600 | `@fontsource-variable/saira` |
| Numbers | JetBrains Mono | static 400/600 | `@fontsource-variable/jetbrains-mono` |

All three are SIL Open Font License 1.1 with no reserved font names. The desktop files are static instances generated from the Google Fonts variable fonts with fontTools; their licences sit next to them.

## Icon

[`icon.png`](icon.png) (460 px) and [`icon2x.png`](icon2x.png) (920 px) are the application icon. The shipped files are scaled from `icon2x.png`:

| File | Sizes | Used for |
|---|---|---|
| `src/Hunters.Desktop/Assets/hunters.ico` | 16–256 px | Window, tray and the `.exe` (`ApplicationIcon`) |
| `web/hunters-web/public/favicon.ico` | 16, 32, 48 px | Browser tab |
| `web/hunters-web/public/icon-192.png` | 192 px | High-resolution favicon |
| `web/hunters-web/public/apple-touch-icon.png` | 180 px | iOS home screen |

When the icon changes, regenerate all four from `icon2x.png` with high-quality (Lanczos) downscaling.
