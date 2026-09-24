<script setup lang="ts">
import { Globe, Search } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { api, ApiError } from '@/lib/api'
import { dateTime } from '@/lib/format'
import type { SearchResult, SearchRow } from './types'

// C1 · One search across every ministry. No ministry filter to pick first (FR-61).
const route = useRoute()
const router = useRouter()
const query = ref(typeof route.query.q === 'string' ? route.query.q : '')
const result = ref<SearchResult | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

const columns: ColumnDef<SearchRow>[] = [
  { accessorKey: 'name', header: 'Household', meta: { cellClass: 'align-top whitespace-normal' } },
  { id: 'members', header: 'Members', meta: { class: 'hidden md:table-cell', cellClass: 'align-top' } },
  { id: 'ministries', header: 'Ministry activity', meta: { class: 'hidden lg:table-cell', cellClass: 'align-top' } },
  { id: 'activity', header: 'Recent activity', meta: { class: 'hidden sm:table-cell', cellClass: 'align-top' } },
]

let timer: ReturnType<typeof setTimeout> | undefined
let latest = 0
async function run(term: string) {
  const q = term.trim()
  router.replace({ query: q ? { q } : {} })
  if (q.length < 2) {
    result.value = null
    error.value = null
    return
  }
  const ticket = ++latest
  loading.value = true
  try {
    const data = await api.get<SearchResult>(`/admin/search?q=${encodeURIComponent(q)}`)
    if (ticket === latest) {
      result.value = data
      error.value = null
    }
  } catch (e) {
    if (ticket === latest) error.value = e instanceof ApiError ? e.message : "Search didn't go through. Try again."
  } finally {
    if (ticket === latest) loading.value = false
  }
}
watch(query, (q) => {
  clearTimeout(timer)
  timer = setTimeout(() => run(q), 250)
})
onMounted(() => {
  if (query.value) run(query.value)
})

const open = (row: SearchRow) => router.push(`/admin/households/${row.id}`)
const adults = (r: SearchRow) => r.members.filter((m) => m.isAdult)
const children = (r: SearchRow) => r.members.filter((m) => !m.isAdult)
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div class="flex flex-wrap items-end justify-between gap-2">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Search families</h1>
        <p class="text-muted-foreground">Campers, guests and households in every ministry.</p>
      </div>
      <p class="flex items-center gap-1.5 text-sm text-muted-foreground">
        <Globe class="size-4" /> Searching across all ministries
      </p>
    </div>

    <form role="search" class="relative" @submit.prevent="run(query)">
      <Search class="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input
        v-model="query"
        type="search"
        autofocus
        aria-label="Search families"
        placeholder="Name, email, phone, or confirmation code"
        class="h-11 pl-9 text-base"
      />
    </form>

    <Alert v-if="error" variant="destructive" role="alert">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <Alert v-else-if="!result && !loading">
      <Search />
      <AlertTitle class="line-clamp-none">Search by any detail the caller gives you</AlertTitle>
      <AlertDescription
        >A last name, a child's name, an email, the last 4 digits of a phone number, or a confirmation code like
        WS-7A3L0E.</AlertDescription
      >
    </Alert>

    <section v-if="result || loading" class="space-y-3" aria-live="polite">
      <div v-if="result">
        <h2 class="font-semibold">{{ result.total }} {{ result.total === 1 ? 'household' : 'households' }}</h2>
        <p class="text-sm text-muted-foreground">
          {{
            result.total > result.rows.length
              ? `Showing the first ${result.rows.length}. Add more detail to narrow it.`
              : 'Select a household to open it.'
          }}
        </p>
      </div>
      <DataTable
        :columns="columns"
        :data="result?.rows"
        :loading="loading"
        :get-row-id="(r) => String(r.id)"
        :on-row-click="open"
        empty-text="No households match. Try an email, a phone number, or a child's name."
      >
        <template #cell-name="{ row: r }">
          <div class="font-medium">{{ r.name }} household</div>
          <div class="text-sm text-muted-foreground">{{ r.email }}</div>
          <div class="text-sm text-muted-foreground tabular-nums">
            {{ r.phone }}<template v-if="r.city"> · {{ r.city }}</template>
          </div>
          <div class="mt-1 text-sm text-muted-foreground md:hidden">
            {{ r.members.map((m) => m.name).join(', ') }}
          </div>
        </template>
        <template #cell-members="{ row: r }">
          <div class="text-sm">
            {{
              adults(r)
                .map((m) => m.name)
                .join(', ') || '—'
            }}
          </div>
          <div v-if="children(r).length" class="text-sm text-muted-foreground">
            Children:
            {{
              children(r)
                .map((m) => m.name.split(' ')[0])
                .join(', ')
            }}
          </div>
        </template>
        <template #cell-ministries="{ row: r }">
          <div class="flex flex-wrap gap-1">
            <Badge v-for="m in r.ministries" :key="m" variant="secondary" class="font-normal">{{ m }}</Badge>
            <span v-if="!r.ministries.length" class="text-sm text-muted-foreground">No registrations yet</span>
          </div>
        </template>
        <template #cell-activity="{ row: r }">
          <template v-if="r.recentActivity">
            <div class="text-sm">{{ r.recentActivity }}</div>
            <div class="text-xs text-muted-foreground">{{ dateTime(r.recentActivityAt!) }}</div>
          </template>
          <span v-else class="text-sm text-muted-foreground">—</span>
        </template>
      </DataTable>
    </section>
  </div>
</template>
