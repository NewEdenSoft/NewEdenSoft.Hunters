import type { HuntingProfileInfo } from '../../api/hunting'
import { profileStyle } from '../../theme/themes'
import { Toggle } from '../ui/Toggle'
import { ProfileMark } from './ProfileMark'

/** The Hunting panel: a toggle per hunting profile, shown on the map as a glow and badges. */
export function HuntingPanel({
  profiles,
  shown,
  toggle,
}: {
  /** The server's hunting profiles; empty when it has none (yet). */
  profiles: HuntingProfileInfo[]
  shown: string[]
  toggle: (id: string) => void
}) {
  if (profiles.length === 0) return <p className="text-[0.8125rem] text-fg-muted">This server has no hunting profiles yet.</p>
  return (
    <div className="grid gap-3">
      <div className="flex flex-wrap gap-1">
        {profiles.map((p) => (
          <Toggle key={p.id} active={shown.includes(p.id)} onClick={() => toggle(p.id)}>
            <ProfileMark style={profileStyle(p.id, p.name)} className="mr-1.5" />
            {p.name}
          </Toggle>
        ))}
      </div>
      <p className="text-[0.75rem] text-fg-muted">
        {shown.length > 0
          ? 'The glow is where a shown profile is likely now: usually seen there at this hour (UTC). A ring marks where someone with a routine is usually there at this hour.'
          : 'Show a profile to see where it is seen, and where it is likely now.'}
      </p>
    </div>
  )
}
