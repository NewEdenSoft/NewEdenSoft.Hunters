import { afterEach, describe, expect, it, vi } from 'vitest'
import { ESI_BASE, ESI_COMPATIBILITY_DATE, fetchLocationEsi, fetchNames } from './esi'
import { EveNames } from './names'
import { NeedsLoginError, ServerTokenProvider, TOKEN_MARGIN_MS } from './tokens'

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('fetchLocationEsi', () => {
  const signal = new AbortController().signal

  it('calls ESI with the token and compatibility date, and maps the answer', async () => {
    const fetch = vi.fn().mockResolvedValue(json(200, { solar_system_id: 30000142, station_id: 60003760 }))
    vi.stubGlobal('fetch', fetch)

    const result = await fetchLocationEsi.getLocation(90000001, 'abc', signal)

    expect(result).toEqual({ status: 'ok', value: { solarSystemId: 30000142, stationId: 60003760, structureId: null } })
    const [url, init] = fetch.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(`${ESI_BASE}/characters/90000001/location`)
    expect(init.headers).toEqual({ Authorization: 'Bearer abc', 'X-Compatibility-Date': ESI_COMPATIBILITY_DATE })
  })

  it.each([
    [401, 'unauthorized'],
    [403, 'forbidden'],
    [420, 'failed'],
    [503, 'failed'],
  ])('maps %i to %s with ESI’s message', async (status, expected) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(status, { error: 'nope' })))

    expect(await fetchLocationEsi.getOnline(1, 'abc', signal)).toEqual({ status: expected, error: 'nope' })
  })

  it('reports a network failure as failed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    expect(await fetchLocationEsi.getShip(1, 'abc', signal)).toMatchObject({ status: 'failed' })
  })
})

describe('EveNames', () => {
  it('looks names up once and retries failures later', async () => {
    const source = vi.fn(async (ids: number[]) => new Map(ids.filter((id) => id !== 1).map((id) => [id, `name ${id}`])))
    const names = new EveNames(source)

    expect(await names.resolve([30000142, 12005])).toBe(true)
    expect(await names.resolve([30000142])).toBe(true)
    expect(names.get(30000142)).toBe('name 30000142')
    expect(source).toHaveBeenCalledTimes(1)

    source.mockRejectedValueOnce(new Error('ESI is down'))
    expect(await names.resolve([30000144])).toBe(false)
    expect(names.get(30000144)).toBeUndefined()
  })

  it('posts the ids to ESI', async () => {
    const fetch = vi.fn().mockResolvedValue(json(200, [{ id: 30000142, name: 'Jita', category: 'solar_system' }]))
    vi.stubGlobal('fetch', fetch)

    expect(await fetchNames([30000142])).toEqual(new Map([[30000142, 'Jita']]))
    expect(fetch.mock.calls[0][1]).toMatchObject({ method: 'POST', body: '[30000142]' })
  })
})

describe('ServerTokenProvider', () => {
  const expiresIn = (ms: number) => new Date(Date.now() + ms).toISOString()

  it('caches a token until a minute before it expires, sharing a request in flight', async () => {
    let now = Date.now()
    const fetch = vi.fn(async () => json(200, { characterId: 1, accessToken: `t${fetch.mock.calls.length}`, expiresAt: expiresIn(20 * 60_000) }))
    vi.stubGlobal('fetch', fetch)
    const tokens = new ServerTokenProvider(() => now)

    const [a, b] = await Promise.all([tokens.getAccessToken(1), tokens.getAccessToken(1)])
    expect([a, b]).toEqual(['t1', 't1'])
    expect(fetch).toHaveBeenCalledTimes(1)
    expect((fetch.mock.calls[0] as unknown[])[0]).toBe('/api/characters/1/access-token')

    now += 20 * 60_000 - TOKEN_MARGIN_MS - 1
    expect(await tokens.getAccessToken(1)).toBe('t1')
    now += 2
    expect(await tokens.getAccessToken(1)).toBe('t2')

    tokens.invalidate(1)
    expect(await tokens.getAccessToken(1)).toBe('t3')
  })

  it('turns the server’s needs_reauth into NeedsLoginError', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(409, { title: 'Log in with this character again.', code: 'needs_reauth' })))

    await expect(new ServerTokenProvider().getAccessToken(1)).rejects.toBeInstanceOf(NeedsLoginError)
  })
})
