import '@fontsource-variable/oxanium'
import '@fontsource-variable/saira'
import '@fontsource-variable/jetbrains-mono'
import { QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createRouter, RouterProvider } from '@tanstack/react-router'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { ApiError } from './api/http'
import { routeTree } from './routeTree.gen'
import { applyTheme, installTokens, savedThemeId } from './theme/themes'

// Apply the theme before the first render so there is no flash of the default colours.
installTokens()
applyTheme(savedThemeId(), false)

const queryClient = new QueryClient({
  queryCache: new QueryCache({
    // The session ended while the app was open (logged out elsewhere, revoked, account deleted):
    // forget cached data and go back to the login page. While a page is loading, the `_authed`
    // route's guard handles this instead.
    onError: (error) => {
      if (
        error instanceof ApiError &&
        error.isUnauthorized &&
        router.state.status === 'idle' &&
        router.state.location.pathname !== '/login'
      ) {
        queryClient.clear()
        void router.navigate({ to: '/login', search: { redirect: router.state.location.href } })
      }
    },
  }),
  defaultOptions: {
    queries: {
      // Client errors (401, 404, 409) do not get better by retrying.
      retry: (failureCount, error) => !(error instanceof ApiError && error.status < 500) && failureCount < 2,
    },
  },
})

const router = createRouter({
  routeTree,
  context: { queryClient },
  defaultPreload: 'intent',
  // Route loaders read through TanStack Query, which has its own caching.
  defaultPreloadStaleTime: 0,
  scrollRestoration: true,
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
