import { useSuspenseQuery } from '@tanstack/react-query'
import { createFileRoute, Link, Outlet, redirect } from '@tanstack/react-router'
import { accountQuery } from '../api/account'
import { ApiError } from '../api/http'

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

  return (
    <>
      <header className="appbar">
        <div className="wrap appbar-inner">
          <Link to="/" className="brand">
            Hunters
          </Link>
          <nav className="nav" aria-label="Main">
            <Link to="/" activeOptions={{ exact: true }}>
              Home
            </Link>
            <Link to="/account">Account</Link>
            <Link to="/settings">Settings</Link>
          </nav>
          <div className="appbar-user">
            {main && <span className="muted">{main.name}</span>}
            {/* A plain form post: the server clears the cookie and redirects. */}
            <form method="post" action="/auth/web/logout">
              <button type="submit" className="btn ghost">
                Log out
              </button>
            </form>
          </div>
        </div>
      </header>
      <main className="wrap page">
        <Outlet />
      </main>
    </>
  )
}
