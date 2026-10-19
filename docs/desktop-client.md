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
- Each profile's app session is stored separately in the OS credential store.

## Login

Loopback login with a single-use code. See [Authentication](authentication.md#desktop-login).

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

At startup the saved overlays are restored. The main window opens unless **Start in the system tray** is on and at least one overlay is open.

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

Avalonia runs on Windows, macOS and Linux, so the core app (map, login, ESI, fleet, account management) is cross-platform without extra work. The platform-specific parts are behind interfaces (`IOverlayPlatform`, `IGlobalHotkeys`, `ICredentialStore`, `IEveLogLocator`) with one implementation per OS.

| Feature | Windows | macOS | Linux (X11) | Linux (Wayland) |
|---|---|---|---|---|
| Transparent, always-on-top widgets | ✅ | ✅ | ✅ needs a compositor | ⚠️ the compositor decides; always-on-top and window positioning are not guaranteed |
| Click-through | ✅ `WS_EX_TRANSPARENT \| WS_EX_LAYERED` | ✅ `NSWindow.ignoresMouseEvents` via native handle | ✅ empty XShape input region | ⚠️ works under XWayland via X11 shape; native Wayland depends on the compositor |
| Over fullscreen EVE | Borderless only | ✅ with `fullScreenAuxiliary` window behaviour | Borderless only | Borderless only |
| Global hotkeys (if added later) | ✅ `RegisterHotKey` | ✅ Carbon hotkeys (no extra permission) | ✅ `XGrabKey` | ⚠️ only via the desktop portal GlobalShortcuts API (KDE, recent GNOME) |
| System tray | ✅ | ✅ menu bar extra | ✅ StatusNotifier / AppIndicator | ✅ StatusNotifier (GNOME needs the AppIndicator extension) |
| Credential storage | Credential Manager / DPAPI | Keychain | Secret Service (libsecret) | Secret Service (libsecret) |
| Log watcher | `Documents\EVE\logs` | Mac client's log folder | Inside the Proton/Wine prefix | Inside the Proton/Wine prefix |
| Packaging | Installer + auto-update (e.g. Velopack) | Signed + notarized `.app` (needs an Apple developer account) | AppImage / Flatpak | AppImage / Flatpak |

Notes:

- **macOS:** EVE has a native macOS client. Overlays work well; the main cost is code signing and notarization, without which Gatekeeper warns users on first launch.
- **Linux:** EVE runs through Steam Proton, so its log files sit inside the Proton prefix (`steamapps/compatdata/8500/pfx/…`). Avalonia runs through XWayland on most Wayland desktops, so X11 behaviour usually applies, but always-on-top and global hotkeys remain at the compositor's discretion.

Plan: Windows full support first, macOS second, Linux X11 best effort, Wayland documented as limited.
