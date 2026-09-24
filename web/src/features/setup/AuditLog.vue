<script setup lang="ts">
import { ChevronLeft, ChevronRight, Download, Lock, Search } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { refDebounced } from '@vueuse/core'
import { computed, onMounted, ref, watch } from 'vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import { dateTime } from '@/lib/format'
import type { AuditEntry, AuditPage, AuditRow } from './types'
import { useSetupLoad } from './useSetupLoad'

// K12 · Audit log (FR-81). Every staff member can read it; nobody can change or delete a row.
const category = ref('all')
const actor = ref('all')
const from = ref('')
const to = ref('')
const q = ref('')
const qDebounced = refDebounced(q, 300)
const page = ref(1)

const query = computed(() => {
  const p = new URLSearchParams()
  if (category.value !== 'all') p.set('category', category.value)
  if (actor.value !== 'all') p.set('actor', actor.value)
  if (from.value) p.set('from', from.value)
  if (to.value) p.set('to', to.value)
  if (qDebounced.value.trim()) p.set('q', qDebounced.value.trim())
  return p
})
const { data, loading, error, load } = useSetupLoad<AuditPage>(() => {
  const p = new URLSearchParams(query.value)
  p.set('page', String(page.value))
  return `/admin/audit-log?${p}`
})
const exportUrl = computed(() => `/api/admin/audit-log/export?${query.value}`)
const filtered = computed(() => [...query.value.keys()].length > 0)
onMounted(load)
// A filter change goes back to page 1; the page watcher loads it, so load here only when already there.
watch(query, () => {
  if (page.value === 1) load()
  else page.value = 1
})
watch(page, load)
function clear() {
  category.value = 'all'
  actor.value = 'all'
  from.value = ''
  to.value = ''
  q.value = ''
}

const columns: ColumnDef<AuditRow>[] = [
  {
    accessorKey: 'createdAt',
    header: 'When',
    meta: { class: 'hidden w-40 sm:table-cell', cellClass: 'whitespace-nowrap text-muted-foreground' },
  },
  { accessorKey: 'actor', header: 'Who', meta: { class: 'hidden md:table-cell' } },
  { accessorKey: 'what', header: 'What happened', meta: { cellClass: 'whitespace-normal' } },
  { accessorKey: 'category', header: 'Area', meta: { class: 'hidden lg:table-cell' } },
]

const areaLabel = (c: string) => data.value?.categories.find((x) => x.value === c)?.label ?? c

const openId = ref<number | null>(null)
const entry = ref<AuditEntry | null>(null)
const entryError = ref<string | null>(null)
watch(openId, async (id) => {
  entry.value = null
  entryError.value = null
  if (id == null) return
  try {
    entry.value = await api.get<AuditEntry>(`/admin/audit-log/${id}`)
  } catch (e) {
    entryError.value = e instanceof ApiError ? e.message : "Couldn't load this entry."
  }
})
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Audit log</h1>
        <p class="text-muted-foreground">
          Who changed what, and when. Covers setup, registrations, payments and approvals.
        </p>
      </div>
      <Button variant="outline" as-child>
        <a :href="exportUrl" download><Download />Export CSV</a>
      </Button>
    </div>

    <Alert>
      <Lock />
      <AlertTitle class="line-clamp-none">This log can't be edited</AlertTitle>
      <AlertDescription
        >Entries are written when a change is saved. No one, including admins, can change or delete
        them.</AlertDescription
      >
    </Alert>

    <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-[1fr_12rem_12rem_9.5rem_9.5rem]">
      <div class="space-y-1 sm:col-span-2 lg:col-span-1">
        <Label for="a-q">Search</Label>
        <div class="relative">
          <Search class="absolute top-2.5 left-2.5 size-4 text-muted-foreground" aria-hidden="true" />
          <Input id="a-q" v-model="q" type="search" class="pl-8" placeholder="Code, program, record ID…" />
        </div>
      </div>
      <div class="space-y-1">
        <Label for="a-cat">Area</Label>
        <Select v-model="category">
          <SelectTrigger id="a-cat" class="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All areas</SelectItem>
            <SelectItem v-for="c in data?.categories ?? []" :key="c.value" :value="c.value">{{ c.label }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="space-y-1">
        <Label for="a-actor">Who</Label>
        <Select v-model="actor">
          <SelectTrigger id="a-actor" class="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Anyone</SelectItem>
            <SelectItem v-for="a in data?.actors ?? []" :key="a" :value="a">{{ a }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="space-y-1">
        <Label for="a-from">From</Label>
        <Input id="a-from" v-model="from" type="date" />
      </div>
      <div class="space-y-1">
        <Label for="a-to">To</Label>
        <Input id="a-to" v-model="to" type="date" />
      </div>
    </div>

    <div class="flex flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground">
      <p aria-live="polite">
        {{ data ? `${data.total.toLocaleString()} ${data.total === 1 ? 'entry' : 'entries'}` : 'Loading…' }}
      </p>
      <Button v-if="filtered" variant="ghost" size="sm" @click="clear">Clear filters</Button>
    </div>

    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >
    <DataTable
      v-else
      :columns="columns"
      :data="data?.rows"
      :loading="loading"
      :get-row-id="(r) => String(r.id)"
      :on-row-click="(r) => (openId = r.id)"
      :empty-text="filtered ? 'Nothing matches these filters.' : 'Nothing has been logged yet.'"
    >
      <template #cell-createdAt="{ row: r }">{{ dateTime(r.createdAt) }}</template>
      <template #cell-what="{ row: r }">
        <div class="font-medium">{{ r.what }}</div>
        <div class="text-sm text-muted-foreground md:hidden">
          {{ r.actor }}<span class="sm:hidden"> · {{ dateTime(r.createdAt) }}</span>
        </div>
        <div v-if="r.detail" class="line-clamp-2 text-sm text-muted-foreground">{{ r.detail }}</div>
      </template>
      <template #cell-category="{ row: r }"
        ><Badge variant="secondary">{{ areaLabel(r.category) }}</Badge></template
      >
    </DataTable>

    <div v-if="data && data.pages > 1" class="flex items-center justify-end gap-2">
      <Button variant="outline" size="sm" :disabled="page <= 1 || loading" @click="page--"><ChevronLeft />Newer</Button>
      <span class="text-sm text-muted-foreground">Page {{ data.page }} of {{ data.pages }}</span>
      <Button variant="outline" size="sm" :disabled="page >= data.pages || loading" @click="page++"
        >Older<ChevronRight
      /></Button>
    </div>

    <Sheet :open="openId !== null" @update:open="(v) => !v && (openId = null)">
      <SheetContent class="w-full overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>{{ entry?.what ?? 'Audit entry' }}</SheetTitle>
          <SheetDescription v-if="entry">{{ entry.actor }} · {{ dateTime(entry.createdAt) }}</SheetDescription>
        </SheetHeader>
        <div class="space-y-5 px-4 pb-6 text-sm">
          <p v-if="entryError" class="text-destructive" role="alert">{{ entryError }}</p>
          <Skeleton v-else-if="!entry" class="h-40" />
          <template v-else>
            <dl class="grid grid-cols-[7rem_1fr] gap-x-3 gap-y-2">
              <dt class="text-muted-foreground">Action</dt>
              <dd class="font-mono text-xs">{{ entry.action }}</dd>
              <dt class="text-muted-foreground">Record</dt>
              <dd>
                {{ entry.entityType
                }}<template v-if="entry.entityId && entry.entityId !== '-'"> #{{ entry.entityId }}</template>
              </dd>
              <dt v-if="entry.detail" class="text-muted-foreground">Detail</dt>
              <dd v-if="entry.detail">{{ entry.detail }}</dd>
            </dl>
            <div v-if="entry.changes.length" class="space-y-2">
              <h3 class="font-medium">Changes</h3>
              <ul class="divide-y rounded-lg border">
                <li v-for="c in entry.changes" :key="c.field" class="space-y-1 p-3">
                  <p class="font-medium">{{ c.field }}</p>
                  <p v-if="c.before == null">Set to {{ c.after ?? 'nothing' }}</p>
                  <p v-else-if="c.after == null" class="text-muted-foreground line-through">{{ c.before }}</p>
                  <p v-else class="flex flex-wrap items-center gap-2">
                    <span class="text-muted-foreground line-through">{{ c.before }}</span>
                    <ChevronRight class="size-3.5 text-muted-foreground" aria-label="changed to" />
                    <span>{{ c.after }}</span>
                  </p>
                </li>
              </ul>
            </div>
            <p v-else class="text-muted-foreground">No field-level changes were recorded for this entry.</p>
          </template>
        </div>
      </SheetContent>
    </Sheet>
  </div>
</template>
