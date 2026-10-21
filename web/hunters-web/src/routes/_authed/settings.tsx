import { createFileRoute } from '@tanstack/react-router'
import { ThemePicker } from '../../components/ThemePicker'
import { Label } from '../../components/ui/Label'
import { PageHeader } from '../../components/ui/PageHeader'
import { Panel } from '../../components/ui/Panel'
import { Toggle } from '../../components/ui/Toggle'
import { mapLayouts, useMapLayout } from '../../settings/mapSettings'
import { useTheme } from '../../theme/useTheme'

export const Route = createFileRoute('/_authed/settings')({
  component: SettingsPage,
})

function SettingsPage() {
  const [theme, selectTheme] = useTheme()
  const [layout, setLayout] = useMapLayout()

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
    </>
  )
}
