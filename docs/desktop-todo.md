# Desktop To-Do

Features are built in the web client first; the desktop mirrors them afterwards. Each web PR adds what the desktop still needs here, and each desktop PR ticks off or removes what it covers. Behaviour and wording should match the web ([Web Client](web-client.md)); the desktop's own conventions are in [Desktop Client](desktop-client.md).

Already on the desktop: system search, the focused system with its menu, the Map controls widget, activity colours, and hunting profiles (glow, badges, hover). The map model has heights and `MapModel.LyBetween` (map pack format 2) and keeps security in doubles.

## Selection and focus (web #48)
See [Selection and focus](web-client.md#map).
- [ ] **Selection apart from focus.** `SelectedSystemId` on the desktop is today the focus. Add a separate selection: a single click on a system selects it, and a click on empty space or **Esc** clears it.
- [ ] **Drawing:** the selection as one ring with four ticks (a crosshair) and its name, in the text colour. The focus keeps its double ring in the primary colour. Both are drawn at every zoom, even when the filter hides the system.
- [ ] **Search selects** (Enter and a click on a result): the map goes there, and the focus stays as it was. With no search typed, the field still shows the focused system with its clear button.
- [ ] **Selected card** under the search and the character chip: **Selected**, name, security, region, a **Focus** button and ✕.
- [ ] **Double click focuses** (when the setting is on): two clicks on the same system within 350 ms.
- [ ] **Map behaviour settings** on the Settings page, saved with the other desktop settings. Defaults:
  - Double click to focus: on
  - Focus on entering known space: on
  - Focus after a long move: on, 5 jumps (choices 2, 3, 5, 10, 20)
- [ ] **Auto-focus:** follow the **followed character**, else the **main**. Port `autoFocusReason` and `jumpsWithin` (a breadth-first search over gates that stops at the minimum) from `map/autoFocus.ts`, with its tests. The first known location never focuses. The map goes to the new focus, and following carries on.
- [ ] Focusing by hand moves the map there and stops following, as now; the character chip stays.

## Jump range (web #49)
See [Jump range](web-client.md#map).
- [ ] **Map controls:** a **Jump range** toggle showing the range ("On · 8 ly"). When on, the presets and a custom value:
  - Presets with Jump Drive Calibration V: Black Ops 8 ly; Carrier, Dread and FAX 7; Super and Titan 6; Jump freighter and Rorqual 10. Each preset has a tooltip with its range.
  - Custom: 0.5 to 20 ly.
  - Saved with the desktop settings; off by default, 8 ly.
- [ ] **Which systems:** within the range in 3D (`MapModel.LyBetween`) and a cyno target: display security below 0.5, not Pochven (region 10000070), not Zarzakh (30100000). Port `jumpRangeOf` and `isCynoTarget` from `map/jumpRange.ts`, with its tests.
- [ ] **Drawing** in `jump-range` (`#3fa9ff`, a fixed token in `design/themes.json`, to add to the desktop theme resources):
  - a soft radial glow under each system in range: reach `max(drawn radius × 3, drawn radius + 8, 9)`, alpha 0.8 at the centre, 0.45 halfway, 0 at the edge, drawn under the hunting glow
  - a 1.5 px ring at the drawn radius + 2, drawn over the systems and under the hover ring, so busy systems with an activity colour still read as in range
- [ ] **Hover:** with the range on, the distance from the focus ("14.6 ly from Tama") and **in range**, **out of range** or **no cyno**.
- [ ] **Without a focus** nothing is drawn, and the controls say to focus a system.
