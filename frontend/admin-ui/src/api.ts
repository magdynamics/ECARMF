// Tenant-aware API client. The platform serves multiple clients: every call
// carries the X-Tenant-Id header for the tenant selected in the UI.

const TENANT_KEY = 'ecarmf.tenantId'
const USER_KEY = 'ecarmf.userId'
const API_KEY = 'ecarmf.apiKey'

export function getTenant(): string {
  return localStorage.getItem(TENANT_KEY) ?? ''
}

export function setTenant(tenantId: string): void {
  localStorage.setItem(TENANT_KEY, tenantId)
}

export function getUser(): string {
  return localStorage.getItem(USER_KEY) ?? 'owner@platform'
}

export function setUser(userId: string): void {
  localStorage.setItem(USER_KEY, userId)
}

// Access-key sign-in: when a key is stored, it is the credential — the server
// derives tenant and identity from it and ignores asserted headers.
export function getApiKey(): string {
  return localStorage.getItem(API_KEY) ?? ''
}

export function setApiKey(key: string): void {
  if (key) localStorage.setItem(API_KEY, key)
  else localStorage.removeItem(API_KEY)
}

export class ApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(message: string, status: number, body: unknown) {
    super(message)
    this.status = status
    this.body = body
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const apiKey = getApiKey()
  const tenant = getTenant()
  if (!apiKey && !tenant) {
    throw new ApiError('Select a tenant first (or sign in with an access key).', 0, null)
  }

  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (apiKey) {
    headers['X-Api-Key'] = apiKey
  } else {
    headers['X-Tenant-Id'] = tenant
    headers['X-User-Id'] = getUser()
  }

  const response = await fetch(path, {
    ...init,
    headers: { ...headers, ...init?.headers },
  })

  const text = await response.text()
  const body = text ? JSON.parse(text) : null

  if (!response.ok) {
    const message =
      (body && (body.error ?? body.errors?.join('; '))) ??
      `${response.status} ${response.statusText}`
    throw new ApiError(message, response.status, body)
  }

  return body as T
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, payload?: unknown) =>
    request<T>(path, {
      method: 'POST',
      body: payload === undefined ? undefined : JSON.stringify(payload),
    }),
  put: <T>(path: string, payload?: unknown) =>
    request<T>(path, {
      method: 'PUT',
      body: payload === undefined ? undefined : JSON.stringify(payload),
    }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
}
