// Tenant-aware API client. The platform serves multiple clients: every call
// carries the X-Tenant-Id header for the tenant selected in the UI.

const TENANT_KEY = 'ecarmf.tenantId'

export function getTenant(): string {
  return localStorage.getItem(TENANT_KEY) ?? ''
}

export function setTenant(tenantId: string): void {
  localStorage.setItem(TENANT_KEY, tenantId)
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
  const tenant = getTenant()
  if (!tenant) {
    throw new ApiError('Select a tenant first.', 0, null)
  }

  const response = await fetch(path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-Tenant-Id': tenant,
      ...init?.headers,
    },
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
}
