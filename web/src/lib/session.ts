import { computed, ref } from 'vue'

export interface Session {
  signedIn: boolean
  name?: string
  email?: string
  kind?: 'family' | 'staff'
  role?: string | null
}

const session = ref<Session | null>(null)
let pending: Promise<Session> | null = null

/** Who is signed in. Cached for the page's lifetime; pass `force` after sign-in state changes. */
export function loadSession(force = false): Promise<Session> {
  if (force || !pending) {
    pending = fetch('/api/auth/me')
      .then((r) => (r.ok ? (r.json() as Promise<Session>) : { signedIn: false }))
      .then((s) => (session.value = s))
  }
  return pending
}

/** Leaves the SPA for WorkOS AuthKit, which returns to `returnTo` once signed in. */
export function signIn(returnTo: string = location.pathname + location.search) {
  location.assign(`/api/auth/login?returnTo=${encodeURIComponent(returnTo)}`)
}

export async function signOut() {
  await fetch('/api/auth/logout', { method: 'POST' })
  session.value = { signedIn: false }
  pending = null
  location.assign('/programs')
}

export const roleLabels: Record<string, string> = {
  cet: 'Customer Experience',
  finance: 'Finance',
  host: 'Host coordinator',
  admin: 'Administrator',
}

export function useSession() {
  const initials = computed(() =>
    (session.value?.name ?? '')
      .split(' ')
      .map((w) => w[0] ?? '')
      .join('')
      .slice(0, 2)
      .toUpperCase(),
  )
  const roleLabel = computed(() => roleLabels[session.value?.role ?? ''] ?? '')
  return { session, initials, roleLabel, signIn, signOut }
}
