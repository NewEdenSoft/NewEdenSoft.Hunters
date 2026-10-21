import type { QueryClient } from '@tanstack/react-query'
import { createRootRouteWithContext, Link, Outlet, useRouter, type ErrorComponentProps } from '@tanstack/react-router'
import { lazy, Suspense } from 'react'
import { Button } from '../components/ui/Button'
import { Label } from '../components/ui/Label'
import { Panel } from '../components/ui/Panel'
import { Status } from '../components/ui/Status'
import { buttonClass, cn, wrap } from '../components/ui/styles'

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
    <main className={cn(wrap, 'py-10')}>
      <Panel>
        <Label>Something went wrong</Label>
        <Status tone="danger" role="alert">
          {error instanceof Error ? error.message : String(error)}
        </Status>
        <div>
          <Button onClick={() => void router.invalidate()}>Try again</Button>
        </div>
      </Panel>
    </main>
  )
}

function NotFound() {
  return (
    <main className={cn(wrap, 'py-10')}>
      <Panel>
        <Label>Not found</Label>
        <p>There is nothing at this address.</p>
        <div>
          <Link to="/" className={buttonClass()}>
            Home
          </Link>
        </div>
      </Panel>
    </main>
  )
}

declare module '@tanstack/react-router' {
  interface StaticDataRouteOption {
    /** The page fills the window below the app bar (the map) instead of the centred column. */
    fullBleed?: boolean
  }
}
