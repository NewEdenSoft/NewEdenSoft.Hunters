import type { MapFilter } from './filters'

/** The active filter's colours, as a compact scale. */
export function MapLegend({ filter }: { filter: MapFilter }) {
  return (
    <ul className="flex flex-wrap gap-px" aria-label={`${filter.label} colours`}>
      {filter.legend.map((item) => (
        <li key={item.label} className="grid min-w-7 justify-items-center gap-1">
          <span className="block h-1.5 w-full" style={{ background: item.colour }} />
          <span className="font-mono text-[0.625rem] text-fg-muted">{item.label}</span>
        </li>
      ))}
    </ul>
  )
}
