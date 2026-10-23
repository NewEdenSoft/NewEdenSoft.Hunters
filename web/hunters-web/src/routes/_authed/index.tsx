import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useEffect, useMemo } from 'react'
import { activityQuery } from '../../api/activity'
import { discoveryQuery } from '../../api/discovery'
import { mapPackQuery } from '../../api/map'
import { Label } from '../../components/ui/Label'
import { Panel } from '../../components/ui/Panel'
import { Status } from '../../components/ui/Status'
import { Toggle } from '../../components/ui/Toggle'
import {
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
import { mapLayouts, useMapLayout } from '../../settings/mapSettings'

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
    refetchInterval: (query) =>
      query.state.data && !query.state.data.mapPack ? 10_000 : query.state.data && !query.state.data.features.includes('activity') ? 300_000 : false,
  })
  const sdeBuild = discovery.data?.mapPack?.sdeBuild
  const hasActivity = discovery.data?.features.includes('activity') ?? false
  const pack = useQuery({ ...mapPackQuery(sdeBuild ?? 0), enabled: sdeBuild !== undefined })
  const model = useMemo(() => (pack.data ? buildMapModel(pack.data) : null), [pack.data])

  // Activity is fetched only while an activity colour is chosen.
  const needsActivity = mapFilters.find((f) => f.id === colour)?.activity ?? false
  const activity = useQuery({ ...activityQuery, enabled: needsActivity })
  const minNpc = search.min ?? 0
  const filter = useMemo(
    () => (model ? createFilter(colour, model, { activity: activity.data ?? null, minNpc }) : null),
    [colour, model, activity.data, minNpc],
  )

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
