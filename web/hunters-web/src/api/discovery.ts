import { queryOptions } from '@tanstack/react-query'
import { api } from './http'

// Mirrors NewEdenSoft.Hunters.Contracts.Discovery.DiscoveryDocument.
// Will be replaced by types generated from the server's OpenAPI document.
export interface DiscoveryDocument {
  name: string
  apiVersion: string
  minClientVersion: string
  admission: { requiresAccessKey: boolean }
  mapPack: { sdeBuild: number } | null
  features: string[]
}

/** Public server information; also tells the login page whether an access key may be needed. */
export const discoveryQuery = queryOptions({
  queryKey: ['discovery'],
  queryFn: ({ signal }) => api<DiscoveryDocument>('/.well-known/hunters', { signal }),
  staleTime: 5 * 60_000,
})
