import { useSuspenseQuery } from '@tanstack/react-query'
import { createFileRoute, Link, Outlet, redirect, useMatches } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { accountQuery } from '../api/account'
import { ApiError } from '../api/http'
import { Button } from '../components/ui/Button'
import { cn, wrap } from '../components/ui/styles'

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

function AuthedLayout() {
  const { data: account } = useSuspenseQuery(accountQuery)
  const main = account.characters.find((c) => c.id === account.mainCharacterId)
  // Pages like the map fill the window below the app bar instead of the centred column.
  const fullBleed = useMatches({ select: (matches) => matches.some((m) => m.staticData.fullBleed) })

  return (
    <div className={cn('flex flex-col', fullBleed ? 'h-dvh' : 'min-h-dvh')}>
      <header className="sticky top-0 z-10 shrink-0 border-b border-line bg-bg/90 backdrop-blur-md">
        <div className={cn(wrap, 'flex flex-wrap items-center gap-x-7 gap-y-2.5 py-2.5')}>
          <Link
            to="/"
            className="font-display text-lg font-semibold tracking-[0.18em] text-primary uppercase no-underline text-shadow-glow focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-fg"
          >
            Hunters
          </Link>
          <nav className="flex flex-wrap gap-x-5 gap-y-1" aria-label="Main">
            <NavLink to="/" exact>
              Home
            </NavLink>
            <NavLink to="/map">Map</NavLink>
            <NavLink to="/account">Account</NavLink>
            <NavLink to="/settings">Settings</NavLink>
          </nav>
          <div className="ml-auto flex items-center gap-3">
            {main && <span className="text-fg-muted">{main.name}</span>}
            {/* A plain form post: the server clears the cookie and redirects. */}
            <form method="post" action="/auth/web/logout">
              <Button type="submit" variant="ghost">
                Log out
              </Button>
            </form>
          </div>
        </div>
      </header>
      {fullBleed ? (
        <main className="relative min-h-0 flex-1">
          <Outlet />
        </main>
      ) : (
        <main className={cn(wrap, 'grid gap-6 pt-10 pb-18')}>
          <Outlet />
        </main>
      )}
    </div>
  )
}

function NavLink({ to, exact, children }: { to: '/' | '/map' | '/account' | '/settings'; exact?: boolean; children: ReactNode }) {
  return (
    <Link
      to={to}
      activeOptions={{ exact }}
      className="border-b-2 border-transparent py-1 font-display text-[0.8125rem] font-semibold tracking-[0.14em] text-fg-muted uppercase no-underline hover:text-fg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-fg data-[status=active]:border-primary data-[status=active]:text-primary"
    >
      {children}
    </Link>
  )
}
