import { createFileRoute } from '@tanstack/react-router'
import { ThemePicker } from '../../components/ThemePicker'
import { PageHeader } from '../../components/ui/PageHeader'
import { Panel } from '../../components/ui/Panel'
import { useTheme } from '../../theme/useTheme'

export const Route = createFileRoute('/_authed/settings')({
  component: SettingsPage,
})

function SettingsPage() {
  const [theme, selectTheme] = useTheme()

  return (
    <>
      <PageHeader label="System">Settings</PageHeader>
      <Panel>
        <ThemePicker current={theme} onSelect={selectTheme} />
      </Panel>
    </>
  )
}
