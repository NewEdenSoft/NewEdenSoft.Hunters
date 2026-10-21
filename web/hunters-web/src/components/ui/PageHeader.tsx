import type { ReactNode } from 'react'
import { Label } from './Label'

interface PageHeaderProps {
  label: string
  children: ReactNode
}

/** Section caption and page title. */
export function PageHeader({ label, children }: PageHeaderProps) {
  return (
    <header className="grid gap-3">
      <Label tone="accent">{label}</Label>
      <h1 className="text-4xl leading-tight max-sm:text-[1.75rem]">{children}</h1>
    </header>
  )
}
