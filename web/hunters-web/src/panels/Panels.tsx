import type { ReactNode } from 'react'
import { BottomSheet } from './BottomSheet'
import type { PanelDefinition } from './definitions'
import { FloatingPanel } from './FloatingPanel'
import { PanelLayer } from './PanelLayer'
import { useIsPhone } from './useIsPhone'

export interface PanelItem {
  definition: PanelDefinition
  content: ReactNode
}

/** The panels over the map: floating panels on larger screens, a bottom sheet with tabs on a phone. */
export function Panels({ items }: { items: PanelItem[] }) {
  const phone = useIsPhone()
  if (phone) return <BottomSheet items={items} />
  return (
    <PanelLayer>
      {items.map(({ definition, content }) => (
        <FloatingPanel key={definition.id} definition={definition}>
          {content}
        </FloatingPanel>
      ))}
    </PanelLayer>
  )
}
