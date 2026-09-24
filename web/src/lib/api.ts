import { signIn } from '@/lib/session'

export class ApiError extends Error {
  status: number
  errors: Record<string, string[]>
  body: unknown

  constructor(status: number, message: string, errors: Record<string, string[]> = {}, body: unknown = null) {
    super(message)
    this.status = status
    this.errors = errors
    this.body = body
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(`/api${url}`, {
    method,
    headers: body ? { 'content-type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  })
  // Session expired or never started: send the browser through sign-in and back here.
  if (res.status === 401) signIn()
  const text = await res.text()
  const data = text ? JSON.parse(text) : null
  if (!res.ok) {
    const errors = (data?.errors ?? {}) as Record<string, string[]>
    const message =
      data?.error ?? data?.message ?? Object.values(errors).flat()[0] ?? data?.title ?? `Request failed (${res.status})`
    throw new ApiError(res.status, message, errors, data)
  }
  return data as T
}

export const api = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: <T>(url: string, body?: unknown) => request<T>('PUT', url, body ?? {}),
  patch: <T>(url: string, body?: unknown) => request<T>('PATCH', url, body ?? {}),
  delete: <T>(url: string) => request<T>('DELETE', url),
}
