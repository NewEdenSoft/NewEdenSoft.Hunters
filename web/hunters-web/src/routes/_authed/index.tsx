import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useEffect, useMemo, useState } from 'react'
import { activityQuery, type ActivityColumns } from '../../api/activity'
import { discoveryQuery } from '../../api/discovery'
import { huntingOverviewQuery, huntingProfilesQuery, type HuntingProfileInfo } from '../../api/hunting'
import { mapPackQuery } from '../../api/map'
import { Label } from '../../components/ui/Label'
import { Panel } from '../../components/ui/Panel'
import { Status } from '../../components/ui/Status'
import { Toggle } from '../../components/ui/Toggle'
import {
  activityFor,
  createFilter,
  mapFilters,
  minNpcChoices,
  parseColourParam,
  parseMinNpcParam,
  type MapFilter,
  type MapFilterId,
} from '../../map/filters'
import { MapLegend } from '../../map/MapLegend'
import { buildMapModel, type MapLayout, type MapModel } from '../../map/model'
import { MapView } from '../../map/MapView'
import { CharactersPanel } from '../../components/characters/CharactersPanel'
import { FocusChip } from '../../components/characters/FocusChip'
import { SystemSearch } from '../../components/map/SystemSearch'
import { SelectionCard } from '../../components/map/SelectionCard'
import { useAutoFocus } from '../../map/useAutoFocus'
import { focusCharacter, stopFollowing } from '../../map/mapFocus'
import { parseSystemParam } from '../../map/systemSearch'
import { useCharacterMarkers, useMapFocusTarget } from '../../map/useCharacterMarkers'
import { panelDefinitions } from '../../panels/definitions'
import { Panels } from '../../panels/Panels'
import { getMapBehaviour, mapLayouts, useJumpRange, useMapLayout, useShownHuntingProfiles } from '../../settings/mapSettings'
import { jumpRangeOf } from '../../map/jumpRange'
import { buildHuntingLayer, profilesBySystem, type HuntingLayer, type PlaceProfile } from '../../map/hunting'
import { useUtcHour } from '../../map/useUtcHour'
import { profileStyle } from '../../theme/themes'
import { HuntingPanel } from '../../components/map/HuntingPanel'
import { JumpRangePanel } from '../../components/map/JumpRangePanel'
import { ControlGroup } from '../../components/ui/ControlGroup'

export interface MapSearch {
  colour?: MapFilterId
  /** For NPC rising and falling: the minimum NPC kills in the last hour. */
  min?: number
  /** The focused system: the origin for nearby intel, ringed on the map. */
  system?: number
}

export const Route = createFileRoute('/_authed/')({
  // The map is the app: it fills the window beside the rail, with panels floating over it.
  staticData: { fullBleed: true },
  // The colour filter and the focused system are in the URL, so a view can be linked; defaults are
  // left out. The layout is a user setting (settings/mapSettings.ts).
  validateSearch: (search: Record<string, unknown>): MapSearch => ({
    colour: parseColourParam(search.colour),
    min: parseMinNpcParam(search.min),
    system: parseSystemParam(search.system),
  }),
  component: MapPage,
})

function MapPage() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const [layout, setLayout] = useMapLayout()
  const colour = search.colour ?? mapFilters[0].id

  // Discovery names the current map pack; it is missing while the server is still loading the SDE.
  // Discovery also says whether the server has activity data; it is asked again until it does.
  const discovery = useQuery({
    ...discoveryQuery,
    refetchInterval: (query) => {
      const data = query.state.data
      if (data && !data.mapPack) return 10_000
      return data && !(data.features.includes('activity') && data.features.includes('hunting')) ? 300_000 : false
    },
  })
  const sdeBuild = discovery.data?.mapPack?.sdeBuild
  const packFormat = discovery.data?.mapPack?.format
  const hasActivity = discovery.data?.features.includes('activity') ?? false
  const hasHunting = discovery.data?.features.includes('hunting') ?? false
  const pack = useQuery({ ...mapPackQuery(sdeBuild ?? 0, packFormat), enabled: sdeBuild !== undefined })
  const model = useMemo(() => (pack.data ? buildMapModel(pack.data) : null), [pack.data])

  // Activity is fetched whenever the server has it: the hover panel always shows NPC kills.
  const needsActivity = mapFilters.find((f) => f.id === colour)?.activity ?? false
  const activity = useQuery({ ...activityQuery, enabled: needsActivity || hasActivity })
  const minNpc = search.min ?? 0
  const filter = useMemo(
    () => (model ? createFilter(colour, model, { activity: activity.data ?? null, minNpc }) : null),
    [colour, model, activity.data, minNpc],
  )

  // Hunting profiles: the list once the server has them, where they are seen once one is shown.
  const [shownProfiles, toggleProfile] = useShownHuntingProfiles()
  const profiles = useQuery({ ...huntingProfilesQuery, enabled: hasHunting })
  const shown = useMemo(() => shownProfiles.filter((id) => profiles.data?.some((p) => p.id === id)), [shownProfiles, profiles.data])
  const overview = useQuery({ ...huntingOverviewQuery, enabled: hasHunting && shown.length > 0 })
  const hour = useUtcHour()
  const hunting = useMemo(() => {
    if (!model || !overview.data || shown.length === 0) return null
    const names = new Map(profiles.data?.map((p) => [p.id, p.name]))
    return buildHuntingLayer(model, overview.data, shown, hour, (id) => profileStyle(id, names.get(id) ?? id))
  }, [model, overview.data, shown, hour, profiles.data])
  // The hover panel lists the shown profiles seen at a system.
  const profilesAt = useMemo(() => {
    if (!model || !overview.data || shown.length === 0) return null
    const names = new Map(profiles.data?.map((p) => [p.id, p.name]))
    const nameOf = (id: string) => names.get(id) ?? id
    return profilesBySystem(model, overview.data, shown, hour, (id) => profileStyle(id, nameOf(id)), nameOf)
  }, [model, overview.data, shown, hour, profiles.data])

  if (!model || !filter) {
    const error = discovery.error ?? pack.error
    return (
      <div className="grid size-full place-items-center p-4">
        <Panel className="max-w-md">
          <Label>Map</Label>
          {error ? (
            <Status tone="danger" role="alert">
              {error.message}
            </Status>
          ) : (
            <p className="text-fg-muted">
              {discovery.data && !discovery.data.mapPack ? 'The server is preparing the map…' : 'Loading the map…'}
            </p>
          )}
        </Panel>
      </div>
    )
  }

  const setColour = (id: MapFilterId) =>
    void navigate({ search: (prev) => ({ ...prev, colour: id === mapFilters[0].id ? undefined : id }), replace: true })
  const setMinNpc = (min: number) => void navigate({ search: (prev) => ({ ...prev, min: min > 0 ? min : undefined }), replace: true })
  const setSystem = (id: number | undefined) => void navigate({ search: (prev) => ({ ...prev, system: id }), replace: true })

  return (
    <LoadedMap
      model={model}
      npcKills={activityFor(model, activity.data ?? null)?.npcKills ?? null}
      hunting={hunting}
      profilesAt={profilesAt}
      huntingProfiles={profiles.data ?? []}
      shownProfiles={shown}
      toggleProfile={toggleProfile}
      filter={filter}
      layout={layout}
      setLayout={setLayout}
      colour={colour}
      setColour={setColour}
      hasActivity={hasActivity}
      minNpc={minNpc}
      setMinNpc={setMinNpc}
      system={search.system}
      setSystem={setSystem}
    />
  )
}

/**
 * The map once its pack is loaded: the canvas with the characters' markers and the focused system, the
 * system search and character chip at the top, and the panels.
 */
function LoadedMap({
  model,
  npcKills,
  hunting,
  profilesAt,
  huntingProfiles,
  shownProfiles,
  toggleProfile,
  filter,
  layout,
  setLayout,
  colour,
  setColour,
  hasActivity,
  minNpc,
  setMinNpc,
  system,
  setSystem,
}: {
  model: MapModel
  /** NPC kills per system (last hour and the one before), for the hover panel; null without activity data. */
  npcKills: ActivityColumns | null
  hunting: HuntingLayer | null
  /** The hunting profiles seen at each system, for the hover panel. */
  profilesAt: Map<number, PlaceProfile[]> | null
  /** The server's hunting profiles; empty when it has none. */
  huntingProfiles: HuntingProfileInfo[]
  shownProfiles: string[]
  toggleProfile: (id: string) => void
  filter: MapFilter
  layout: MapLayout
  setLayout: (layout: MapLayout) => void
  colour: MapFilterId
  setColour: (id: MapFilterId) => void
  /** The server has activity data: the activity colours are offered. */
  hasActivity: boolean
  minNpc: number
  setMinNpc: (min: number) => void
  system: number | undefined
  setSystem: (id: number | undefined) => void
}) {
  const markers = useCharacterMarkers(model)
  // The focused system is the staging point (in the URL); the selection is the system last clicked.
  const focused = system === undefined ? -1 : (model.indexOf.get(system) ?? -1)
  const [selection, setSelection] = useState(-1)
  // Opened with a focused system: the map starts there.
  const [focus, goTo] = useMapFocusTarget(model, focused)

  // A system id from an old link that is not on this map: forget it.
  useEffect(() => {
    if (system !== undefined && focused < 0) setSystem(undefined)
  }, [system, focused, setSystem])

  // The jump range: drawn around the focused system while it is on.
  const [rangeSetting] = useJumpRange()
  const jumpRange = useMemo(
    () => (rangeSetting.enabled && focused >= 0 ? { origin: focused, ly: rangeSetting.ly, inRange: jumpRangeOf(model, focused, rangeSetting.ly) } : null),
    [model, focused, rangeSetting.enabled, rangeSetting.ly],
  )

  // Escape clears the selection, unless the user is typing somewhere.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null
      if (e.key !== 'Escape' || (target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))) return
      setSelection(-1)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  /** Focus a system by hand: the map goes there, taking over from a followed character as dragging does. */
  const focusSystem = (index: number) => {
    stopFollowing()
    goTo(index)
    setSystem(model.ids[index])
  }

  /** Select a system from the search: the map goes there too. */
  const findSystem = (index: number) => {
    stopFollowing()
    goTo(index)
    setSelection(index)
  }

  // The followed character (or the main) left unknown space or moved far: its system becomes the focus.
  // Following goes on; the map goes there.
  useAutoFocus(model, (index) => {
    goTo(index)
    setSystem(model.ids[index])
  })

  return (
    <div className="relative size-full">
      <MapView
        model={model}
        layout={layout}
        filter={filter}
        markers={markers}
        focus={focus}
        onMarkerClick={focusCharacter}
        onUserPan={stopFollowing}
        focused={focused}
        selection={selection}
        onSelectSystem={setSelection}
        onDoubleClickSystem={(index) => {
          if (getMapBehaviour().doubleClickFocus) focusSystem(index)
        }}
        onFocusSystem={focusSystem}
        npcKills={npcKills}
        hunting={hunting}
        profilesAt={profilesAt}
        jumpRange={jumpRange}
        onClearSystem={() => setSystem(undefined)}
      />
      <div className="pointer-events-none absolute top-3 left-1/2 z-[1000] grid w-[calc(100%-1.5rem)] max-w-80 -translate-x-1/2 justify-items-center gap-2">
        <SystemSearch model={model} focused={focused} onSelect={findSystem} onClear={() => setSystem(undefined)} />
        <FocusChip />
        {selection >= 0 && (
          <SelectionCard
            model={model}
            index={selection}
            focused={selection === focused}
            onFocus={() => focusSystem(selection)}
            onDismiss={() => setSelection(-1)}
          />
        )}
      </div>

      <Panels
        items={[
          {
            definition: panel('map'),
            content: (
          <div className="grid gap-3">
            <ControlGroup label="Layout">
              {mapLayouts.map((l) => (
                <Toggle key={l.id} active={l.id === layout} onClick={() => setLayout(l.id)}>
                  {l.label}
                </Toggle>
              ))}
            </ControlGroup>
            <ControlGroup label="Colour">
              {mapFilters
                .filter((f) => !f.activity || hasActivity || f.id === colour)
                .map((f) => (
                  <Toggle key={f.id} active={f.id === colour} onClick={() => setColour(f.id)}>
                    {f.label}
                  </Toggle>
                ))}
            </ControlGroup>
            {(colour === 'npc-rising' || colour === 'npc-falling') && (
              <ControlGroup label="Min NPC kills, last hour">
                {minNpcChoices.map((min) => (
                  <Toggle key={min} active={min === minNpc} onClick={() => setMinNpc(min)}>
                    {min === 0 ? 'Any' : `${min}+`}
                  </Toggle>
                ))}
              </ControlGroup>
            )}
            {filter.notice ? <p className="text-[0.8125rem] text-fg-muted">{filter.notice}</p> : <MapLegend filter={filter} />}
          </div>
            ),
          },
          {
            definition: panel('hunting'),
            content: <HuntingPanel profiles={huntingProfiles} shown={shownProfiles} toggle={toggleProfile} />,
          },
          { definition: panel('jump-range'), content: <JumpRangePanel model={model} focused={focused} /> },
          { definition: panel('characters'), content: <CharactersPanel /> },
        ]}
      />
    </div>
  )
}

const panel = (id: string) => panelDefinitions.find((d) => d.id === id)!
