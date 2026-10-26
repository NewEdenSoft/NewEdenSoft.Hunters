import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type ReactNode } from 'react'
import type { ActivityMetrics } from '../../api/activity'
import { typeIconUrl } from '../../api/eveImages'
import { systemHuntingQuery, type HuntingRegular } from '../../api/hunting'
import { systemKillsQuery, type RecentKill } from '../../api/kills'
import type { MapModel } from '../../map/model'
import { formatSecurity } from '../../map/security'
import {
  dotlanSystemUrl,
  formatIsk,
  killNameIds,
  metricRows,
  partyName,
  profileStatus,
  sortProfiles,
  timeAgo,
  windowCovers,
  windowLabel,
  zkillboardKillUrl,
  zkillboardSystemUrl,
  type MetricRow,
} from '../../map/systemInfo'
import { useUtcHour } from '../../map/useUtcHour'
import { profileStyle, securityColor } from '../../theme/themes'
import { useTracking } from '../../tracking/trackingContext'
import { Icon, type IconName } from '../ui/Icon'
import { Label } from '../ui/Label'
import { cn } from '../ui/styles'
import { ProfileMark } from './ProfileMark'
import { useDestination, type Destination } from './useDestination'

/**
 * The System panel: the selected system, or the focused one while nothing is selected. Its name,
 * security and region; a command bar (focus, set destination, zKillboard, Dotlan); the last hour's
 * activity with the change from the hour before; the hunting profiles seen there; and its recent kills.
 */
export function SystemInfoPanel({
  model,
  selection,
  focused,
  activity,
  hasHunting,
  hasKills,
  onFocus,
  onClearFocus,
}: {
  model: MapModel
  selection: number
  focused: number
  /** The current hour's activity for this map, or null without it. */
  activity: ActivityMetrics | null
  hasHunting: boolean
  hasKills: boolean
  onFocus: (index: number) => void
  onClearFocus: () => void
}) {
  const index = selection >= 0 ? selection : focused
  if (index < 0) {
    return <p className="text-[0.8125rem] text-fg-muted">Click a system to see it here. Double click one to focus it.</p>
  }

  const id = model.ids[index]
  const name = model.names[index]
  const security = model.security[index]
  const isFocused = index === focused
  return (
    <div className="grid gap-3">
      <div className="grid gap-0.5">
        <div className="flex items-baseline gap-2">
          <span className="min-w-0 truncate font-display text-lg font-semibold text-fg">{name}</span>
          <span className="font-mono text-sm font-semibold" style={{ color: securityColor(security) }}>
            {formatSecurity(security)}
          </span>
          <span className="ml-auto flex shrink-0 gap-2.5">
            <ExternalLink href={zkillboardSystemUrl(id)} label="zKill" title={`${name} on zKillboard`} />
            <ExternalLink href={dotlanSystemUrl(name)} label="Dotlan" title={`${name} on Dotlan`} />
          </span>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-[0.8125rem] text-fg-muted">
          {model.regionNames.get(model.regionIds[index])}
          {index === selection && <Tag>Selected</Tag>}
          {isFocused && <Tag primary>Focused</Tag>}
        </div>
      </div>

      {/* Keyed by system: another system starts with the character list closed. */}
      <CommandBar
        key={id}
        name={name}
        systemId={id}
        isFocused={isFocused}
        onFocus={() => onFocus(index)}
        onClearFocus={onClearFocus}
      />

      {activity && <HourStats rows={metricRows(activity, index)} />}
      {hasHunting && <Hunting systemId={id} />}
      {hasKills && <RecentKills systemId={id} />}
    </div>
  )
}

function Tag({ primary, children }: { primary?: boolean; children: ReactNode }) {
  return (
    <span
      className={cn(
        'border px-1.5 font-display text-[0.625rem] font-semibold tracking-[0.14em] uppercase',
        primary ? 'border-primary text-primary' : 'border-line-strong text-fg',
      )}
    >
      {children}
    </span>
  )
}

const chipClass =
  'inline-flex cursor-pointer items-center gap-1 border border-line-strong px-1.5 py-1 font-display text-[0.6875rem] font-semibold ' +
  'text-fg no-underline hover:border-primary hover:bg-primary-soft focus-visible:outline-2 focus-visible:outline-fg'
const commandClass = chipClass + ' tracking-[0.08em] uppercase'

function Command({ icon, label, title, onClick, active }: { icon: IconName; label: string; title: string; onClick: () => void; active?: boolean }) {
  return (
    <button type="button" title={title} aria-pressed={active} onClick={onClick} className={cn(commandClass, active && 'border-primary text-primary')}>
      <Icon name={icon} className="size-3.5" />
      {label}
    </button>
  )
}

/** Another site, in a new tab: a quiet text link beside the system's name. */
function ExternalLink({ href, label, title }: { href: string; label: string; title: string }) {
  return (
    <a
      href={href}
      target="_blank"
      rel="noreferrer"
      title={title}
      className="inline-flex items-center gap-1 py-1 font-display text-[0.6875rem] font-semibold tracking-[0.08em] text-fg-muted uppercase no-underline hover:text-primary focus-visible:outline-2 focus-visible:outline-fg"
    >
      {label}
      <Icon name="external" className="size-3" />
    </a>
  )
}

/** Focus (or clear it), and set the destination: on every tracked character, or (▾) on one. */
function CommandBar({
  name,
  systemId,
  isFocused,
  onFocus,
  onClearFocus,
}: {
  name: string
  systemId: number
  isFocused: boolean
  onFocus: () => void
  onClearFocus: () => void
}) {
  const [choosing, setChoosing] = useState(false)
  const destination = useDestination(systemId, name)
  const tracked = destination.tracked

  return (
    <div className="grid gap-2">
      <div role="toolbar" aria-label="System actions" className="flex flex-wrap gap-1">
        {isFocused ? (
          <Command icon="close" label="Unfocus" title="Clear the focus: stop using this system as the staging point" onClick={onClearFocus} />
        ) : (
          <Command icon="target" label="Focus" title="Make this system the focus: the staging point for nearby intel and jump range" onClick={onFocus} />
        )}
        {/* A split button: the destination for every tracked character, or ▾ to choose one. */}
        <div role="group" aria-label="Destination" className="flex">
          <button
            type="button"
            disabled={tracked.length === 0 || destination.busy}
            onClick={() => void destination.setFor(tracked)}
            title={
              tracked.length === 0
                ? 'No tracked characters: choose one with the arrow'
                : `Set the autopilot destination here for every tracked character: ${tracked.map((c) => c.name).join(', ')}`
            }
            className={cn(commandClass, 'disabled:cursor-default disabled:opacity-50 disabled:hover:border-line-strong disabled:hover:bg-transparent')}
          >
            <Icon name="flag" className="size-3.5" />
            Destination
            {tracked.length > 0 && <span className="font-mono text-fg-muted">{tracked.length}</span>}
          </button>
          <button
            type="button"
            aria-label="Set the destination for one character"
            title="Set the destination for one character"
            aria-expanded={choosing}
            onClick={() => setChoosing(!choosing)}
            className={cn(commandClass, '-ml-px px-1', choosing && 'border-primary text-primary')}
          >
            <Icon name={choosing ? 'collapse' : 'expand'} className="size-3.5" />
          </button>
        </div>
      </div>
      {choosing && <DestinationPicker destination={destination} />}
      {destination.status && (
        <div
          role="status"
          className={cn('grid text-[0.75rem]', destination.status.tone === 'ok' ? 'text-ok' : destination.status.tone === 'danger' ? 'text-danger' : 'text-fg-muted')}
        >
          {destination.status.lines.map((line) => (
            <span key={line}>{line}</span>
          ))}
        </div>
      )}
    </div>
  )
}

/** The account's usable characters, online ones first: choosing one sets only its destination. */
function DestinationPicker({ destination }: { destination: Destination }) {
  const { usable } = destination
  return (
    <div className="grid gap-1.5 border-l-2 border-primary pl-2.5">
      <span className="text-[0.75rem] text-fg-muted">Set the destination for one character:</span>
      {usable.length === 0 ? (
        <span className="text-[0.8125rem] text-fg-muted">No character can: each needs a working login.</span>
      ) : (
        <div className="flex flex-wrap gap-1">
          {usable.map((c) => (
            <button
              key={c.id}
              type="button"
              disabled={destination.busy}
              onClick={() => void destination.setFor([c])}
              title={c.online ? 'Online' : 'Not online: the destination shows when it logs in, if at all'}
              className={cn(chipClass, !c.online && 'text-fg-muted')}
            >
              <span className={cn('size-1.5 rounded-full', c.online ? 'bg-ok' : 'bg-fg-faint')} />
              {c.name}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

/** The last hour of each metric, the change from the hour before, the last 24 hours, and the usual. */
function HourStats({ rows }: { rows: MetricRow[] }) {
  return (
    <section className="grid gap-1.5">
      <Label>Last hour</Label>
      <div className="grid grid-cols-[1fr_auto_auto_auto_auto] items-baseline gap-x-3 gap-y-0.5 text-[0.8125rem]">
        <span />
        <span className="text-right text-[0.6875rem] text-fg-faint">hour</span>
        <span className="text-right text-[0.6875rem] text-fg-faint">change</span>
        <span className="text-right text-[0.6875rem] text-fg-faint">24 h</span>
        <span className="text-right text-[0.6875rem] text-fg-faint">usual</span>
        {rows.map((r) => (
          <MetricLine key={r.label} row={r} />
        ))}
      </div>
    </section>
  )
}

function MetricLine({ row }: { row: MetricRow }) {
  const { change } = row
  return (
    <>
      <span className="flex min-w-0 items-baseline gap-2 text-fg-muted">
        {row.label}
        {row.peak && <span className={cn('text-[0.75rem]', row.peak === 'Peak now' ? 'text-warning' : 'text-fg-faint')}>{row.peak}</span>}
      </span>
      <span className="text-right font-mono font-semibold text-fg">{row.lastHour}</span>
      <span
        className={cn('text-right font-mono', change === null || change === 0 ? 'text-fg-faint' : change > 0 ? 'text-ok' : 'text-danger')}
        title={change === null ? 'No hour before to compare with' : 'Since the hour before'}
      >
        {change === null ? '–' : change > 0 ? `▲ ${change}` : change < 0 ? `▼ ${-change}` : '±0'}
      </span>
      <span className="text-right font-mono text-fg-muted">{row.day}</span>
      <span className="text-right font-mono text-fg-faint">{row.typical === null ? '–' : Math.round(row.typical)}</span>
    </>
  )
}

/**
 * The hunting profiles seen here: the ones usually here at this hour first. Each lists its regulars: who
 * has a routine here, and when.
 */
function Hunting({ systemId }: { systemId: number }) {
  const place = useQuery(systemHuntingQuery(systemId))
  const hour = useUtcHour()
  const now = useNow()
  const profiles = place.data ? sortProfiles(place.data.profiles, hour) : []
  const nameOf = useEveNames(profiles.flatMap((p) => (p.regulars ?? []).map((r) => r.pilotId)))
  return (
    <section className="grid gap-1.5">
      <Label>Hunting profiles</Label>
      {place.isPending ? (
        <span className="text-[0.8125rem] text-fg-muted">Loading…</span>
      ) : place.isError ? (
        <span className="text-[0.8125rem] text-danger">{place.error.message}</span>
      ) : profiles.length === 0 ? (
        <span className="text-[0.8125rem] text-fg-muted">None seen here.</span>
      ) : (
        <ul className="grid gap-1 text-[0.8125rem]">
          {profiles.map((p) => {
            const status = profileStatus(p, hour)
            return (
              <li key={p.profile} className="grid gap-0.5">
                <span className="flex items-baseline gap-2">
                  <ProfileMark style={profileStyle(p.profile, p.name)} className="w-5 text-[0.6875rem]" />
                  <span className={cn('min-w-0 truncate', status === 'seen' ? 'text-fg-muted' : 'text-fg')}>{p.name}</span>
                  <span
                    className={cn('shrink-0', status === 'seen' ? 'text-fg-faint' : 'text-warning')}
                    title={status === 'seen' ? 'Seen here, but not usually at this hour (UTC)' : 'Usually seen here at this hour (UTC)'}
                  >
                    {status}
                  </span>
                  {(p.reliable?.[hour] ?? 0) > 0 && (
                    <span className="shrink-0 text-hunt-reliable" title="Someone with a routine here is usually here at this hour (UTC)">
                      reliable
                    </span>
                  )}
                  <span className="ml-auto shrink-0 font-mono text-[0.75rem] text-fg-faint" title={`Last seen ${new Date(p.lastSeen).toUTCString()}; ${p.pilots} pilots in 30 days`}>
                    {p.pilots > 0 && `${p.pilots} ${p.pilots === 1 ? 'pilot' : 'pilots'} · `}
                    {timeAgo(p.lastSeen, now)}
                  </span>
                </span>
                {(p.regulars ?? []).length > 0 && <Regulars regulars={p.regulars} hour={hour} nameOf={nameOf} />}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

/**
 * A profile's regulars here: seen on different days at about the same time of day. Their window (UTC),
 * the days in it, "local" when seen in their own alliance's sovereignty, and "now" when the window covers
 * this hour.
 */
function Regulars({ regulars, hour, nameOf }: { regulars: HuntingRegular[]; hour: number; nameOf: (id: number) => string | undefined }) {
  return (
    <ul className="ml-7 grid gap-0.5 text-[0.75rem]">
      {regulars.map((r) => {
        const active = windowCovers(r.windowStart, r.windowHours, hour)
        return (
          <li key={r.pilotId} className="flex items-baseline gap-2">
            <span className={cn('min-w-0 truncate', active ? 'text-fg' : 'text-fg-muted')}>{nameOf(r.pilotId) ?? '…'}</span>
            {r.local && (
              <span className="shrink-0 text-info" title="Seen in its own alliance's sovereignty">
                local
              </span>
            )}
            <span
              className="ml-auto shrink-0 font-mono text-fg-faint"
              title={`${r.daysInWindow} of ${r.days} days seen here in this window; last seen ${new Date(r.lastSeen).toUTCString()}`}
            >
              {windowLabel(r.windowStart, r.windowHours)} · {r.daysInWindow}d
            </span>
            <span className={cn('w-7 shrink-0 text-right', active ? 'text-hunt-reliable' : 'invisible')} title="Usually here at this hour (UTC)">
              now
            </span>
          </li>
        )
      })}
    </ul>
  )
}

/** The time now, moving on every half minute, for "12m ago". */
function useNow(): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 30_000)
    return () => clearInterval(timer)
  }, [])
  return now
}

/** Names for EVE ids from the session's name cache, looked up (once) as needed. */
function useEveNames(ids: number[]): (id: number) => string | undefined {
  const tracking = useTracking()
  const [, setResolved] = useState(0)
  const key = ids.join(',')
  useEffect(() => {
    if (!tracking || key === '') return
    let current = true
    void tracking.names.resolve(key.split(',').map(Number)).then(() => current && setResolved((n) => n + 1))
    return () => {
      current = false
    }
  }, [tracking, key])
  return (id) => tracking?.names.get(id)
}

/** The newest kills here: the ship that died, its pilot, and who dealt the final blow. Each links to zKillboard. */
function RecentKills({ systemId }: { systemId: number }) {
  const kills = useQuery(systemKillsQuery(systemId))
  const nameOf = useEveNames(kills.data ? killNameIds(kills.data.kills) : [])
  const now = useNow()
  return (
    <section className="grid gap-1.5">
      <Label>Recent kills</Label>
      {kills.isPending ? (
        <span className="text-[0.8125rem] text-fg-muted">Loading…</span>
      ) : kills.isError ? (
        <span className="text-[0.8125rem] text-danger">{kills.error.message}</span>
      ) : kills.data.kills.length === 0 ? (
        <span className="text-[0.8125rem] text-fg-muted">No kills here in the kept history.</span>
      ) : (
        <ul className="grid gap-1">
          {kills.data.kills.map((k) => (
            <KillLine key={k.killmailId} kill={k} nameOf={nameOf} now={now} />
          ))}
        </ul>
      )}
    </section>
  )
}

function KillLine({ kill, nameOf, now }: { kill: RecentKill; nameOf: (id: number) => string | undefined; now: number }) {
  const ship = kill.victim.shipTypeId ? (nameOf(kill.victim.shipTypeId) ?? '…') : 'Unknown'
  const victim = partyName(kill.victim, nameOf)
  const finalBlow = kill.finalBlow ? partyName(kill.finalBlow, nameOf) : undefined
  const finalShip = kill.finalBlow?.shipTypeId && kill.finalBlow.characterId ? nameOf(kill.finalBlow.shipTypeId) : undefined
  return (
    <li>
      <a
        href={zkillboardKillUrl(kill.killmailId)}
        target="_blank"
        rel="noreferrer"
        title="Open on zKillboard"
        className="grid grid-cols-[2rem_1fr_auto] items-center gap-x-2 px-1 py-0.5 text-inherit no-underline hover:bg-primary-soft focus-visible:outline-2 focus-visible:outline-fg"
      >
        {kill.victim.shipTypeId ? (
          <img src={typeIconUrl(kill.victim.shipTypeId, 32)} alt="" className="row-span-2 size-8 border border-line" loading="lazy" />
        ) : (
          <span className="row-span-2 size-8 border border-line" />
        )}
        <span className="flex min-w-0 items-baseline gap-1.5 text-[0.8125rem]">
          <span className="truncate font-semibold text-fg">{ship}</span>
          {victim && <span className="truncate text-fg-muted">{victim}</span>}
        </span>
        <span className="text-right font-mono text-[0.75rem] text-fg-muted" title={new Date(kill.time).toUTCString()}>
          {timeAgo(kill.time, now)}
        </span>
        <span className="flex min-w-0 items-baseline gap-1.5 text-[0.75rem] text-fg-muted">
          {kill.npc && <span className="shrink-0 text-warning">NPC</span>}
          {kill.finalBlow ? (
            <span className="truncate">
              by {finalBlow ?? '…'}
              {finalShip && <span className="text-fg-faint"> · {finalShip}</span>}
              {kill.attackerCount > 1 && <span className="text-fg-faint"> +{kill.attackerCount - 1}</span>}
            </span>
          ) : (
            <span className="truncate text-fg-faint">{kill.attackerCount === 1 ? '1 attacker' : `${kill.attackerCount} attackers`}</span>
          )}
        </span>
        <span className="text-right font-mono text-[0.75rem] text-fg">{formatIsk(kill.totalValue)}</span>
      </a>
    </li>
  )
}
