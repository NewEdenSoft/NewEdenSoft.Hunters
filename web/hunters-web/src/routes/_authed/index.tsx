import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useMemo } from 'react'
import { discoveryQuery } from '../../api/discovery'
import { mapPackQuery } from '../../api/map'
import { Label } from '../../components/ui/Label'
import { Panel } from '../../components/ui/Panel'
import { Status } from '../../components/ui/Status'
import { Toggle } from '../../components/ui/Toggle'
import { createFilter, mapFilters, type MapFilter, type MapFilterId } from '../../map/filters'
import { MapLegend } from '../../map/MapLegend'
import { buildMapModel, type MapLayout, type MapModel } from '../../map/model'
import { MapView } from '../../map/MapView'
import { CharactersPanel } from '../../components/characters/CharactersPanel'
import { FocusChip } from '../../components/characters/FocusChip'
import { focusCharacter, stopFollowing } from '../../map/mapFocus'
import { useCharacterMarkers, useMapFocusTarget } from '../../map/useCharacterMarkers'
import { panelDefinitions } from '../../panels/definitions'
import { Panels } from '../../panels/Panels'
import { mapLayouts, useMapLayout } from '../../settings/mapSettings'

export interface MapSearch {
  colour?: MapFilterId
}

export const Route = createFileRoute('/_authed/')({
  // The map is the app: it fills the window beside the rail, with panels floating over it.
  staticData: { fullBleed: true },
  // The colour filter is in the URL, so a view can be linked; the default is left out. The layout is
  // a user setting (settings/mapSettings.ts).
  validateSearch: (search: Record<string, unknown>): MapSearch => ({
    colour:
      mapFilters.some((f) => f.id === search.colour) && search.colour !== mapFilters[0].id
        ? (search.colour as MapFilterId)
        : undefined,
  }),
  component: MapPage,
})

function MapPage() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const [layout, setLayout] = useMapLayout()
  const colour = search.colour ?? mapFilters[0].id

  // Discovery names the current map pack; it is missing while the server is still loading the SDE.
  const discovery = useQuery({
    ...discoveryQuery,
    refetchInterval: (query) => (query.state.data && !query.state.data.mapPack ? 10_000 : false),
  })
  const sdeBuild = discovery.data?.mapPack?.sdeBuild
  const pack = useQuery({ ...mapPackQuery(sdeBuild ?? 0), enabled: sdeBuild !== undefined })
  const model = useMemo(() => (pack.data ? buildMapModel(pack.data) : null), [pack.data])
  const filter = useMemo(() => (model ? createFilter(colour, model) : null), [colour, model])

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
    void navigate({ search: { colour: id === mapFilters[0].id ? undefined : id }, replace: true })

  return <LoadedMap model={model} filter={filter} layout={layout} setLayout={setLayout} colour={colour} setColour={setColour} />
}

/** The map once its pack is loaded: the canvas with the characters' markers, the focus chip and the panels. */
function LoadedMap({
  model,
  filter,
  layout,
  setLayout,
  colour,
  setColour,
}: {
  model: MapModel
  filter: MapFilter
  layout: MapLayout
  setLayout: (layout: MapLayout) => void
  colour: MapFilterId
  setColour: (id: MapFilterId) => void
}) {
  const markers = useCharacterMarkers(model)
  const focus = useMapFocusTarget(model)

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
      />
      <FocusChip />

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
              {mapFilters.map((f) => (
                <Toggle key={f.id} active={f.id === colour} onClick={() => setColour(f.id)}>
                  {f.label}
                </Toggle>
              ))}
            </ControlGroup>
            <MapLegend filter={filter} />
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
