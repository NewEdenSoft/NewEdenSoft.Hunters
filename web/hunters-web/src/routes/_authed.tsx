import { useSuspenseQuery } from '@tanstack/react-query'
import { createFileRoute, Link, Outlet, redirect, useMatches } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { accountQuery } from '../api/account'
import { characterPortraitUrl } from '../api/eveImages'
import { ApiError } from '../api/http'
import { Icon, type IconName } from '../components/ui/Icon'
import { cn, wrap } from '../components/ui/styles'
import { panelDefinitions, type PanelDefinition } from '../panels/definitions'
import { resetPanels, togglePanel, usePanel } from '../panels/panelStore'

/**
 * Layout for every page that needs a session (docs/decisions.md, D19). Loading the account is the
 * session check: without a valid session cookie the user is sent to /login and comes back here
 * after logging in.
 */
export const Route = createFileRoute('/_authed')({
  beforeLoad: async ({ context, location }) => {
    try {
      await context.queryClient.ensureQueryData(accountQuery)
    } catch (error) {
      if (error instanceof ApiError && error.isUnauthorized) {
        throw redirect({ to: '/login', search: { redirect: location.href } })
      }
      throw error
    }
  },
  component: AuthedLayout,
})

/**
 * A slim rail on the left of every page: the map, and on the map a toggle per panel; Account,
 * Settings and log out at the bottom. The map fills the rest of the window; other pages scroll in
 * the centred column.
 */
function AuthedLayout() {
  const { data: account } = useSuspenseQuery(accountQuery)
  const main = account.characters.find((c) => c.id === account.mainCharacterId)
  const fullBleed = useMatches({ select: (matches) => matches.some((m) => m.staticData.fullBleed) })

  return (
    <div className="flex h-dvh">
      <nav aria-label="Main" className="flex w-14 shrink-0 flex-col items-center gap-1 border-r border-line bg-surface py-2.5">
        <Link
          to="/"
          aria-label="Hunters"
          title="Hunters"
          className="mb-2 grid size-10 place-items-center font-display text-lg font-bold text-primary no-underline text-shadow-glow focus-visible:outline-2 focus-visible:outline-fg"
        >
          H
        </Link>
        <RailLink to="/" icon="map" label="Map" exact />

        {fullBleed && (
          <>
            <div className="my-1.5 h-px w-7 bg-line" />
            {panelDefinitions.map((d) => (
              <PanelToggle key={d.id} definition={d} />
            ))}
            <RailButton icon="reset" label="Reset panels" onClick={resetPanels} />
          </>
        )}

        <div className="mt-auto flex flex-col items-center gap-1">
          <RailLink to="/account" icon="user" label={main ? `Account · ${main.name}` : 'Account'}>
            {main && <img src={characterPortraitUrl(main.id, 64)} alt="" className="size-7 border border-line-strong" />}
          </RailLink>
          <RailLink to="/settings" icon="settings" label="Settings" />
          {/* A plain form post: the server clears the cookie and redirects. */}
          <form method="post" action="/auth/web/logout">
            <RailButton type="submit" icon="logout" label="Log out" />
          </form>
        </div>
      </nav>

      {fullBleed ? (
        <main className="relative min-w-0 flex-1">
          <Outlet />
        </main>
      ) : (
        <main className="min-w-0 flex-1 overflow-y-auto">
          <div className={cn(wrap, 'grid gap-6 pt-10 pb-18')}>
            <Outlet />
          </div>
        </main>
      )}
    </div>
  )
}

const railItem =
  'relative grid size-10 cursor-pointer place-items-center text-fg-muted no-underline hover:bg-primary-soft hover:text-fg ' +
  'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-fg'

/** Highlighted, with a bar on the rail's edge. */
const railActive = 'text-primary before:absolute before:top-2 before:bottom-2 before:-left-2 before:w-0.5 before:bg-primary'

function RailLink({
  to,
  icon,
  label,
  exact,
  children,
}: {
  to: '/' | '/account' | '/settings'
  icon: IconName
  label: string
  exact?: boolean
  children?: ReactNode
}) {
  return (
    <Link
      to={to}
      activeOptions={{ exact }}
      aria-label={label}
      title={label}
      className={railItem}
      activeProps={{ className: railActive }}
    >
      {children ?? <Icon name={icon} />}
    </Link>
  )
}

function RailButton({ icon, label, active, ...props }: { icon: IconName; label: string; active?: boolean } & React.ComponentProps<'button'>) {
  return (
    <button type="button" aria-label={label} title={label} className={cn(railItem, active && railActive)} {...props}>
      <Icon name={icon} />
    </button>
  )
}

/** Opens or closes one panel over the map. */
function PanelToggle({ definition }: { definition: PanelDefinition }) {
  const { open } = usePanel(definition.id)
  return (
    <RailButton
      icon={definition.icon}
      label={`${open ? 'Close' : 'Open'} the ${definition.title} panel`}
      aria-pressed={open}
      active={open}
      onClick={() => togglePanel(definition.id)}
    />
  )
}
