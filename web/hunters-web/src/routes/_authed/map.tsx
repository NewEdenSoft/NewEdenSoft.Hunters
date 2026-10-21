import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { discoveryQuery } from '../../api/discovery'
import { mapPackQuery } from '../../api/map'
import { Button } from '../../components/ui/Button'
import { Label } from '../../components/ui/Label'
import { Panel } from '../../components/ui/Panel'
import { Status } from '../../components/ui/Status'
import { cn } from '../../components/ui/styles'
import { buildMapModel, type MapLayout } from '../../map/model'
import { MapView } from '../../map/MapView'

export const Route = createFileRoute('/_authed/map')({
  staticData: { fullBleed: true },
  component: MapPage,
})

const layouts: { id: MapLayout; label: string }[] = [
  { id: 'topDown', label: 'Top-down' },
  { id: 'flat', label: '2D map' },
]

function MapPage() {
  // Discovery names the current map pack; it is missing while the server is still loading the SDE.
  const discovery = useQuery({
    ...discoveryQuery,
    refetchInterval: (query) => (query.state.data && !query.state.data.mapPack ? 10_000 : false),
  })
  const sdeBuild = discovery.data?.mapPack?.sdeBuild
  const pack = useQuery({ ...mapPackQuery(sdeBuild ?? 0), enabled: sdeBuild !== undefined })
  const model = useMemo(() => (pack.data ? buildMapModel(pack.data) : null), [pack.data])
  const [layout, setLayout] = useState<MapLayout>('topDown')

  if (!model) {
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

  return (
    <div className="relative size-full">
      <MapView model={model} layout={layout} />
      <div className="absolute top-3 left-3 flex gap-1 border border-line bg-surface/90 p-1 backdrop-blur-sm">
        {layouts.map((l) => (
          <Button
            key={l.id}
            variant={l.id === layout ? 'primary' : 'ghost'}
            className={cn('px-3 py-1.5 text-[0.6875rem]')}
            aria-pressed={l.id === layout}
            onClick={() => setLayout(l.id)}
          >
            {l.label}
          </Button>
        ))}
      </div>
    </div>
  )
}
