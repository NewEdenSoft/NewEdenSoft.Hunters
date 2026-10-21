import { createFileRoute } from '@tanstack/react-router'
import { ThemePicker } from '../../components/ThemePicker'
import { useTheme } from '../../theme/useTheme'

export const Route = createFileRoute('/_authed/settings')({
  component: SettingsPage,
})

function SettingsPage() {
  const [theme, selectTheme] = useTheme()

  return (
    <>
      <section className="hero">
        <span className="label accent">System</span>
        <h1>Settings</h1>
      </section>
      <section className="panel">
        <ThemePicker current={theme} onSelect={selectTheme} />
      </section>
    </>
  )
}
