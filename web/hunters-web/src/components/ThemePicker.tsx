import { themeGroups, type ThemeDefinition } from '../theme/themes'
import { Label } from './ui/Label'

interface ThemePickerProps {
  current: ThemeDefinition
  onSelect: (id: string) => void
}

/** Colour theme swatches, grouped by race, faction and corporation. */
export function ThemePicker({ current, onSelect }: ThemePickerProps) {
  return (
    <div className="grid gap-3.5">
      <div className="flex items-baseline gap-2.5">
        <Label>Theme</Label>
        <strong className="font-display text-lg font-semibold text-primary text-shadow-glow">{current.name}</strong>
        <Label>{current.group}</Label>
      </div>
      <div className="flex flex-wrap gap-x-4.5 gap-y-2" role="group" aria-label="Colour theme">
        {themeGroups.map((group) => (
          <div className="flex flex-wrap items-center gap-1.5" key={group.name}>
            <Label>{group.name}</Label>
            {group.themes.map((theme) => (
              <button
                key={theme.id}
                type="button"
                className="chamfer-sm size-5.5 cursor-pointer border border-fg/25 bg-(--c) p-0 transition-transform hover:-translate-y-px focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-fg focus-visible:[clip-path:none] aria-pressed:outline-2 aria-pressed:outline-offset-2 aria-pressed:outline-fg aria-pressed:[clip-path:none]"
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
  )
}
