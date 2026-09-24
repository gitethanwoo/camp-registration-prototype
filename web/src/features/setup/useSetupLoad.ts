import { ref, type Ref } from 'vue'
import { api, ApiError } from '@/lib/api'

/** Loads a setup endpoint and tells a 403 (not an admin) apart from other failures. */
export function useSetupLoad<T>(url: () => string | null) {
  const data = ref<T | null>(null) as Ref<T | null>
  const loading = ref(false)
  const forbidden = ref(false)
  const error = ref<string | null>(null)

  async function load() {
    const u = url()
    if (!u) return
    loading.value = true
    try {
      data.value = await api.get<T>(u)
      error.value = null
    } catch (e) {
      if (e instanceof ApiError && e.status === 403) forbidden.value = true
      else error.value = e instanceof ApiError ? e.message : "Couldn't load this page. Refresh to try again."
    } finally {
      loading.value = false
    }
  }

  return { data, loading, forbidden, error, load }
}

/** The message and field errors from a failed save, for inline display. */
export function saveError(e: unknown) {
  if (e instanceof ApiError) return { message: e.message, fields: e.errors }
  return { message: "That didn't save. Check your connection and try again.", fields: {} as Record<string, string[]> }
}

/** Dollars typed into a field, as cents. Empty or invalid input is NaN so the server rejects it with a message. */
export function toCents(dollars: string | number) {
  const n = typeof dollars === 'number' ? dollars : Number(String(dollars).replace(/[$,\s]/g, ''))
  return Number.isFinite(n) ? Math.round(n * 100) : Number.NaN
}

/** Cents as a plain number for an input field ("1450" or "292.5"). */
export function toDollars(cents: number) {
  return String(cents / 100)
}
