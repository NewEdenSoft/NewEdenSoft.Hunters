import type { MapFilter } from './filters'

/** The active filter's colours, as a compact scale on one line that shares out the width it has. */
export function MapLegend({ filter }: { filter: MapFilter }) {
  return (
    <ul className="grid auto-cols-fr grid-flow-col gap-px" aria-label={`${filter.label} colours`}>
      {filter.legend.map((item) => (
        <li key={item.label} className="grid min-w-0 justify-items-center gap-1">
          <span className="block h-1.5 w-full" style={{ background: item.colour }} />
          <span className="font-mono text-[0.625rem] text-fg-muted">{item.label}</span>
        </li>
      ))}
    </ul>
  )
}
