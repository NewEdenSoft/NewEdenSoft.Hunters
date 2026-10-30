# Desktop To-Do

Features are built in the web client first; the desktop mirrors them afterwards. Each web PR adds what the desktop still needs here, and each desktop PR ticks off or removes what it covers. Behaviour and wording should match the web ([Web Client](web-client.md)); the desktop's own conventions are in [Desktop Client](desktop-client.md).

Already on the desktop: system search, the focused system with its menu, the Map controls widget, activity colours, and hunting profiles (glow, badges, hover). The map model has heights and `MapModel.LyBetween` (map pack format 2) and keeps security in doubles.

## Selection and focus (web #48)
See [Selection and focus](web-client.md#map).
- [x] **Selection apart from focus.** `SelectedSystemId` on the desktop is today the focus. Add a separate selection: a single click on a system selects it, and a click on empty space or **Esc** clears it.
- [x] **Drawing:** the selection as one ring with four ticks (a crosshair) and its name, in the text colour. The focus keeps its double ring in the primary colour. Both are drawn at every zoom, even when the filter hides the system.
- [x] **Search selects** (Enter and a click on a result): the map goes there, and the focus stays as it was. With no search typed, the field still shows the focused system with its clear button.
- [x] **Selected card** under the search and the character chip: **Selected**, name, security, region, a **Focus** button and ✕.
- [x] **Double click focuses** (when the setting is on): two clicks on the same system within 350 ms.
- [x] **Map behaviour settings** on the Settings page, saved with the other desktop settings. Defaults:
  - Double click to focus: on
  - Focus on entering known space: on
  - Focus after a long move: on, 5 jumps (choices 2, 3, 5, 10, 20)
- [x] **Auto-focus:** follow the **followed character**, else the **main**. Port `autoFocusReason` and `jumpsWithin` (a breadth-first search over gates that stops at the minimum) from `map/autoFocus.ts`, with its tests. The first known location never focuses. The map goes to the new focus, and following carries on.
- [x] Focusing by hand moves the map there and stops following, as now; the character chip stays.

## Hunting and Jump range panels
- [x] **Hunting** gets its own widget: the profile toggles and the glow hint move out of Map controls (`HuntingPanel.tsx` on the web).
- [x] **Jump range** gets its own widget, closed by default (`JumpRangePanel.tsx`). The items below go there instead of Map controls.

## Nearby panel
See [Nearby panel](web-client.md#map). It uses the server's `GET /api/intel/reach`.
- [x] A **Nearby** widget from the focused system. Without a focus it says to focus one.
- [x] **Settings:** gate jumps (Off, 3, 5, 10, 15; 5 by default); jump range on or off (the Jump range widget's light years); sort by NPC kills rising, NPC kills (`sort=npc-kills`) or a profile's likely now (a dropdown); show quiet systems (off). Saved with the desktop settings. Port `parseNearby`, `nearbyRequest`, `isActive`, `distanceText` and `inJumpRange` from `map/nearby.ts`, with their tests.
- [x] **Quiet systems hidden:** no kills in the last hour or day and no profile seen. Show a "N quiet hidden" toggle. Ask for 200 rows.
- [x] **Gate jumps filtered locally:** always ask the server by `ly` (the Jump range widget's light years), never by `jumps`; cut the list to the gate jumps chosen on the client (port `withinJumps`), so changing them doesn't ask again. Jump range only adds the light years and the in-range mark. Update `Nearby.Request` and its tests to match.

- [x] **Sort by chance of a target:** a *Chance of a target* option sorted on the client (port `targetChance`, `byTargetChance` and the `npcSort` change, with their tests), asking the server with `sort=npc-kills`; each card shows the chance.

- [x] **Cards** (up to 50, with the active count), each with:
  - name, security and distance (jumps, ly, or both; *no gates*), in jump-range blue when in range
  - NPC kills last hour with ▲ / ▼ and 24 h; PvP (ship and pod kills) last hour and 24 h when any
  - profile badges, bright when likely now, the sorting one outlined
  - quick controls: Dest (every tracked character, outcome on the card) and Focus
- [x] **Clicking a card** selects the system and moves the map there; the quick controls don't.
- [x] **Route on the map:** hovering a card or a system on the map draws the gate route there from the focus, else to the selected system: its gates in the info blue under the systems, as bars moving from the focus towards the destination (a dashed line with a moving offset, 28 px a second; still with reduced motion). Port `gateRoute` from `map/route.ts` with its tests (`MapGeometry` may already have what it needs); redraw only while a route is shown.
- [x] Refreshed every two minutes; keep the old rows, dimmed, while loading.

## Navigate to (web)
See the system menu in [Web Client](web-client.md#map).
- [x] **Navigate to** in the system's menu on the map, a submenu: **All (N)** for every tracked character, then each usable character (online first, then the main, with an online dot) for that one; the outcome shown on the map.
- [x] The same menu (**Focus system**, **Navigate to**) on a right click on a Nearby card, the outcome on the card.
- [x] With a focus, the hover panel shows the gate jumps from the focused system ("5 jumps from X", or *no gates*), and here the light years too.
- Web only: no other context menu on the map or the panels blocks the browser's own menu; the desktop has none to block.

## System panel
See [System panel](web-client.md#map). Needs the server's `GET /api/kills/systems/{id}` (the `kills` feature) and `GET /api/hunting/systems/{id}`.
- [x] A **System** widget showing the selected system, or the focused one while nothing is selected, tagged Selected / Focused.
- [x] **Header:** name, security and region, with zKillboard (`https://zkillboard.com/system/{id}/`) and Dotlan (`https://evemaps.dotlan.net/system/{Name_With_Underscores}`) links.
- [x] **Commands:**
  - Focus / Unfocus
  - Destination as a split button: the main part sets it on every tracked character (showing how many); ▾ lists the usable characters (online first, then the main) to set it for one. ESI `POST /ui/autopilot/waypoint?add_to_beginning=false&clear_other_waypoints=true&destination_id=…` with the character's token; retry once on 401, and ask for a new login on 403.
- [x] **Last hour:** NPC, ship and pod kills and jumps, each with the last hour, the change from the hour before (▲ / ▼ / –), the last 24 hours and the usual, plus Peak now / Peak in N h. Port `metricRows` and `peakHint` from `map/systemInfo.ts`.
- [x] **Hunting profiles:** all seen there, with peak now / now / seen, pilots and last seen, likely now first. Port `profileStatus` and `sortProfiles`.
- [x] **Regulars** under each hunting profile (see the web's System panel): name, **local**, window ("22–02 UTC") with days in it, and **now** in `hunt-reliable` when the window covers this hour; the profile row says **reliable** when its `reliable` curve is set at this hour. Port `windowCovers` and `windowLabel`.
- [x] **Recent kills:** the newest 10, refreshed every minute. Each has the ship icon, ship and pilot, the final blow (pilot and ship, or the NPC) with +N, NPC, time ago and ISK value, and links to the kill on zKillboard. Names come from ESI `/universe/names`. Port `formatIsk`, `timeAgo` and `partyName`.

## Jump range (web #49)
See [Jump range](web-client.md#map).
- [x] **Jump range widget:** a toggle showing the range ("On · 8 ly"). When on, the presets and a custom value:
  - Presets with Jump Drive Calibration V: Black Ops 8 ly; Carrier, Dread and FAX 7; Super and Titan 6; Jump freighter and Rorqual 10. Each preset has a tooltip with its range.
  - Custom: 0.5 to 20 ly.
  - Saved with the desktop settings; off by default, 8 ly.
- [x] **Which systems:** within the range in 3D (`MapModel.LyBetween`) and a cyno target: display security below 0.5, not Pochven (region 10000070), not Zarzakh (30100000). Port `jumpRangeOf` and `isCynoTarget` from `map/jumpRange.ts`, with its tests.
- [x] **Drawing** in `jump-range` (`#3fa9ff`, a fixed token in `design/themes.json`, to add to the desktop theme resources):
  - a soft radial glow under each system in range: reach `max(drawn radius × 3, drawn radius + 8, 9)`, alpha 0.8 at the centre, 0.45 halfway, 0 at the edge, drawn under the hunting glow
  - a 1.5 px ring at the drawn radius + 2, drawn over the systems and under the hover ring, so busy systems with an activity colour still read as in range
- [x] **Hover:** with the range on, the distance from the focus ("14.6 ly from Tama") and **in range**, **out of range** or **no cyno**.
- [x] **Without a focus** nothing is drawn, and the controls say to focus a system.

## Reliable now (web, regulars)
See [Hunting profiles on the map](web-client.md#map). The overview's `reliable` (24 per system) and the system endpoint's `regulars` come from the server (#76, #77).
- [ ] **`hunt-reliable`** (`#5effd0`), a new fixed token in `design/themes.json`, to add to the desktop theme resources.
- [ ] **Reliable ring:** port `reliableLevel` (and `RELIABLE_FULL`) from `map/hunting.ts` and the layer's `reliable` per system (the strongest shown profile's level at this UTC hour). Draw a 2 px ring at the drawn radius + 5, alpha `0.35 + 0.65 × level`, over the systems and outside the jump range's ring, at every zoom.
- [ ] **Hover:** **reliable** in `hunt-reliable` next to a shown profile whose regulars cover this hour.
- [ ] **Hunting widget hint:** add that a ring marks where someone with a routine is usually there at this hour.

## Info page (web)
See the web's `/info` (`routes/_authed/info.tsx`).
- [ ] An **Info** page from the navigation: using the map, the map's marks (glow, reliable ring, badges, jump range, focus and selection rings) and the panels' words (peak now / now / seen, reliable, regulars, local, Peak in N h), how profiles are scored, and each profile's description and evidence from `/api/hunting/profiles` (`HuntingProfileInfo.Description`, `Evidence`). Same wording as the web.
