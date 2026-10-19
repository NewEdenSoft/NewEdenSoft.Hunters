# Desktop Client

Avalonia (.NET 10), MVVM with CommunityToolkit.Mvvm. Windows first; see [Platform support](#platform-support).

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

## Overlays

- Each widget is its own borderless window: transparent, always on top, adjustable opacity.
- **Click-through is toggled per widget** from a central settings screen. A click-through widget passes all mouse input to the EVE client underneath; an interactive widget can be clicked and dragged.
- A global hotkey can temporarily make all widgets interactive (to move or adjust them), then return them to their configured state.
- Widget position, size, opacity and click-through state are saved per widget.
- EVE must run windowed or borderless; overlays cannot draw over exclusive fullscreen.

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
| Global hotkeys | ✅ `RegisterHotKey` | ✅ Carbon hotkeys (no extra permission) | ✅ `XGrabKey` | ⚠️ only via the desktop portal GlobalShortcuts API (KDE, recent GNOME) |
| Credential storage | Credential Manager / DPAPI | Keychain | Secret Service (libsecret) | Secret Service (libsecret) |
| Log watcher | `Documents\EVE\logs` | Mac client's log folder | Inside the Proton/Wine prefix | Inside the Proton/Wine prefix |
| Packaging | Installer + auto-update (e.g. Velopack) | Signed + notarized `.app` (needs an Apple developer account) | AppImage / Flatpak | AppImage / Flatpak |

Notes:

- **macOS:** EVE has a native macOS client. Overlays work well; the main cost is code signing and notarization, without which Gatekeeper warns users on first launch.
- **Linux:** EVE runs through Steam Proton, so its log files sit inside the Proton prefix (`steamapps/compatdata/8500/pfx/…`). Avalonia runs through XWayland on most Wayland desktops, so X11 behaviour usually applies, but always-on-top and global hotkeys remain at the compositor's discretion.

Plan: Windows full support first, macOS second, Linux X11 best effort, Wayland documented as limited.
