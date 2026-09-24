<script setup lang="ts">
import { Plus, Search } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref } from 'vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import AdminOnly from './AdminOnly.vue'
import ProgramSheet from './ProgramSheet.vue'
import PublishBadge from './PublishBadge.vue'
import type { ProgramList, ProgramRow, PublishState } from './types'
import { useSetupLoad } from './useSetupLoad'

// K2 · Programs: create, edit while in draft, and publish only through the approval chain (FR-44, FR-45).
const { data, loading, forbidden, error, load } = useSetupLoad<ProgramList>(() => '/admin/setup/programs')
onMounted(load)

type Filter = 'all' | PublishState
const filter = ref<Filter>('all')
const q = ref('')
const rows = computed(() =>
  (data.value?.rows ?? [])
    .filter((r) => filter.value === 'all' || r.state === filter.value)
    .filter(
      (r) => !q.value.trim() || `${r.name} ${r.ministry.name}`.toLowerCase().includes(q.value.trim().toLowerCase()),
    ),
)
const count = (s: PublishState) => data.value?.rows.filter((r) => r.state === s).length ?? 0

const columns: ColumnDef<ProgramRow>[] = [
  { accessorKey: 'name', header: 'Program' },
  { accessorKey: 'typeLabel', header: 'Type', meta: { class: 'hidden md:table-cell' } },
  { id: 'sessions', header: 'Sessions', meta: { class: 'hidden lg:table-cell' } },
  { id: 'waivers', header: 'Waivers', meta: { class: 'hidden lg:table-cell' } },
  { id: 'approval', header: 'Approval', meta: { class: 'hidden sm:table-cell' } },
  { accessorKey: 'state', header: 'Publish' },
]

// The sheet shows a row by id so it stays current after every reload.
const openId = ref<number | 'new' | null>(null)
const selected = computed(() =>
  typeof openId.value === 'number' ? (data.value?.rows.find((r) => r.id === openId.value) ?? null) : null,
)
async function saved(id?: number) {
  await load()
  if (id) openId.value = id
}
function approvals(r: ProgramRow) {
  const done = r.steps.filter((s) => s.approvedAt).length
  if (r.state === 'Published') return `${r.steps.length} of ${r.steps.length} approved`
  if (r.state === 'PendingApproval') return `${done} of ${r.steps.length} · next: ${r.nextStep}`
  return 'Not submitted'
}
</script>

<template>
  <AdminOnly v-if="forbidden" page="Programs" />
  <div v-else class="mx-auto max-w-6xl space-y-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Programs</h1>
        <p class="text-muted-foreground">
          Program details, sessions and waivers. A program reaches families only after its approval chain signs off.
        </p>
      </div>
      <Button @click="openId = 'new'"><Plus />New program</Button>
    </div>

    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <Tabs v-model="filter">
        <TabsList class="max-w-full overflow-x-auto">
          <TabsTrigger value="all">All ({{ data?.rows.length ?? '…' }})</TabsTrigger>
          <TabsTrigger value="Draft">Draft ({{ count('Draft') }})</TabsTrigger>
          <TabsTrigger value="PendingApproval">Pending ({{ count('PendingApproval') }})</TabsTrigger>
          <TabsTrigger value="Published">Published ({{ count('Published') }})</TabsTrigger>
        </TabsList>
      </Tabs>
      <div class="relative sm:w-64">
        <Search class="absolute top-2.5 left-2.5 size-4 text-muted-foreground" />
        <Input v-model="q" class="pl-8" placeholder="Search programs" aria-label="Search programs" />
      </div>
    </div>

    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >
    <DataTable
      v-else
      :columns="columns"
      :data="rows"
      :loading="loading && !data"
      :get-row-id="(r) => String(r.id)"
      :on-row-click="(r) => (openId = r.id)"
      empty-text="No programs match."
    >
      <template #cell-name="{ row: r }">
        <div class="font-medium">{{ r.name }}</div>
        <div class="text-sm text-muted-foreground">{{ r.ministry.name }}</div>
      </template>
      <template #cell-sessions="{ row: r }"> {{ r.sessions.length }} · {{ r.registered }} registered </template>
      <template #cell-waivers="{ row: r }">
        <span v-if="!r.waivers.length" class="text-muted-foreground">None</span>
        <ul v-else class="space-y-0.5">
          <li v-for="w in r.waivers" :key="w.id" class="max-w-56 truncate" :title="w.title">
            {{ w.title }} <span class="text-muted-foreground">v{{ w.version }}</span>
          </li>
        </ul>
      </template>
      <template #cell-approval="{ row: r }">
        <span class="text-sm">{{ approvals(r) }}</span>
      </template>
      <template #cell-state="{ row: r }"><PublishBadge :state="r.state" :label="r.stateLabel" /></template>
    </DataTable>

    <ProgramSheet
      :open="openId !== null"
      :program="selected"
      :ministries="data?.ministries ?? []"
      @close="openId = null"
      @saved="saved"
    />
  </div>
</template>
