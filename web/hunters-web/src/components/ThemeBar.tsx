import { themeGroups, type ThemeDefinition } from '../theme/themes'

interface ThemeBarProps {
  current: ThemeDefinition
  onSelect: (id: string) => void
}

export function ThemeBar({ current, onSelect }: ThemeBarProps) {
  return (
    <header className="themebar">
      <div className="wrap themebar-inner">
        <div className="themebar-current">
          <span className="label">Theme</span>
          <strong>{current.name}</strong>
          <span className="label">{current.group}</span>
        </div>
        <div className="theme-groups" role="group" aria-label="Colour theme">
          {themeGroups.map((group) => (
            <div className="theme-group" key={group.name}>
              <span className="label">{group.name}</span>
              {group.themes.map((theme) => (
                <button
                  key={theme.id}
                  type="button"
                  className="swatch"
                  style={{ '--c': theme.primary } as React.CSSProperties}
                  title={theme.name}
                  aria-label={`${theme.name} theme`}
                  aria-pressed={theme.id === current.id}
                  onClick={() => onSelect(theme.id)}
                />
              ))}
            </div>
          ))}
        </div>
      </div>
    </header>
  )
}
