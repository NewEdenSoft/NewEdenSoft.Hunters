import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { huntingProfilesQuery, type HuntingProfileInfo } from '../../api/hunting'
import { ProfileMark } from '../../components/map/ProfileMark'
import { Label } from '../../components/ui/Label'
import { PageHeader } from '../../components/ui/PageHeader'
import { Panel } from '../../components/ui/Panel'
import { profileStyle } from '../../theme/themes'

export const Route = createFileRoute('/_authed/info')({
  component: InfoPage,
})

/**
 * How to use the map, what its marks and words mean, how the hunting profiles are scored, and what
 * powers each profile. The profiles' own explanations come from the server, next to their rules.
 */
function InfoPage() {
  return (
    <>
      <PageHeader label="Info">How Hunters works</PageHeader>
      <Panel>
        <p className="text-[0.9375rem] text-fg-muted">
          Hunters reads every kill in New Eden and works out where targets live and when they are there, so you can land somewhere, often off a
          filament, and find something worth catching in reach. The map shows it; this page explains how to read it.
        </p>
      </Panel>
      <UsingTheMap />
      <Indicators />
      <Scoring />
      <Profiles />
    </>
  )
}

function UsingTheMap() {
  return (
    <Panel>
      <Label>Using the map</Label>
      <dl className="grid gap-3 text-[0.875rem]">
        <Term name="Move around">Drag to pan, scroll or pinch to zoom. Names and profile badges appear as you zoom in.</Term>
        <Term name="Select">
          Click a system to select it: the System panel shows its activity, the hunting profiles seen there with their regulars, and its recent
          kills. Click empty space or press Esc to clear it. Hover a system for a quick summary.
        </Term>
        <Term name="Focus">
          The focus is your staging point: Nearby and the jump range are measured from it. Double click a system (when on in Settings), use a
          system's menu (right click, or a long press), or the Focus button of the selection. It can also follow you: entering known space or a
          long move focuses where your character is.
        </Term>
        <Term name="Search">Press / or use the search at the top of the map to find a system; Enter goes there and selects it.</Term>
        <Term name="Panels">
          The rail on the left opens and closes the panels: Map (layout and colours), Nearby, Hunting (which profiles to show), Jump range,
          Characters and System. Drag a panel by its title, resize it from its corner; they snap to the edges and to each other. Reset panels puts
          them back.
        </Term>
        <Term name="Characters">
          Your tracked characters are marked on the map. Click one to go to it; the target button follows it as it moves. Dragging the map stops
          following.
        </Term>
        <Term name="Colours">
          The Map panel colours the systems by security, or by activity from ESI's hourly figures: NPC kills, NPC kills rising or falling since
          the hour before, ship kills or jumps.
        </Term>
      </dl>
    </Panel>
  )
}

function Indicators() {
  return (
    <Panel>
      <Label>On the map</Label>
      <dl className="grid gap-3 text-[0.875rem]">
        <Indicator swatch={<Glow />} name="Amber glow">
          A shown hunting profile is <Em>likely now</Em>: it is seen there, and usually at this hour (UTC). The brighter and wider, the likelier,
          compared with that profile's best system right now, so a rare target still lights up where it is most likely.
        </Indicator>
        <Indicator swatch={<Ring className="border-hunt-reliable" />} name="Mint ring">
          <Em>Reliable now</Em>: someone with a routine in that system is usually there at this hour. The stronger the routine (more days, more
          recent), the more solid the ring. A glow says a kind of target lives there; the ring says someone is likely in space right now.
        </Indicator>
        <Indicator swatch={<ProfileMark style={profileStyle('carriers', 'Carriers')} className="w-6 text-[0.6875rem]" />} name="Badges">
          The shown profiles seen in a system, in a row under its name when zoomed in. Faint when seen there, but not usually at this hour.
        </Indicator>
        <Indicator swatch={<Ring className="border-jump-range bg-jump-range/30" />} name="Blue glow and ring">
          With Jump range on: systems a jump drive can land in from the focus. Only cyno targets count: low and null security, not Pochven or
          Zarzakh.
        </Indicator>
        <Indicator swatch={<Ring className="border-primary outline-2 outline-offset-2 outline-primary" />} name="Double ring">
          The focus, your staging point.
        </Indicator>
        <Indicator swatch={<Ring className="border-fg" />} name="Ring with ticks">
          The selected system.
        </Indicator>
      </dl>
      <Label>In the panels</Label>
      <dl className="grid gap-3 text-[0.875rem]">
        <Term name={<span className="text-warning">peak now · now · seen</span>}>
          For a profile in a system: <Em>peak now</Em> is its busiest hour of the day there, <Em>now</Em> that it is usually there at this hour,{' '}
          <Em>seen</Em> that it has been seen there, but not usually now.
        </Term>
        <Term name={<span className="text-hunt-reliable">reliable</span>}>Someone with a routine there is usually there at this hour: the map's ring.</Term>
        <Term name="Regulars">
          Under each profile in the System panel: pilots seen there on different days at about the same time of day. Each shows the window
          (for example 22–02 UTC), the days seen in it, and <span className="text-hunt-reliable">now</span> when the window covers this hour.
        </Term>
        <Term name={<span className="text-info">local</span>}>
          Seen in its own alliance's sovereignty: someone who lives there, not passing through.
        </Term>
        <Term name={<span className="text-warning">Peak now · Peak in N h</span>}>
          Activity: the system is in its busiest hours of the week for NPC kills, or will be within six hours.
        </Term>
      </dl>
    </Panel>
  )
}

function Scoring() {
  return (
    <Panel>
      <Label>How the profiles are scored</Label>
      <ul className="grid list-disc gap-2 pl-5 text-[0.875rem] text-fg-muted">
        <li>
          <Em>What dies in a system lives there.</Em> A profile counts ships when they die, not when they kill: a capital that helped kill something
          may have come for that kill and left.
        </li>
        <li>
          Each kill that matches a profile is a <Em>sighting</Em>, at most one per pilot an hour, so someone ratting all evening counts once an
          hour, not once a kill.
        </li>
        <li>
          A ship <Em>lost to NPCs</Em> counts double: it died ratting there, the strongest sign it comes back. A <Em>local</Em>, in its own
          alliance's sovereignty at the time, counts one and a half times; both together, three.
        </li>
        <li>
          <Em>Indirect evidence</Em> counts less: a CRAB beacon killed hints that a capital ratted there (0.75 for Carriers and Supers).
        </li>
        <li>
          <Em>Fleet fights</Em> are left out: kills with more than 25 attackers, or 20 kills in a system within a quarter of an hour. Smartbomb camps
          keep their bursts, which are what a camp does.
        </li>
        <li>
          The <Em>score</Em> counts every sighting, each halving every 14 days, so recent habits count most and an abandoned system fades. The
          glow is the score times this hour's share of the sightings there.
        </li>
        <li>
          A <Em>regular</Em> is a pilot seen in a system on at least two different days within a four-hour window of the day (a play session), or a
          six-hour one at half strength, in the last 30 days. Its strength is those days, each fading over 14 days; about three recent days makes a
          solid ring.
        </li>
        <li>
          Kills come from zKillboard as they happen and are kept for 90 days; activity and sovereignty come from ESI hourly. The map updates
          every few minutes.
        </li>
      </ul>
    </Panel>
  )
}

/** Each profile with what it is and what counts as seeing it, as the server explains them next to its rules. */
function Profiles() {
  const profiles = useQuery(huntingProfilesQuery)
  return (
    <Panel>
      <Label>The profiles</Label>
      {profiles.isPending ? (
        <p className="text-[0.875rem] text-fg-muted">Loading…</p>
      ) : profiles.isError ? (
        <p className="text-[0.875rem] text-fg-muted">The server has no hunting profiles yet.</p>
      ) : (
        <div className="grid gap-4">
          {profiles.data.map((p) => (
            <ProfileEntry key={p.id} profile={p} />
          ))}
        </div>
      )}
    </Panel>
  )
}

function ProfileEntry({ profile }: { profile: HuntingProfileInfo }) {
  return (
    <section className="grid gap-1 text-[0.875rem]">
      <h2 className="flex items-baseline gap-2 text-base">
        <ProfileMark style={profileStyle(profile.id, profile.name)} className="w-6 text-[0.6875rem]" />
        {profile.name}
      </h2>
      {profile.description && <p className="text-fg-muted">{profile.description}</p>}
      {profile.evidence.length > 0 && (
        <>
          <span className="text-[0.75rem] text-fg-faint">Seen when</span>
          <ul className="grid list-disc gap-1 pl-5 text-fg-muted">
            {profile.evidence.map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}

function Term({ name, children }: { name: ReactNode; children: ReactNode }) {
  return (
    <div className="grid gap-0.5">
      <dt className="font-semibold text-fg">{name}</dt>
      <dd className="text-fg-muted">{children}</dd>
    </div>
  )
}

/** A map mark with a small drawing of it. */
function Indicator({ swatch, name, children }: { swatch: ReactNode; name: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[2.5rem_1fr] items-start gap-x-3">
      <span className="grid h-6 place-items-center" aria-hidden>
        {swatch}
      </span>
      <Term name={name}>{children}</Term>
    </div>
  )
}

function Glow() {
  return <span className="size-6 rounded-full bg-[radial-gradient(circle,var(--hunt-glow)_0%,transparent_70%)]" />
}

function Ring({ className }: { className: string }) {
  return <span className={`size-4 rounded-full border-2 ${className}`} />
}

function Em({ children }: { children: ReactNode }) {
  return <span className="text-fg">{children}</span>
}
