import { useQuery } from '@tanstack/react-query'
import { createFileRoute, redirect } from '@tanstack/react-router'
import { accountQuery } from '../api/account'
import { discoveryQuery } from '../api/discovery'

interface LoginSearch {
  /** Where to go after logging in; a path on this site. */
  redirect?: string
  /** Set by the server when an EVE login failed (see LoginEvents.WebErrorUrl). */
  error?: string
  message?: string
}

export const Route = createFileRoute('/login')({
  validateSearch: (search: Record<string, unknown>): LoginSearch => ({
    redirect: localPath(search.redirect),
    error: typeof search.error === 'string' ? search.error : undefined,
    message: typeof search.message === 'string' ? search.message : undefined,
  }),
  // Already logged in: skip the login page.
  beforeLoad: async ({ context, search }) => {
    const signedIn = await context.queryClient.fetchQuery(accountQuery).then(
      () => true,
      () => false,
    )
    if (signedIn) {
      throw redirect({ href: search.redirect ?? '/' })
    }
  },
  component: LoginPage,
})

function LoginPage() {
  const { redirect: returnUrl, error, message } = Route.useSearch()
  const discovery = useQuery(discoveryQuery)

  return (
    <main className="wrap page login">
      <section className="hero">
        <span className="label accent">NewEdenSoft · Hunters</span>
        <h1>
          Log in to <span className="accent glow">{discovery.data?.name ?? 'Hunters'}</span>
        </h1>
      </section>

      <section className="panel">
        <span className="label">Log in</span>
        <p className="muted">Log in with your EVE Online character. You are sent to EVE Online and back.</p>

        {error && (
          <p role="alert" className="status status-danger">
            {message ?? 'The login failed. Try again.'}
          </p>
        )}

        {/* A plain form post: the server starts the EVE login and redirects the browser. */}
        <form method="post" action="/auth/web/login" className="form">
          {returnUrl && <input type="hidden" name="returnUrl" value={returnUrl} />}
          {discovery.data?.admission.requiresAccessKey && (
            <label className="field">
              <span className="label">Access key (only for a new account)</span>
              <input name="accessKey" type="password" autoComplete="off" className="input" />
            </label>
          )}
          <div className="row">
            <button type="submit" className="btn primary">
              Log in with EVE Online
            </button>
          </div>
        </form>
      </section>
    </main>
  )
}

/** Only paths on this site, so the login cannot be used as an open redirect. */
function localPath(value: unknown): string | undefined {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\')
    ? value
    : undefined
}
