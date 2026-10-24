import { createFileRoute } from '@tanstack/react-router'
import { ThemePicker } from '../../components/ThemePicker'
import { Label } from '../../components/ui/Label'
import { PageHeader } from '../../components/ui/PageHeader'
import { Panel } from '../../components/ui/Panel'
import { Toggle } from '../../components/ui/Toggle'
import { minJumpChoices } from '../../map/autoFocus'
import { mapLayouts, useMapBehaviour, useMapLayout } from '../../settings/mapSettings'
import { useTheme } from '../../theme/useTheme'

export const Route = createFileRoute('/_authed/settings')({
  component: SettingsPage,
})

function SettingsPage() {
  const [theme, selectTheme] = useTheme()
  const [layout, setLayout] = useMapLayout()
  const [behaviour, setBehaviour] = useMapBehaviour()

  return (
    <>
      <PageHeader label="System">Settings</PageHeader>
      <Panel>
        <ThemePicker current={theme} onSelect={selectTheme} />
      </Panel>
      <Panel>
        <Label>Map layout</Label>
        <p className="text-[0.8125rem] text-fg-muted">
          Top-down shows the star map from above, as in space. The 2D map is CCP's flattened layout, as in the in-game 2D map.
        </p>
        <div className="flex flex-wrap gap-1">
          {mapLayouts.map((l) => (
            <Toggle key={l.id} active={l.id === layout} onClick={() => setLayout(l.id)}>
              {l.label}
            </Toggle>
          ))}
        </div>
      </Panel>
      <Panel>
        <Label>Map behaviour</Label>
        <p className="text-[0.8125rem] text-fg-muted">
          A click selects a system. The <span className="text-fg">focus</span> is your staging point, the origin for nearby intel and jump
          range. Set it by double clicking a system (when on below), from a system's menu (right click) or the selection's Focus button, or let
          it follow you as you move. Moves count for the character you follow, or your main while you follow nobody.
        </p>
        <div className="grid gap-2">
          <Setting
            label="Double click to focus"
            hint="Double clicking a system makes it the focus."
            on={behaviour.doubleClickFocus}
            onChange={(on) => setBehaviour({ doubleClickFocus: on })}
          />
          <Setting
            label="Focus on entering known space"
            hint="Coming out of wormhole space (or anywhere not on the map) focuses the system you arrive in."
            on={behaviour.enterKnownSpace}
            onChange={(on) => setBehaviour({ enterKnownSpace: on })}
          />
          <Setting
            label="Focus after a long move"
            hint="A move of at least this many gate jumps at once (a filament, a jump bridge, a cyno) focuses where you land."
            on={behaviour.longMove}
            onChange={(on) => setBehaviour({ longMove: on })}
          />
          {behaviour.longMove && (
            <div className="flex flex-wrap items-center gap-1 pl-1">
              <span className="mr-1 text-[0.8125rem] text-fg-muted">At least</span>
              {minJumpChoices.map((n) => (
                <Toggle key={n} active={n === behaviour.minJumps} onClick={() => setBehaviour({ minJumps: n })}>
                  {n} jumps
                </Toggle>
              ))}
            </div>
          )}
        </div>
      </Panel>
    </>
  )
}

/** One on/off setting: its name and what it does, with On and Off toggles. */
function Setting({ label, hint, on, onChange }: { label: string; hint: string; on: boolean; onChange: (on: boolean) => void }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
      <div className="grid min-w-0 flex-1 gap-0.5">
        <span className="text-fg">{label}</span>
        <span className="text-[0.8125rem] text-fg-muted">{hint}</span>
      </div>
      <div className="flex gap-1" role="group" aria-label={label}>
        <Toggle active={on} onClick={() => onChange(true)}>
          On
        </Toggle>
        <Toggle active={!on} onClick={() => onChange(false)}>
          Off
        </Toggle>
      </div>
    </div>
  )
}
