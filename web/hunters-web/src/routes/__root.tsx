import type { QueryClient } from '@tanstack/react-query'
import { createRootRouteWithContext, Link, Outlet, useRouter, type ErrorComponentProps } from '@tanstack/react-router'
import { lazy, Suspense } from 'react'

export interface RouterContext {
  queryClient: QueryClient
}

// Devtools only in development; the dynamic imports keep them out of the production bundle.
const Devtools = import.meta.env.DEV
  ? lazy(async () => {
      const [{ TanStackRouterDevtools }, { ReactQueryDevtools }] = await Promise.all([
        import('@tanstack/react-router-devtools'),
        import('@tanstack/react-query-devtools'),
      ])
      return {
        default: () => (
          <>
            <TanStackRouterDevtools position="bottom-left" />
            <ReactQueryDevtools buttonPosition="bottom-right" />
          </>
        ),
      }
    })
  : () => null

export const Route = createRootRouteWithContext<RouterContext>()({
  component: RootLayout,
  notFoundComponent: NotFound,
  errorComponent: RouteError,
})

function RootLayout() {
  return (
    <>
      <Outlet />
      <Suspense>
        <Devtools />
      </Suspense>
    </>
  )
}

/** A page failed to load, for example because the server could not be reached. */
function RouteError({ error }: ErrorComponentProps) {
  const router = useRouter()
  return (
    <main className="wrap page">
      <section className="panel">
        <span className="label">Something went wrong</span>
        <p role="alert" className="status status-danger">
          {error instanceof Error ? error.message : String(error)}
        </p>
        <div className="row">
          <button type="button" className="btn" onClick={() => void router.invalidate()}>
            Try again
          </button>
        </div>
      </section>
    </main>
  )
}

function NotFound() {
  return (
    <main className="wrap page">
      <section className="panel">
        <span className="label">Not found</span>
        <p>There is nothing at this address.</p>
        <div className="row">
          <Link to="/" className="btn">
            Home
          </Link>
        </div>
      </section>
    </main>
  )
}
