import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useEffect, useMemo } from 'react'
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
import { focusCharacter, stopFollowing } from '../../map/mapFocus'
import { parseSystemParam } from '../../map/systemSearch'
import { useCharacterMarkers, useMapFocusTarget } from '../../map/useCharacterMarkers'
import { panelDefinitions } from '../../panels/definitions'
import { Panels } from '../../panels/Panels'
import { mapLayouts, useMapLayout, useShownHuntingProfiles } from '../../settings/mapSettings'
import { buildHuntingLayer, type HuntingLayer } from '../../map/hunting'
import { useUtcHour } from '../../map/useUtcHour'
import { profileStyle } from '../../theme/themes'
import { ProfileMark } from '../../components/map/ProfileMark'

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
  const hasActivity = discovery.data?.features.includes('activity') ?? false
  const hasHunting = discovery.data?.features.includes('hunting') ?? false
  const pack = useQuery({ ...mapPackQuery(sdeBuild ?? 0), enabled: sdeBuild !== undefined })
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
  const selected = system === undefined ? -1 : (model.indexOf.get(system) ?? -1)
  // Opened with a focused system: the map starts there.
  const [focus, goTo] = useMapFocusTarget(model, selected)

  // A system id from an old link that is not on this map: forget it.
  useEffect(() => {
    if (system !== undefined && selected < 0) setSystem(undefined)
  }, [system, selected, setSystem])

  /** Focus a system: the map goes there, taking over from a followed character as dragging does. */
  const focusSystem = (index: number) => {
    stopFollowing()
    goTo(index)
    setSystem(model.ids[index])
  }

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
        selected={selected}
        onFocusSystem={focusSystem}
        npcKills={npcKills}
        hunting={hunting}
        onClearSystem={() => setSystem(undefined)}
      />
      <div className="pointer-events-none absolute top-3 left-1/2 z-[1000] grid w-[calc(100%-1.5rem)] max-w-80 -translate-x-1/2 justify-items-center gap-2">
        <SystemSearch model={model} selected={selected} onSelect={focusSystem} onClear={() => setSystem(undefined)} />
        <FocusChip />
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
            {huntingProfiles.length > 0 && (
              <ControlGroup label="Hunting">
                {huntingProfiles.map((p) => {
                  const style = profileStyle(p.id, p.name)
                  return (
                    <Toggle key={p.id} active={shownProfiles.includes(p.id)} onClick={() => toggleProfile(p.id)}>
                      <ProfileMark style={style} className="mr-1.5" />
                      {p.name}
                    </Toggle>
                  )
                })}
              </ControlGroup>
            )}
            {shownProfiles.length > 0 && (
              <p className="text-[0.75rem] text-fg-muted">The glow is where a shown profile is likely now: usually seen there at this hour (UTC).</p>
            )}
          </div>
            ),
          },
          { definition: panel('characters'), content: <CharactersPanel /> },
        ]}
      />
    </div>
  )
}

const panel = (id: string) => panelDefinitions.find((d) => d.id === id)!

function ControlGroup({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid gap-1.5">
      <Label>{label}</Label>
      <div className="flex flex-wrap gap-1">{children}</div>
    </div>
  )
}
