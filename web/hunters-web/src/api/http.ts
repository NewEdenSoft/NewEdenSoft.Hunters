/** A failed request to the Hunters server, with the problem response's `code` and `title` when it sent one. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(status: number, message: string, code: string | null = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }

  /** The session cookie is missing, expired or revoked. */
  get isUnauthorized(): boolean {
    return this.status === 401
  }
}

/**
 * Calls the Hunters server on this origin (the web app is not server agnostic). The session is
 * the cookie, so no token handling here. Non-2xx answers throw {@link ApiError}.
 */
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(path, { ...init, headers, credentials: 'same-origin' })
  if (!response.ok) {
    throw await toApiError(response)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

async function toApiError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as { title?: unknown; code?: unknown }
    return new ApiError(
      response.status,
      typeof problem.title === 'string' ? problem.title : `The server answered ${response.status}.`,
      typeof problem.code === 'string' ? problem.code : null,
    )
  } catch {
    return new ApiError(response.status, `The server answered ${response.status}.`)
  }
}
