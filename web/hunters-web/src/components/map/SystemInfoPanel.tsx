import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type ReactNode } from 'react'
import { accountQuery } from '../../api/account'
import type { ActivityMetrics } from '../../api/activity'
import { typeIconUrl } from '../../api/eveImages'
import { systemHuntingQuery } from '../../api/hunting'
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
  zkillboardKillUrl,
  zkillboardSystemUrl,
  type MetricRow,
} from '../../map/systemInfo'
import { useUtcHour } from '../../map/useUtcHour'
import { profileStyle, securityColor } from '../../theme/themes'
import { setDestination } from '../../tracking/esi'
import { useTracking } from '../../tracking/trackingContext'
import { Icon, type IconName } from '../ui/Icon'
import { Label } from '../ui/Label'
import { cn } from '../ui/styles'
import { ProfileMark } from './ProfileMark'

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

const commandClass =
  'inline-flex cursor-pointer items-center gap-1 border border-line-strong px-1.5 py-1 font-display text-[0.6875rem] font-semibold tracking-[0.08em] ' +
  'text-fg uppercase no-underline hover:border-primary hover:bg-primary-soft focus-visible:outline-2 focus-visible:outline-fg'

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

/** Focus (or clear it), and set a character's destination. */
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

  return (
    <div className="grid gap-2">
      <div role="toolbar" aria-label="System actions" className="flex flex-wrap gap-1">
        {isFocused ? (
          <Command icon="close" label="Unfocus" title="Clear the focus: stop using this system as the staging point" onClick={onClearFocus} />
        ) : (
          <Command icon="target" label="Focus" title="Make this system the focus: the staging point for nearby intel and jump range" onClick={onFocus} />
        )}
        <Command
          icon="flag"
          label="Destination"
          title="Set a character's autopilot destination here, in the game"
          active={choosing}
          onClick={() => setChoosing(!choosing)}
        />
      </div>
      {choosing && <DestinationPicker systemId={systemId} name={name} onDone={() => setChoosing(false)} />}
    </div>
  )
}

/** The account's usable characters, online ones first: choosing one sets its destination through ESI. */
function DestinationPicker({ systemId, name, onDone }: { systemId: number; name: string; onDone: () => void }) {
  const tracking = useTracking()
  const account = useQuery(accountQuery)
  const [status, setStatus] = useState<{ tone: 'ok' | 'danger' | 'muted'; text: string } | null>(null)
  const characters = (account.data?.characters ?? [])
    .filter((c) => c.status === 'Active')
    .map((c) => ({ ...c, online: tracking?.tracker.get(c.id)?.isOnline === true }))
    .sort((a, b) => Number(b.online) - Number(a.online) || Number(b.isMain) - Number(a.isMain) || a.name.localeCompare(b.name))

  const set = async (characterId: number, characterName: string) => {
    if (!tracking) return
    setStatus({ tone: 'muted', text: `Setting ${characterName}'s destination…` })
    try {
      let result = await setDestination(systemId, await tracking.tokens.getAccessToken(characterId))
      if (result.status === 'unauthorized') {
        tracking.tokens.invalidate(characterId)
        result = await setDestination(systemId, await tracking.tokens.getAccessToken(characterId))
      }
      if (result.status === 'ok') {
        setStatus({ tone: 'ok', text: `Destination set: ${characterName} → ${name}.` })
        setTimeout(onDone, 1500)
      } else {
        setStatus({ tone: 'danger', text: result.status === 'forbidden' ? `${characterName} has not allowed setting waypoints; log in with it again.` : result.error })
      }
    } catch (error) {
      setStatus({ tone: 'danger', text: (error as Error).message })
    }
  }

  return (
    <div className="grid gap-1.5 border-l-2 border-primary pl-2.5">
      <span className="text-[0.75rem] text-fg-muted">Set the destination for:</span>
      {characters.length === 0 ? (
        <span className="text-[0.8125rem] text-fg-muted">No character can: each needs a working login.</span>
      ) : (
        <div className="flex flex-wrap gap-1">
          {characters.map((c) => (
            <button
              key={c.id}
              type="button"
              onClick={() => void set(c.id, c.name)}
              title={c.online ? 'Online' : 'Not online: the destination shows when it logs in, if at all'}
              className={cn(commandClass, 'normal-case tracking-normal', !c.online && 'text-fg-muted')}
            >
              <span className={cn('size-1.5 rounded-full', c.online ? 'bg-ok' : 'bg-fg-faint')} />
              {c.name}
            </button>
          ))}
        </div>
      )}
      {status && (
        <span role="status" className={cn('text-[0.75rem]', status.tone === 'ok' ? 'text-ok' : status.tone === 'danger' ? 'text-danger' : 'text-fg-muted')}>
          {status.text}
        </span>
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

/** The hunting profiles seen here: the ones usually here at this hour first. */
function Hunting({ systemId }: { systemId: number }) {
  const place = useQuery(systemHuntingQuery(systemId))
  const hour = useUtcHour()
  const now = useNow()
  const profiles = place.data ? sortProfiles(place.data.profiles, hour) : []
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
        <ul className="grid gap-0.5 text-[0.8125rem]">
          {profiles.map((p) => {
            const status = profileStatus(p, hour)
            return (
              <li key={p.profile} className="flex items-baseline gap-2">
                <ProfileMark style={profileStyle(p.profile, p.name)} className="w-5 text-[0.6875rem]" />
                <span className={cn('min-w-0 truncate', status === 'seen' ? 'text-fg-muted' : 'text-fg')}>{p.name}</span>
                <span
                  className={cn('shrink-0', status === 'seen' ? 'text-fg-faint' : 'text-warning')}
                  title={status === 'seen' ? 'Seen here, but not usually at this hour (UTC)' : 'Usually seen here at this hour (UTC)'}
                >
                  {status}
                </span>
                <span className="ml-auto shrink-0 font-mono text-[0.75rem] text-fg-faint" title={`Last seen ${new Date(p.lastSeen).toUTCString()}; ${p.pilots} pilots in 30 days`}>
                  {p.pilots > 0 && `${p.pilots} ${p.pilots === 1 ? 'pilot' : 'pilots'} · `}
                  {timeAgo(p.lastSeen, now)}
                </span>
              </li>
            )
          })}
        </ul>
      )}
    </section>
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
