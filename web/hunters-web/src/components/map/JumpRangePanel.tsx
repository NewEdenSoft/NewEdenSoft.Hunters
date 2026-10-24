import { formatLy, jumpRangePresets, MAX_JUMP_RANGE, MIN_JUMP_RANGE } from '../../map/jumpRange'
import type { MapModel } from '../../map/model'
import { useJumpRange } from '../../settings/mapSettings'
import { ControlGroup } from '../ui/ControlGroup'
import { Toggle } from '../ui/Toggle'

/** The Jump range panel: the toggle, the range (a preset or custom) and what the blue means. */
export function JumpRangePanel({ model, focused }: { model: MapModel; focused: number }) {
  const [setting, update] = useJumpRange()
  return (
    <div className="grid gap-3">
      <ControlGroup label="Show">
        <Toggle active={setting.enabled} onClick={() => update({ enabled: !setting.enabled })}>
          {setting.enabled ? 'On' : 'Off'} · {formatLy(setting.ly)}
        </Toggle>
      </ControlGroup>
      <ControlGroup label="Range">
        {jumpRangePresets.map((p) => (
          <Toggle key={p.id} active={p.ly === setting.ly} onClick={() => update({ ly: p.ly })} title={`${formatLy(p.ly)} with Jump Drive Calibration V`}>
            {p.label}
          </Toggle>
        ))}
      </ControlGroup>
      <label className="flex items-center gap-2 text-[0.8125rem] text-fg-muted">
        Custom
        <input
          type="number"
          min={MIN_JUMP_RANGE}
          max={MAX_JUMP_RANGE}
          step={0.1}
          value={setting.ly}
          onChange={(e) => {
            const ly = e.target.valueAsNumber
            if (Number.isFinite(ly)) update({ ly })
          }}
          className="w-20 border border-line-strong bg-bg px-2 py-1 font-mono text-fg focus:border-primary focus:outline-none"
        />
        ly
      </label>
      <p className="text-[0.75rem] text-fg-muted">
        {focused >= 0 ? (
          <>
            <span className="text-jump-range">Blue</span>: systems a jump drive can land in from {model.names[focused]} (not high-sec, Pochven or Zarzakh).
          </>
        ) : (
          'Focus a system (double click it) to see what is in range of it.'
        )}
      </p>
    </div>
  )
}
