import { computed, onMounted, ref } from 'vue'
import { useAdminScope } from '@/composables/useAdminScope'
import { api, ApiError } from '@/lib/api'
import { useSession } from '@/lib/session'

/** Loads one operations view for the selected session. `path` gets the session id, e.g. `(id) => ...`. */
export function useOpsData<T>(path: (sessionId: number) => string) {
  const { sessionId, ready } = useAdminScope()
  const data = ref<T | null>(null)
  const error = ref<string | null>(null)
  const loading = ref(false)
  // Finance can look; only the Customer Experience team and admins can change anything.
  const { session } = useSession()
  const canEdit = computed(() => ['cet', 'admin'].includes(session.value?.role ?? ''))

  async function load() {
    await ready
    const id = sessionId.value
    if (!id) {
      error.value = 'Choose a session in the menu to see its operations.'
      return
    }
    loading.value = true
    try {
      data.value = await api.get<T>(path(id))
      error.value = null
    } catch (e) {
      error.value = describe(e)
    } finally {
      loading.value = false
    }
  }
  onMounted(load)
  return { data, error, loading, load, sessionId, canEdit }
}

export function describe(e: unknown, fallback = 'Something went wrong. Try again.') {
  if (e instanceof ApiError) {
    if (e.status === 403) return 'Only the Customer Experience team and admins can change camp operations.'
    if (e.status === 404) return "That session wasn't found. Choose another session."
    return e.message || fallback
  }
  return fallback
}
