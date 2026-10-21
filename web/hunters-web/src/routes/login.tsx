import { useQuery } from '@tanstack/react-query'
import { createFileRoute, redirect } from '@tanstack/react-router'
import { accountQuery } from '../api/account'
import { discoveryQuery } from '../api/discovery'
import { Button } from '../components/ui/Button'
import { Label } from '../components/ui/Label'
import { PageHeader } from '../components/ui/PageHeader'
import { Panel } from '../components/ui/Panel'
import { Status } from '../components/ui/Status'

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
    <main className="mx-auto grid w-full max-w-160 gap-6 px-4 pt-10 pb-18">
      <PageHeader label="NewEdenSoft · Hunters">
        Log in to <span className="text-primary text-shadow-glow">{discovery.data?.name ?? 'Hunters'}</span>
      </PageHeader>

      <Panel>
        <Label>Log in</Label>
        <p className="text-fg-muted">Log in with your EVE Online character. You are sent to EVE Online and back.</p>

        {error && (
          <Status tone="danger" role="alert">
            {message ?? 'The login failed. Try again.'}
          </Status>
        )}

        {/* A plain form post: the server starts the EVE login and redirects the browser. */}
        <form method="post" action="/auth/web/login" className="grid gap-3.5">
          {returnUrl && <input type="hidden" name="returnUrl" value={returnUrl} />}
          {discovery.data?.admission.requiresAccessKey && (
            <label className="grid max-w-105 gap-1.5">
              <Label>Access key (only for a new account)</Label>
              <input
                name="accessKey"
                type="password"
                autoComplete="off"
                className="border border-line-strong bg-bg px-2.5 py-2 font-mono text-fg outline-none hover:border-primary focus:border-primary"
              />
            </label>
          )}
          <div>
            <Button type="submit" variant="primary">
              Log in with EVE Online
            </Button>
          </div>
        </form>
      </Panel>
    </main>
  )
}

/** Only paths on this site, so the login cannot be used as an open redirect. */
function localPath(value: unknown): string | undefined {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\')
    ? value
    : undefined
}
