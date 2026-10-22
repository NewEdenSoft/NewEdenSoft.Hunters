import { createFileRoute, redirect } from '@tanstack/react-router'
import type { MapSearch } from './index'

/** The map moved to the home page; old links keep working, with their colour filter. */
export const Route = createFileRoute('/_authed/map')({
  validateSearch: (search: Record<string, unknown>): MapSearch => search as MapSearch,
  beforeLoad: ({ search }) => {
    throw redirect({ to: '/', search, replace: true })
  },
})
