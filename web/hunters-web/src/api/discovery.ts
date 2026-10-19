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

export async function getDiscoveryDocument(signal?: AbortSignal): Promise<DiscoveryDocument> {
  const response = await fetch('/.well-known/hunters', { signal })
  if (!response.ok) {
    throw new Error(`Discovery request failed: ${response.status}`)
  }
  return (await response.json()) as DiscoveryDocument
}
