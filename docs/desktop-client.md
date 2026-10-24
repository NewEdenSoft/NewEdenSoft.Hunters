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

### Account page

Once logged in, the Account page shows the server and the account's characters (main first, with portraits from EVE's public [image server](https://developers.eveonline.com/docs/services/image-server/), kept in memory while the app runs), with:

| Action | Notes |
|---|---|
| Add character | Browser login with the session attached; the character joins this account. |
| Log in again | Shown on characters that need re-auth (see below). Same browser login; the user picks that character at EVE. |
| Make main | Needed before the main can be removed while other characters remain. |
| Remove | Asks for confirmation. The server revokes the character's token at EVE. |
| Delete account | Asks for confirmation. Deletes the account, characters and sessions on the server and logs out. |
| Log out | Revokes this session on the server. |

**Map markers.** Each character has a marker that will show it on the map: a letter (A to Z), a number (1 to 9) or an icon (target, star, diamond, triangle, bolt, shield, eye, heart). Clicking the badge at the start of a character's row opens a picker below it. The default is the first letter of the character's name. Markers are saved in this computer's desktop settings by character id (`Characters.{id}.Marker`) and are not shared with the server or other devices. `CharacterPreferences` reads and saves them and raises `MarkerChanged`; `MarkerBadge` draws one. Icons are SVG path data in a 24 x 24 box (`MarkerIcons`), so Avalonia and the map's Skia renderer draw the same shapes.

**Needs re-auth.** Access tokens expire after about 20 minutes and the server refreshes them on request, so the user never sees that. A refresh token stops working when the user revokes the app at EVE, a password changes, or EVE expires it; the server only finds out when it next refreshes. The desktop does not poll for this. A character is flagged when:

- the account snapshot from the server says `NeedsReauth` or `MissingScopes` (on login, refresh, or after any account change), or
- `ICharacterTokenProvider` asks the server for an access token and gets `needs_reauth`. ESI code that gets a rejected token calls `InvalidateAccessToken` and asks again, which leads to the same check.

Flagged characters show a warning icon and a *Log in again* button, and the Account entry in the navigation rail shows `LOG IN`.

## ESI calls

The desktop calls ESI directly with NewEdenKit.ESI, using a local file cache. Access tokens come from the server through `ICharacterTokenProvider`; the desktop never holds refresh tokens.

- Polls location, online status and ship for the user's characters (see [Character tracking](#character-tracking)), and reports changes to the server where they are shared (fleet).
- Sets in-game waypoints.

### Character tracking

`CharacterTracker` (`Hunters.Client/Tracking`) polls ESI for each character it is told to track and keeps the latest state of each: online, location (system, and station or structure when docked), ship, and a status (`Starting`, `Tracking`, `NeedsLogin`, `Failing`). One tracker serves the whole app. Views read the current state from it and subscribe to `Changed`, which is raised only when something other than the timestamp changes. Later, the location reporter for fleets will subscribe the same way.

| Character | Polled | Interval |
|---|---|---|
| Online | Online status, location, ship | Location and ship every 5 s (ESI's cache time); online every 60 s |
| Offline | Online status only; location and ship once at the start, so the last known place is shown | 60 s (ESI's cache time for online) |

- A token ESI rejects (401) is dropped, so the next attempt asks the server for a new one.
- Failures back off: 5, 10, 20, 40 and 80 s, then every 2 minutes. The last good state is kept, with the error.
- When the server can no longer refresh a character's token, the session flags the character and the tracker stops polling it, with status `NeedsLogin`. Calling `Track` again after the character logs in restarts it.
- `Track(ids)` sets exactly which characters are tracked; characters no longer in the list are stopped and removed, with a `Changed` event whose `Current` is null.
- The ESI calls go through `ILocationEsi`, implemented with NewEdenKit.ESI's `ILocation`, so the tracker is tested with a fake ESI and a fake clock.

In the desktop, `CharacterTracking` owns the one tracker for the signed-in session. It tracks the account's characters that are usable (no re-auth or missing permissions) and not switched off, and follows account changes and the switches. A new session replaces the tracker; logging out stops it. Each character row on the Account page has a **Track** switch (on by default, saved for this computer in `Characters.{id}.IsTracked`) and a line such as *Online · Jita · Docked at Jita IV - Moon 4 - Caldari Navy Assembly Plant · Ishtar “Hunter One”*, with a green dot while online. While polling fails, the line turns amber, with the error in its tooltip. Names of systems, stations and ship types come from ESI's public `POST /universe/names` and are cached for the app's lifetime (`EveNames`). Player structures are not public, so those show as *Docked*.

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
| `services.AddWidget<TViewModel, TView>(id, title, description, width, height, fillsPage)` | Registers a widget. Its view model is a single instance, so state survives moving between page and overlay. With `fillsPage`, the docked widget fills the height of its page instead of scrolling (the map). |
| `WidgetViewModelBase.IsActive` | True while the widget is on screen (selected page in a visible main window, or a shown overlay). Use it to pause polling. |
| `WidgetState` | Live layout state per widget, bound by the page and the overlay. |
| `WidgetManager` | Floats and docks widgets, owns the overlay windows, restores and saves layout. |
| `OverlayWindow` | Borderless, transparent, topmost, not in the taskbar or Alt+Tab, does not take focus when shown. |
| `IOverlayPlatform` | Click-through and overlay window styles per OS. Windows: `WS_EX_LAYERED \| WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`. |
| `TrayIconService` | The tray icon and menu. |

Widgets so far: the [Map](#map) and its [Map controls](#map-controls), [Characters](#characters-widget), and placeholders for Nearby activity and Fleet, whose content arrives with the features they show.

Widget ideas:

- Nearby activity list
- Fleet members
- Route strip
- Kill ticker

## Characters widget

The account's characters at a glance, small enough for an overlay: each row has the character's [marker](#account-page), portrait, an online dot, name, system (with *Docked* when docked) and ship, live from the [tracker](#character-tracking). Characters that are not tracked say so, and characters that need a new login are flagged in amber. The order is main first, then by name, and stays put as characters log in and out. Clicking a row shows the character on the map; the target button at the end of the row follows it ([focus and follow](#map)). The tooltip has the full station name and the last tracking error. The Account page and the widget describe a character with the same `CharacterWhereabouts`.

## Map

The **Map** widget draws known space from the server's map pack, like the [web map](web-client.md#map): the same layouts, sizes, gate styles, colours, labels and colour filters, drawn with SkiaSharp ([D21](decisions.md#d21--the-map-is-2d-drawn-with-canvas-2d-web-and-skia-desktop)). Drag to pan, scroll to zoom; hovering a system shows its name, security and region, and, when the server has activity, its NPC kills in the last hour with the change from the hour before (▲ / ▼), the usual for this hour of the week, and *Peak now* or *Peak in N h*. Docked, it fills its page; popped out, it is an overlay like any widget. The map has no background of its own, so it shows over the overlay's background at the user's opacity. The layout, colour and legend are in a widget of their own, [Map controls](#map-controls), so the map keeps its space.

| Piece | Role |
|---|---|
| `Map/MapModel.cs` | The map pack as flat arrays per system index; positions per layout; region label centres. |
| `Map/SecurityStatus.cs` | Security as EVE shows it, shared with `ThemeCatalog.SecurityColor`. |
| `Map/MapFilter.cs` | Colour filters: a colour group per system, or `Hidden`, with an optional size per group and a notice when there is nothing to show. Security, NPC kills, NPC rising and falling, ship kills and jumps, like the web's. |
| `Map/GridIndex.cs` | Uniform grid for the nearest system to the cursor. |
| `Map/HuntingLayer.cs` | The shown hunting profiles' glow and badges, and the profiles per system for the hover panel ("likely now"), like the web's `hunting.ts`. |
| `Map/ProfileIcons.cs` | Icons a profile's badge shows instead of its code (`skull` for smartbomb camps). |
| `Controls/ProfileMark.cs` | A profile's icon or code in its colour, for Map controls and the hover panel. |
| `Map/MapScene.cs` | Everything for one layout, filter and theme, built once: gates by style and colour, gradients, label widths, region names. Draws a frame on any `SKCanvas`; gates go on a layer of their own, so the margins around systems are cut out of the gates only. |
| `Map/MapControl.cs` | The Avalonia control: camera, pan, zoom, hover, and right clicks on systems. It draws the scene with a custom draw operation on Avalonia's own Skia canvas, so no bitmap is copied. |
| `Map/SystemSearch.cs` | Finds systems by name, ranked like the web's search. |
| `Widgets/Map/MapWidgetViewModel.cs` | Loads the map pack while the widget is on screen, then keeps the activity current; the hover panel's system; the focused system. |
| `Widgets/Map/SystemSearchViewModel.cs` | The search box's query, results and keyboard highlight. |
| `Widgets/Map/MapControlsWidgetViewModel.cs` | The [Map controls](#map-controls) widget. |
| `Map/MapPreferences.cs` | The layout setting, saved in the desktop settings (`Map.Layout`) and chosen in Map controls or on the Settings page. CCP's 2D map is the default. |

`MapScene` is immutable: the UI thread builds a new one when the model, layout, filter or theme colours change, and frames are drawn on the render thread from the scene and camera captured with them. Theme colours reach the control as `DynamicResource` bindings, so a theme change rebuilds the scene.

**Character markers.** Each tracked character with a known location is drawn with its [marker](#account-page) at its system: a short leader line from the system's edge up and to the right, ending at a row of badges, one per character in that system (main first). Offline characters stay at their last known system, dimmed. Markers are drawn at every zoom, over everything else; a row that would overlap one already drawn (neighbouring systems, zoomed out) moves further up along the same diagonal. Hovering a system also lists the account's characters in it. Markers are frame data (`MapControl.Markers`), not part of the scene, so position updates only redraw.

**Focus and follow.** Clicking a character in the [Characters widget](#characters-widget), or its marker on the map, focuses the map on it: the map pans and zooms (to at least 60 px/ly, where system names show) to the character's system in a short animation. A docked map's page is selected first; a character whose location is not known yet is focused as soon as it is. A focused character gets a chip at the top right of the map with its marker, name, **Follow** and dismiss. The follow button on each row of the Characters widget does the same. While following, the map pans to the character each time it changes system and keeps the user's zoom. Dragging the map stops following; focusing another character does too. `MapFocus` holds the focused and followed character for the widget, the map and the main window; `MapControl.FocusOn` animates the camera, and marker clicks use the same layout as drawing (`MapScene.HitTestMarker`).

**Search and the focused system.** As on the [web map](web-client.md#map), a system can be **focused**: it is the origin for nearby intel, and the map rings it twice in the theme's primary colour and always names it, even zoomed out. The search box at the top left finds systems by name (case and punctuation ignored, so `1dq1a` finds 1DQ1-A): exact names first, then names starting with the query, then names containing it. **/** puts the cursor in it; ↑ and ↓ move through the results, **Enter** or a click selects one (see below) and moves the map there, and the focus stays as it was; **Esc** clears the search. Right-clicking a system opens its menu: **Focus** it, or **Clear focus** on the focused one. Focusing moves the map there (zooming in to names) and stops following a character, as dragging does; the character's chip stays. While nothing is typed, the search box shows the focused system, with a button to clear it. It is kept for the session, not saved. Apart from the focus, a single click on a system (or the search) **selects** it, and a click on empty space or **Esc** clears the selection. The selection is drawn as one ring with four ticks (a crosshair) and its name, in the text colour; like the focus, it is drawn at every zoom, even when the colour filter hides the system. The search hides when the map is smaller than 300 × 220, as in a small overlay.

**Activity.** Once the map is loaded, and if the server's discovery document lists the `activity` feature, the widget asks for `/api/activity/current` every minute while it is on screen. It sends the last answer's ETag (`HuntersSession.GetActivityAsync`), so the server answers 304 until there is a new hour. A server without activity is asked again every five minutes. The activity colours use the same heat bands as the web (`heat` in `design/themes.json`, `heat-1` to `heat-5`); systems below the first band are drawn small in the theme's line colour, and busier bands larger, up to twice the size. NPC rising and falling compare with the hour before, from a minimum of NPC kills in the last hour (any, 25, 100 or 250).

**Hunting profiles.** As on the [web map](web-client.md#map), hunting profiles turned on in [Map controls](#map-controls) are drawn over the colours rather than as a colour of their own:
- **Glow:** a glow in `hunt-glow` behind each system where a shown profile is *likely now*. That is the profile's score there times this hour's share (UTC) of its sightings there, relative to the profile's strongest system, so a rare target still lights up where it is most likely.
- **Badges:** zoomed in to 50 px/ly, a row of badges below each system's name, one per shown profile seen there. Each has the profile's code or icon in its colour (`profiles` in `design/themes.json`), faint when the profile is not usually there at this hour. Names keep clear of the badges.
- **Hover:** the shown profiles seen in the system, likely now first, each marked *peak now*, *now* or *seen*.

The profiles are fetched once the server lists the `hunting` feature. Where they are seen (`/api/hunting/overview`, revalidated with its ETag) is fetched only while some are shown: at once when one is turned on, then every five minutes while the map is on screen. The shown profiles are saved in the desktop settings (`Map.HuntingProfiles`).

The map pack is downloaded the first time the widget is on screen in a session and kept in memory for the session. While the server is still preparing it, the page asks again every 10 seconds. A disk cache by SDE build comes later.

Next for the map: the Nearby panel, route highlight on hover, fleet member markers.

## Map controls

How the [map](#map) is drawn: its **layout** (CCP's 2D map or top-down), its **colour** filter (the activity ones only when the server has activity), the **minimum NPC kills** for NPC rising and falling, the filter's legend (or why it shows nothing yet), and which **hunting** profiles are shown. It is a widget of its own, so the map stays clear: dock it as a page, or pop it out as an overlay next to the map. Its state is the map's (`MapWidgetViewModel.FilterId` and `MinNpc`, `MapPreferences.Layout` and `HuntingProfiles`), so both stay in step wherever they are shown.

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
