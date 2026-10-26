import { useQuery } from '@tanstack/react-query'
import type { HuntingProfileInfo } from '../../api/hunting'
import { reachQuery, type ReachSystem } from '../../api/reach'
import { formatLy } from '../../map/jumpRange'
import type { MapModel } from '../../map/model'
import {
  byTargetChance,
  distanceText,
  inJumpRange,
  isActive,
  nearbyJumpChoices,
  nearbyRequest,
  NEARBY_LIMIT,
  TARGET_SORT,
  targetChance,
} from '../../map/nearby'
import { formatSecurity } from '../../map/security'
import { formatIsk as compact } from '../../map/systemInfo'
import { useJumpRange, useNearby } from '../../settings/mapSettings'
import { profileStyle, securityColor } from '../../theme/themes'
import { ControlGroup } from '../ui/ControlGroup'
import { Icon } from '../ui/Icon'
import { Label } from '../ui/Label'
import { cn } from '../ui/styles'
import { Toggle } from '../ui/Toggle'
import { ProfileMark } from './ProfileMark'
import { useDestination } from './useDestination'

/**
 * The Nearby panel: what is in reach of the focused system (`/api/intel/reach`), within gate jumps and/or
 * the Jump range panel's light years, sorted by the chance of a target (here), the rise in NPC kills or
 * by a hunting profile's "likely now". One card per system with activity, with quick controls; systems where nothing happens are
 * hidden unless asked for. Clicking a card selects the system and takes the map there.
 */
export function NearbyPanel({
  model,
  focused,
  selection,
  profiles,
  onSelect,
  onFocus,
}: {
  model: MapModel
  focused: number
  selection: number
  /** The server's hunting profiles, for sorting; empty without them. */
  profiles: HuntingProfileInfo[]
  onSelect: (index: number) => void
  onFocus: (index: number) => void
}) {
  const [setting, update] = useNearby()
  const [range] = useJumpRange()
  const request = nearbyRequest(
    setting,
    focused >= 0 ? model.ids[focused] : null,
    range.ly,
    profiles.map((p) => p.id),
  )
  const reach = useQuery({
    ...reachQuery(request ?? { from: 0, jumps: null, ly: null, profile: null, sort: 'npc-rising', limit: 0 }),
    enabled: request !== null,
  })
  const names = new Map(profiles.map((p) => [p.id, p.name]))
  const byTarget = setting.sort === TARGET_SORT
  // The rows on screen may be the previous answer while the next one loads.
  const rows = reach.data?.systems.filter((s) => s.systemId !== reach.data.from) ?? []
  const around = byTarget ? byTargetChance(rows) : rows
  const active = around.filter(isActive)
  const quiet = around.length - active.length
  const shown = (setting.showQuiet ? around : active).slice(0, NEARBY_LIMIT)

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
      <label className="grid gap-1.5">
        <Label>Sort by</Label>
        {/* A list, not toggles: one per profile would fill the panel. */}
        <select
          value={request?.profile ?? (byTarget || setting.sort === 'npc-kills' ? setting.sort : 'activity')}
          onChange={(e) => update({ sort: e.target.value })}
          className="border border-line-strong bg-bg px-2 py-1 text-[0.8125rem] text-fg focus:border-primary focus:outline-none"
        >
          <option value={TARGET_SORT}>Chance of a target</option>
          <option value="activity">NPC kills rising</option>
          <option value="npc-kills">NPC kills</option>
          {profiles.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}: likely now
            </option>
          ))}
        </select>
      </label>

      {focused < 0 ? (
        <p className="text-[0.8125rem] text-fg-muted">Focus a system (double click it) to see what is near it.</p>
      ) : request === null ? (
        <p className="text-[0.8125rem] text-fg-muted">Choose gate jumps, the jump range, or both.</p>
      ) : reach.isError ? (
        <p className="text-[0.8125rem] text-danger">{reach.error.message}</p>
      ) : !reach.data ? (
        <p className="text-[0.8125rem] text-fg-muted">Looking around {model.names[focused]}…</p>
      ) : (
        <section className="grid gap-2" aria-busy={reach.isFetching}>
          <p className="flex flex-wrap items-baseline gap-x-2 text-[0.75rem] text-fg-muted">
            <span>
              {active.length} active near {model.names[focused]}
              {request.profile
                ? `, ${names.get(request.profile)} likely now first`
                : byTarget
                  ? ', best chance of a target first'
                  : request.sort === 'npc-kills'
                    ? ', most NPC kills first'
                    : ', NPC kills rising first'}
              {shown.length < (setting.showQuiet ? around.length : active.length) && ` (first ${NEARBY_LIMIT})`}.
            </span>
            {quiet > 0 && (
              <button
                type="button"
                onClick={() => update({ showQuiet: !setting.showQuiet })}
                className="cursor-pointer text-fg-faint underline decoration-dotted hover:text-fg"
                title="Systems without kills in the last day or hunting profiles seen"
              >
                {setting.showQuiet ? `hide ${quiet} quiet` : `${quiet} quiet hidden`}
              </button>
            )}
          </p>
          {shown.length === 0 ? (
            <p className="text-[0.8125rem] text-fg-muted">Nothing happening in reach.</p>
          ) : (
            <ul className={cn('grid gap-1.5', reach.isFetching && 'opacity-70')}>
              {shown.map((row) => {
                const index = model.indexOf.get(row.systemId)
                if (index === undefined) return null
                return (
                  <NearbyCard
                    key={row.systemId}
                    name={model.names[index]}
                    row={row}
                    selected={index === selection}
                    byJumps={request.jumps !== null}
                    ly={request.ly}
                    sortProfile={request.profile}
                    chance={byTarget ? targetChance(row) : null}
                    names={names}
                    onSelect={() => onSelect(index)}
                    onFocus={() => onFocus(index)}
                  />
                )
              })}
            </ul>
          )}
        </section>
      )}
    </div>
  )
}

const quickClass =
  'inline-flex cursor-pointer items-center gap-1 border border-line-strong px-1.5 py-0.5 font-display text-[0.625rem] font-semibold tracking-[0.08em] ' +
  'text-fg uppercase hover:border-primary hover:bg-primary-soft focus-visible:outline-2 focus-visible:outline-fg disabled:cursor-default disabled:opacity-50'

/** One system in reach: where, what happens there, the profiles seen, and quick controls. */
function NearbyCard({
  name,
  row,
  selected,
  byJumps,
  ly,
  sortProfile,
  chance,
  names,
  onSelect,
  onFocus,
}: {
  name: string
  row: ReachSystem
  selected: boolean
  byJumps: boolean
  /** The light years asked for, or null. */
  ly: number | null
  sortProfile: string | null
  /** The chance of a target there now (0–1), shown when sorting by it; else null. */
  chance: number | null
  names: Map<string, string>
  onSelect: () => void
  onFocus: () => void
}) {
  const destination = useDestination(row.systemId, name)
  const npc = row.activity?.npcKills
  const change = npc && npc.previousHour !== null ? npc.lastHour - npc.previousHour : null
  const pvpHour = row.activity ? row.activity.shipKills.lastHour + row.activity.podKills.lastHour : 0
  const pvpDay = row.activity ? row.activity.shipKills.day + row.activity.podKills.day : 0
  const jumpable = inJumpRange(row, ly)
  // The ones likely now first, then the rest by score (the server's order).
  const seen = [...row.profiles].sort((a, b) => Number(b.likelyNow > 0) - Number(a.likelyNow > 0))
  const tracked = destination.tracked

  return (
    <li
      onClick={onSelect}
      className={cn(
        'grid cursor-pointer gap-1 border bg-bg/40 px-2 py-1.5 hover:border-primary',
        selected ? 'border-primary bg-primary-soft' : 'border-line',
      )}
    >
      <div className="flex items-baseline gap-2">
        {/* The card is clickable; the name is its button for the keyboard. */}
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation()
            onSelect()
          }}
          aria-current={selected || undefined}
          className="min-w-0 cursor-pointer truncate font-display font-semibold text-fg focus-visible:outline-2 focus-visible:outline-fg"
        >
          {name}
        </button>
        <span className="font-mono text-[0.75rem] font-semibold" style={{ color: securityColor(row.security) }}>
          {formatSecurity(row.security)}
        </span>
        {chance !== null && (
          <span
            className={cn('font-mono text-[0.75rem]', chance >= 0.5 ? 'text-ok' : 'text-fg-muted')}
            title="Chance of a target now: someone ratting (NPC kills in the last hour) or a hunting profile usually here at this hour"
          >
            {Math.round(chance * 100)}%
          </span>
        )}
        <span
          className={cn('ml-auto shrink-0 font-mono text-[0.75rem]', jumpable ? 'text-jump-range' : 'text-fg-muted')}
          title={
            ly === null ? undefined : jumpable ? 'In jump range' : row.cynoTarget ? 'Out of jump range' : 'No cyno: high-sec, Pochven or Zarzakh'
          }
        >
          {distanceText(row, byJumps, ly !== null)}
        </span>
      </div>

      {/* Stats and profiles on the left, the quick controls beside them: two rows when nothing is seen there. */}
      <div className="grid grid-cols-[1fr_auto] items-center gap-x-2">
        <div className="grid min-w-0 gap-1">
          <div className="flex flex-wrap items-baseline gap-x-3 text-[0.75rem] text-fg-muted">
            {npc ? (
              <span title={`NPC kills: last hour${change === null ? '' : ', change since the hour before'}, last 24 hours`}>
                NPC <span className="font-mono text-fg">{npc.lastHour}</span>
                {change !== null && change !== 0 && (
                  <span className={cn('ml-1 font-mono', change > 0 ? 'text-ok' : 'text-danger')}>{change > 0 ? `▲${change}` : `▼${-change}`}</span>
                )}
                <span className="ml-1 font-mono text-fg-faint">{compact(npc.day)}/24h</span>
              </span>
            ) : (
              <span className="text-fg-faint">no activity data</span>
            )}
            {pvpDay > 0 && (
              <span title="Ship and pod kills: last hour, last 24 hours">
                PvP <span className={cn('font-mono', pvpHour > 0 ? 'text-warning' : 'text-fg')}>{pvpHour}</span>
                <span className="ml-1 font-mono text-fg-faint">{pvpDay}/24h</span>
              </span>
            )}
          </div>

          {seen.length > 0 && (
            <span className="flex min-w-0 flex-wrap gap-1">
              {seen.slice(0, 6).map((p) => {
                const profileName = names.get(p.profile) ?? p.profile
                const now = p.likelyNow > 0
                return (
                  <span
                    key={p.profile}
                    title={`${profileName}: ${now ? ((p.peakNow ?? 0) >= 0.8 ? 'peak now' : 'usually here now') : 'seen here'}`}
                    className={cn(!now && 'opacity-40', p.profile === sortProfile && 'outline outline-current')}
                  >
                    <ProfileMark style={profileStyle(p.profile, profileName)} className="text-[0.6875rem]" />
                  </span>
                )
              })}
            </span>
          )}
        </div>
        <span className="flex shrink-0 gap-1">
          <button
            type="button"
            disabled={tracked.length === 0 || destination.busy}
            onClick={(e) => {
              e.stopPropagation()
              void destination.setFor(tracked)
            }}
            title={
              tracked.length === 0
                ? 'No tracked characters'
                : `Set the autopilot destination to ${name} for every tracked character: ${tracked.map((c) => c.name).join(', ')}`
            }
            className={quickClass}
          >
            <Icon name="flag" className="size-3" />
            Dest
            {tracked.length > 0 && <span className="font-mono text-fg-muted">{tracked.length}</span>}
          </button>
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation()
              onFocus()
            }}
            title={`Focus ${name}: look around it instead`}
            aria-label={`Focus ${name}`}
            className={quickClass}
          >
            <Icon name="target" className="size-3" />
          </button>
        </span>
      </div>

      {destination.status && (
        <div
          role="status"
          className={cn(
            'grid text-[0.6875rem]',
            destination.status.tone === 'ok' ? 'text-ok' : destination.status.tone === 'danger' ? 'text-danger' : 'text-fg-muted',
          )}
        >
          {destination.status.lines.map((line) => (
            <span key={line}>{line}</span>
          ))}
        </div>
      )}
    </li>
  )
}
