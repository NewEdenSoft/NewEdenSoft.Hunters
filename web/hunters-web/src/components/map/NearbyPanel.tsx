import { useQuery } from '@tanstack/react-query'
import type { HuntingProfileInfo } from '../../api/hunting'
import { reachQuery, type ReachSystem } from '../../api/reach'
import { formatLy } from '../../map/jumpRange'
import type { MapModel } from '../../map/model'
import { distanceText, inJumpRange, nearbyJumpChoices, nearbyRequest, NEARBY_LIMIT } from '../../map/nearby'
import { formatSecurity } from '../../map/security'
import { useJumpRange, useNearby } from '../../settings/mapSettings'
import { profileStyle, securityColor } from '../../theme/themes'
import { ControlGroup } from '../ui/ControlGroup'
import { Label } from '../ui/Label'
import { cn } from '../ui/styles'
import { Toggle } from '../ui/Toggle'
import { ProfileMark } from './ProfileMark'

/**
 * The Nearby panel: what is in reach of the focused system (`/api/intel/reach`), within gate jumps and/or
 * the Jump range panel's light years, sorted by the rise in NPC kills or by a hunting profile's "likely
 * now". Clicking a row selects the system and takes the map there.
 */
export function NearbyPanel({
  model,
  focused,
  selection,
  profiles,
  onSelect,
}: {
  model: MapModel
  focused: number
  selection: number
  /** The server's hunting profiles, for sorting; empty without them. */
  profiles: HuntingProfileInfo[]
  onSelect: (index: number) => void
}) {
  const [setting, update] = useNearby()
  const [range] = useJumpRange()
  const request = nearbyRequest(
    setting,
    focused >= 0 ? model.ids[focused] : null,
    range.ly,
    profiles.map((p) => p.id),
  )
  const reach = useQuery({ ...reachQuery(request ?? { from: 0, jumps: null, ly: null, profile: null, limit: 0 }), enabled: request !== null })
  const names = new Map(profiles.map((p) => [p.id, p.name]))
  // The rows on screen may be the previous answer while the next one loads.
  const rows = reach.data?.systems.filter((s) => s.systemId !== reach.data.from) ?? []

  return (
    <div className="grid gap-3">
      <ControlGroup label="Gate jumps">
        <Toggle active={setting.jumps === null} onClick={() => update({ jumps: null })}>
          Off
        </Toggle>
        {nearbyJumpChoices.map((n) => (
          <Toggle key={n} active={setting.jumps === n} onClick={() => update({ jumps: n })}>
            {n}
          </Toggle>
        ))}
      </ControlGroup>
      <ControlGroup label="Jump range">
        <Toggle
          active={setting.byJumpRange}
          onClick={() => update({ byJumpRange: !setting.byJumpRange })}
          title="Cyno targets within the Jump range panel's range, however many gate jumps away"
        >
          {setting.byJumpRange ? 'On' : 'Off'} · {formatLy(range.ly)}
        </Toggle>
      </ControlGroup>
      {profiles.length > 0 && (
        <label className="grid gap-1.5">
          <Label>Sort by</Label>
          {/* A list, not toggles: one per profile would fill the panel. */}
          <select
            value={request?.profile ?? 'activity'}
            onChange={(e) => update({ sort: e.target.value })}
            className="border border-line-strong bg-bg px-2 py-1 text-[0.8125rem] text-fg focus:border-primary focus:outline-none"
          >
            <option value="activity">NPC kills rising</option>
            {profiles.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}: likely now
              </option>
            ))}
          </select>
        </label>
      )}

      {focused < 0 ? (
        <p className="text-[0.8125rem] text-fg-muted">Focus a system (double click it) to see what is near it.</p>
      ) : request === null ? (
        <p className="text-[0.8125rem] text-fg-muted">Choose gate jumps, the jump range, or both.</p>
      ) : reach.isError ? (
        <p className="text-[0.8125rem] text-danger">{reach.error.message}</p>
      ) : !reach.data ? (
        <p className="text-[0.8125rem] text-fg-muted">Looking around {model.names[focused]}…</p>
      ) : (
        <section className="grid gap-1.5" aria-busy={reach.isFetching}>
          <p className="text-[0.75rem] text-fg-muted">
            {reach.data.total - 1} systems near {model.names[focused]}
            {reach.data.total - 1 > NEARBY_LIMIT && `, the first ${NEARBY_LIMIT} shown`}
            {request.profile ? `, where ${names.get(request.profile)} is likely now first` : ', NPC kills rising first'}.
          </p>
          <ul className={cn('grid gap-px', reach.isFetching && 'opacity-70')}>
            {rows.map((row) => {
              const index = model.indexOf.get(row.systemId)
              if (index === undefined) return null
              return (
                <NearbyRow
                  key={row.systemId}
                  model={model}
                  index={index}
                  row={row}
                  selected={index === selection}
                  byJumps={request.jumps !== null}
                  ly={request.ly}
                  sortProfile={request.profile}
                  names={names}
                  onSelect={() => onSelect(index)}
                />
              )
            })}
          </ul>
        </section>
      )}
    </div>
  )
}

function NearbyRow({
  model,
  index,
  row,
  selected,
  byJumps,
  ly,
  sortProfile,
  names,
  onSelect,
}: {
  model: MapModel
  index: number
  row: ReachSystem
  selected: boolean
  byJumps: boolean
  /** The light years asked for, or null. */
  ly: number | null
  sortProfile: string | null
  names: Map<string, string>
  onSelect: () => void
}) {
  const npc = row.activity?.npcKills
  const change = npc && npc.previousHour !== null ? npc.lastHour - npc.previousHour : null
  const jumpable = inJumpRange(row, ly)
  // The ones likely now first, then the rest by score (the server's order).
  const seen = [...row.profiles].sort((a, b) => Number(b.likelyNow > 0) - Number(a.likelyNow > 0))
  return (
    <li>
      <button
        type="button"
        onClick={onSelect}
        aria-current={selected || undefined}
        className={cn(
          'grid w-full cursor-pointer grid-cols-[1fr_auto] items-baseline gap-x-2 px-1.5 py-1 text-left hover:bg-primary-soft focus-visible:outline-2 focus-visible:outline-fg',
          selected && 'bg-primary-soft',
        )}
      >
        <span className="flex min-w-0 items-baseline gap-2">
          <span className="truncate font-semibold text-fg">{model.names[index]}</span>
          <span className="font-mono text-[0.75rem] font-semibold" style={{ color: securityColor(row.security) }}>
            {formatSecurity(row.security)}
          </span>
        </span>
        <span
          className={cn('text-right font-mono text-[0.75rem]', jumpable ? 'text-jump-range' : 'text-fg-muted')}
          title={ly === null ? undefined : jumpable ? 'In jump range' : row.cynoTarget ? 'Out of jump range' : 'No cyno: high-sec, Pochven or Zarzakh'}
        >
          {distanceText(row, byJumps, ly !== null)}
        </span>
        <span className="flex min-w-0 items-center gap-1.5 text-[0.75rem] text-fg-muted">
          {npc ? (
            <>
              NPC <span className="font-mono text-fg">{npc.lastHour}</span>
              {change !== null && change !== 0 && (
                <span className={cn('font-mono', change > 0 ? 'text-ok' : 'text-danger')}>{change > 0 ? `▲${change}` : `▼${-change}`}</span>
              )}
            </>
          ) : (
            <span className="text-fg-faint">no activity data</span>
          )}
        </span>
        <span className="flex justify-end gap-1">
          {seen.slice(0, 5).map((p) => {
            const name = names.get(p.profile) ?? p.profile
            const now = p.likelyNow > 0
            return (
              <span
                key={p.profile}
                title={`${name}: ${now ? ((p.peakNow ?? 0) >= 0.8 ? 'peak now' : 'usually here now') : 'seen here'}`}
                className={cn(!now && 'opacity-40', p.profile === sortProfile && 'outline outline-1 outline-current')}
              >
                <ProfileMark style={profileStyle(p.profile, name)} className="text-[0.6875rem]" />
              </span>
            )
          })}
        </span>
      </button>
    </li>
  )
}
