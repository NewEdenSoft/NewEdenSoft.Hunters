# Desktop Client

Avalonia (.NET 10), MVVM with CommunityToolkit.Mvvm. Windows first; see [Platform support](#platform-support).

## Composition

The app runs inside the .NET Generic Host (`DesktopHost`): dependency injection, configuration, logging, and hosted services for background work (ESI polling, log watcher). The host starts before Avalonia and stops after the last window closes.

- Views and view models are registered in DI with `services.AddView<TViewModel, TView>()`.
- `ViewLocator` resolves views from DI by view model type, without reflection.
- `App` has a parameterless constructor for the XAML previewer only; at runtime it receives the service provider.

## Server profiles

- Users add servers by URL. The client reads the [discovery document](discovery-and-admission.md), checks compatibility, and asks the user to confirm trust.
- Several profiles can be saved; one is active at a time initially.
- Profiles store the server address and its name, never a session. See [Sessions are memory only](#sessions-are-memory-only).

## Login

Loopback login with a single-use code. See [Authentication](authentication.md#desktop-login).

### Signed out and signed in (planned)

Every feature requires a signed-in session. See [D19](decisions.md#d19--both-apps-are-behind-login).

#### Sessions are memory only

The desktop keeps its app session in memory and never writes it to disk or the OS credential store, so every start of the app needs a login ([D20](decisions.md#d20--desktop-sessions-are-memory-only)). The EVE login page usually remembers the browser, so this is normally two clicks. When the app quits it revokes its session on the server (best effort), so unused sessions do not pile up.

- **Signed out**: the main window shows only the login screen (server address, access key, log in). No navigation rail, no widgets, no overlays. The tray menu has only *Open Hunters* and *Quit Hunters*.
- **Signed in**: the full app (navigation rail, widget pages, Account, Settings) and the saved overlays are restored.
- **At startup**: the app always opens on the login screen, with the last server's address filled in, even if *Start in the system tray* is on. Saved overlays and *Start in the system tray* apply after login.
- **Session ends** (log out, revoked by the server, or rejected by admission): overlays close, background work stops and the app returns to the login screen.

Until this is built, the Account page is just another page and the rest of the app works without a session.

## ESI calls

The desktop calls ESI directly with NewEdenKit.ESI, using a local file cache. Access tokens come from the server through `ICharacterTokenProvider`; the desktop never holds refresh tokens.

- Polls location, online status and ship for the user's characters, and reports changes to the server where they are shared (fleet).
- Sets in-game waypoints.

ESI's error limit is per IP, so these calls count against the user's own connection rather than the server's.

## Local log watcher

EVE writes a line to the Local chat log when a character changes system ("Channel changed to Local : &lt;system&gt;"). The desktop watches these logs to detect system changes immediately, ahead of ESI polling. The log folder is auto-detected per platform and can be overridden in settings.

## Widgets

Most features of the desktop app are **widgets**. Each widget is either:

- **Docked**: a page in the main window, selected from the navigation rail, or
- **Floating**: an overlay window on top of the game.

The main window is mostly closed. It holds account and character management, settings, and any widgets that are not floating.

```
Main window                                          Overlay (floating widget)
┌──────────────┬───────────────────────────────┐     ┌─[ FLEET ]──────── MOVABLE ┐
│ HUNTERS      │                               │     │                           │
│ WIDGETS      │  Docked: the widget itself    │     │   widget content          │
│ ▸ Nearby     │                               │     │                           │
│ ▸ Fleet  OVL │  Floating: its overlay        │     │                         ⋰ │
│ SYSTEM       │  settings (opacity, lock,     │     └───────────────────────────┘
│ ▸ Account    │  click-through, reset, dock)  │
│ ▸ Settings   │                               │
└──────────────┴───────────────────────────────┘
```

### Per-widget overlay settings

While a widget floats, its page in the main window shows its overlay settings:

| Setting | Effect |
|---|---|
| Background opacity | Opacity of the panel behind the content (20–100%). Text stays fully opaque. |
| Lock in place | The overlay cannot be moved or resized. Its content can still be clicked. |
| Click-through | Mouse input passes through to the game. Implies locked. |
| Reset position | Back to the automatic position and default size. |
| Dock to main window | Closes the overlay; the widget returns to its page. |

Overlays are moved by dragging the header and resized from the bottom-right grip, both only while unlocked. Position, size and all settings are saved per widget in `desktop-settings.json`.

EVE must run windowed or borderless; overlays cannot draw over exclusive fullscreen.

### System tray and lifetime

The app lives in the system tray. Closing the main window hides it; the app exits only from the tray menu.

| Tray menu item | Action |
|---|---|
| Open Hunters | Show the main window (also a left click on the icon) |
| Overlays ▸ *widget* | Pop each widget out as an overlay, or dock it back (checked = floating) |
| Hide all overlays | Temporarily hide every overlay without docking it |
| Quit Hunters | Save layout and exit |

After login the saved overlays are restored, and the main window hides to the tray if **Start in the system tray** is on and at least one overlay is open.

### Implementation

| Piece | Role |
|---|---|
| `services.AddWidget<TViewModel, TView>(id, title, description, width, height)` | Registers a widget. Its view model is a single instance, so state survives moving between page and overlay. |
| `WidgetViewModelBase.IsActive` | True while the widget is on screen (selected page in a visible main window, or a shown overlay). Use it to pause polling. |
| `WidgetState` | Live layout state per widget, bound by the page and the overlay. |
| `WidgetManager` | Floats and docks widgets, owns the overlay windows, restores and saves layout. |
| `OverlayWindow` | Borderless, transparent, topmost, not in the taskbar or Alt+Tab, does not take focus when shown. |
| `IOverlayPlatform` | Click-through and overlay window styles per OS. Windows: `WS_EX_LAYERED \| WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`. |
| `TrayIconService` | The tray icon and menu. |

Widgets so far are placeholders (Nearby activity, Fleet); their content arrives with the features they show.

Widget ideas:

- Nearby activity list
- Fleet members
- Route strip
- Kill ticker

## Map control

Custom Skia-drawn control: pan, zoom, region focus, activity colouring, route highlight on hover, fleet member markers.

## Platform support

Avalonia runs on Windows, macOS and Linux, so the core app (map, login, ESI, fleet, account management) is cross-platform without extra work. The platform-specific parts are behind interfaces (`IOverlayPlatform`, `IGlobalHotkeys`, `IEveLogLocator`) with one implementation per OS.

| Feature | Windows | macOS | Linux (X11) | Linux (Wayland) |
|---|---|---|---|---|
| Transparent, always-on-top widgets | ✅ | ✅ | ✅ needs a compositor | ⚠️ the compositor decides; always-on-top and window positioning are not guaranteed |
| Click-through | ✅ `WS_EX_TRANSPARENT \| WS_EX_LAYERED` | ✅ `NSWindow.ignoresMouseEvents` via native handle | ✅ empty XShape input region | ⚠️ works under XWayland via X11 shape; native Wayland depends on the compositor |
| Over fullscreen EVE | Borderless only | ✅ with `fullScreenAuxiliary` window behaviour | Borderless only | Borderless only |
| Global hotkeys (if added later) | ✅ `RegisterHotKey` | ✅ Carbon hotkeys (no extra permission) | ✅ `XGrabKey` | ⚠️ only via the desktop portal GlobalShortcuts API (KDE, recent GNOME) |
| System tray | ✅ | ✅ menu bar extra | ✅ StatusNotifier / AppIndicator | ✅ StatusNotifier (GNOME needs the AppIndicator extension) |
| Log watcher | `Documents\EVE\logs` | Mac client's log folder | Inside the Proton/Wine prefix | Inside the Proton/Wine prefix |
| Packaging | Installer + auto-update (e.g. Velopack) | Signed + notarized `.app` (needs an Apple developer account) | AppImage / Flatpak | AppImage / Flatpak |

Notes:

- **macOS:** EVE has a native macOS client. Overlays work well; the main cost is code signing and notarization, without which Gatekeeper warns users on first launch.
- **Linux:** EVE runs through Steam Proton, so its log files sit inside the Proton prefix (`steamapps/compatdata/8500/pfx/…`). Avalonia runs through XWayland on most Wayland desktops, so X11 behaviour usually applies, but always-on-top and global hotkeys remain at the compositor's discretion.

Plan: Windows full support first, macOS second, Linux X11 best effort, Wayland documented as limited.
