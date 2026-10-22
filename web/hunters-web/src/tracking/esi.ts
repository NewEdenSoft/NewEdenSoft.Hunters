import type { CharacterLocation, CharacterShip } from './types'

/** ESI's base address. ESI allows calls from any origin, with the headers sent here. */
export const ESI_BASE = 'https://esi.evetech.net'

/** The ESI API version to use; the same as the desktop's NewEdenKit.ESI, so both clients see the same data. */
export const ESI_COMPATIBILITY_DATE = '2026-08-18'

export type EsiCallStatus =
  | 'ok'
  /** ESI rejected the access token (401): ask the server for a new one. */
  | 'unauthorized'
  /** ESI refused the call (403), usually a scope the character has not granted. */
  | 'forbidden'
  /** Any other failure: ESI errors, error limits, the network. */
  | 'failed'

/** The outcome of one ESI call. `value` is set only when `status` is `ok`. */
export type EsiCall<T> = { status: 'ok'; value: T } | { status: Exclude<EsiCallStatus, 'ok'>; error: string }

/** The three ESI calls behind character tracking, all with the character's own access token. */
export interface LocationEsi {
  getOnline(characterId: number, accessToken: string, signal: AbortSignal): Promise<EsiCall<boolean>>
  getLocation(characterId: number, accessToken: string, signal: AbortSignal): Promise<EsiCall<CharacterLocation>>
  getShip(characterId: number, accessToken: string, signal: AbortSignal): Promise<EsiCall<CharacterShip>>
}

/** {@link LocationEsi} with fetch, straight from the browser. */
export const fetchLocationEsi: LocationEsi = {
  getOnline: (id, token, signal) =>
    call(`/characters/${id}/online`, token, signal, (v: { online: boolean }) => v.online),
  getLocation: (id, token, signal) =>
    call(`/characters/${id}/location`, token, signal, (v: { solar_system_id: number; station_id?: number; structure_id?: number }) => ({
      solarSystemId: v.solar_system_id,
      stationId: v.station_id ?? null,
      structureId: v.structure_id ?? null,
    })),
  getShip: (id, token, signal) =>
    call(`/characters/${id}/ship`, token, signal, (v: { ship_type_id: number; ship_name: string; ship_item_id: number }) => ({
      typeId: v.ship_type_id,
      name: v.ship_name,
      itemId: v.ship_item_id,
    })),
}

async function call<TBody, T>(path: string, accessToken: string, signal: AbortSignal, select: (body: TBody) => T): Promise<EsiCall<T>> {
  let response: Response
  try {
    response = await fetch(ESI_BASE + path, {
      headers: { Authorization: `Bearer ${accessToken}`, 'X-Compatibility-Date': ESI_COMPATIBILITY_DATE },
      signal,
    })
  } catch (error) {
    if (signal.aborted) throw error
    return { status: 'failed', error: `ESI could not be reached: ${(error as Error).message}` }
  }

  if (response.ok) return { status: 'ok', value: select((await response.json()) as TBody) }

  const message = await errorMessage(response)
  if (response.status === 401) return { status: 'unauthorized', error: message }
  if (response.status === 403) return { status: 'forbidden', error: message }
  return { status: 'failed', error: message }
}

async function errorMessage(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { error?: unknown }
    if (typeof body.error === 'string') return body.error
  } catch {
    // Not JSON.
  }
  return `ESI answered ${response.status}.`
}

/** Looks up names of EVE ids: solar systems, stations, types and so on. */
export type NameSource = (ids: number[], signal?: AbortSignal) => Promise<Map<number, string>>

/** ESI's public `POST /universe/names`. Throws when the lookup fails. */
export const fetchNames: NameSource = async (ids, signal) => {
  const response = await fetch(`${ESI_BASE}/universe/names`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Compatibility-Date': ESI_COMPATIBILITY_DATE },
    body: JSON.stringify(ids),
    signal,
  })
  if (!response.ok) throw new Error(`ESI name lookup failed (${response.status}).`)
  const names = (await response.json()) as { id: number; name: string }[]
  return new Map(names.map((n) => [n.id, n.name]))
}
