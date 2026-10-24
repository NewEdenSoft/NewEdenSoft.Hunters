import type { ReactNode } from 'react'
import { Label } from './Label'

/** A labelled row of toggles in a panel. */
export function ControlGroup({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-1.5">
      <Label>{label}</Label>
      <div className="flex flex-wrap gap-1">{children}</div>
    </div>
  )
}
