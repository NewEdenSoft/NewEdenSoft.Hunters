import type { NameSource } from './esi'

/**
 * Names of systems, stations and ship types, cached for the page's lifetime: they never change.
 * Player structures are not public, so their ids must not be passed (ESI rejects the whole request).
 */
export class EveNames {
  private readonly names = new Map<number, string>()
  private readonly source: NameSource

  constructor(source: NameSource) {
    this.source = source
  }

  get(id: number): string | undefined {
    return this.names.get(id)
  }

  /** Looks up the ids not cached yet, in one request. Returns false when the lookup failed; try again later. */
  async resolve(ids: Iterable<number>): Promise<boolean> {
    const missing = [...new Set(ids)].filter((id) => !this.names.has(id))
    if (missing.length === 0) return true
    try {
      for (const [id, name] of await this.source(missing)) this.names.set(id, name)
      return true
    } catch {
      return false
    }
  }
}
