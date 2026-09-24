<script setup lang="ts">
import { RefreshCw, Search, ShieldCheck } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { api } from '@/lib/api'
import { dateTime } from '@/lib/format'
import AdminOnly from '@/features/setup/AdminOnly.vue'
import { saveError, useSetupLoad } from '@/features/setup/useSetupLoad'
import AccessBadge from './AccessBadge.vue'
import StaffSheet from './StaffSheet.vue'
import type { StaffList, StaffRow, SyncRun } from './types'
import { actorName, ago } from './when'

// K11 · Staff users and roles (FR-2, FR-6). Who is staff comes from WorkOS (standing in for Entra);
// this page sets ministry scope and health-data access, and shows who was revoked.
const { data, loading, forbidden, error, load } = useSetupLoad<StaffList>(() => '/access/staff')

const syncing = ref(false)
const syncError = ref<string | null>(null)
async function sync(quiet = false) {
  syncing.value = true
  syncError.value = null
  try {
    const run = await api.post<SyncRun>('/access/staff/sync')
    await load()
    // The automatic sync on opening the page speaks up only when it changed someone's access.
    if (!quiet || run.added || run.updated || run.revoked) toast.success(syncSummary(run))
  } catch (e) {
    syncError.value = saveError(e).message
  } finally {
    syncing.value = false
  }
}
function syncSummary(run: SyncRun) {
  const parts = [`Synced ${run.members} staff from WorkOS.`]
  if (run.added) parts.push(`${run.added} added.`)
  if (run.updated) parts.push(`${run.updated} updated.`)
  if (run.revoked) parts.push(`${run.revoked} revoked.`)
  if (!run.added && !run.updated && !run.revoked) parts.push('No changes.')
  return parts.join(' ')
}

onMounted(async () => {
  await load()
  // "Revoked automatically on the next sync": opening the page syncs when the last one is stale.
  if (data.value?.syncDue) await sync(true)
})

type StatusFilter = 'Active' | 'Revoked' | 'all'
const status = ref<StatusFilter>('Active')
const ministry = ref('all')
const role = ref('all')
const q = ref('')
const rows = computed(() => {
  const term = q.value.trim().toLowerCase()
  return (data.value?.rows ?? [])
    .filter((r) => status.value === 'all' || r.status === status.value)
    .filter((r) => ministry.value === 'all' || String(r.ministry?.id ?? 'none') === ministry.value)
    .filter((r) => role.value === 'all' || r.role === role.value)
    .filter((r) => !term || `${r.name} ${r.email}`.toLowerCase().includes(term))
})
const count = (s: 'Active' | 'Revoked') => data.value?.rows.filter((r) => r.status === s).length ?? 0
const roles = computed(() => {
  const seen = new Map<string, string>()
  for (const r of data.value?.rows ?? []) seen.set(r.role, r.roleLabel)
  return [...seen].map(([slug, label]) => ({ slug, label })).toSorted((a, b) => a.label.localeCompare(b.label))
})

const columns: ColumnDef<StaffRow>[] = [
  { accessorKey: 'name', header: 'Name' },
  { id: 'sync', header: 'WorkOS sync', meta: { class: 'hidden xl:table-cell' } },
  { accessorKey: 'roleLabel', header: 'Role', meta: { class: 'hidden md:table-cell' } },
  { id: 'scope', header: 'Ministry scope', meta: { class: 'hidden lg:table-cell' } },
  { id: 'health', header: 'Health data', meta: { class: 'hidden sm:table-cell' } },
  { id: 'signIn', header: 'Last sign-in', meta: { class: 'hidden lg:table-cell' } },
  { accessorKey: 'status', header: 'Status' },
]

const openId = ref<number | null>(null)
</script>

<template>
  <AdminOnly v-if="forbidden" page="Staff access" />
  <div v-else class="mx-auto max-w-6xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Staff access</h1>
      <p class="text-muted-foreground">
        Who can use the WinShape console and whether they can read camper health details. Ministry scope limits health
        details to one ministry's programs; it doesn't yet limit registrations, households or waitlists.
      </p>
    </div>

    <Alert>
      <ShieldCheck />
      <AlertTitle class="line-clamp-none"
        >Staff access is managed in WorkOS, standing in for Microsoft Entra</AlertTitle
      >
      <AlertDescription>
        <p>
          Add or remove people and change roles in the WinShape Staff organization. This page picks up the change on the
          next sync, and anyone removed shows as Revoked. There are no passwords here.
        </p>
        <div class="mt-2 flex flex-wrap items-center gap-x-3 gap-y-2">
          <span v-if="data?.lastSync" class="text-foreground" data-testid="last-sync">
            Last synced {{ ago(data.lastSync.ranAt) }} by {{ actorName(data.lastSync.actor) }} ·
            {{ data.lastSync.members }} in WorkOS
          </span>
          <span v-else-if="data" class="text-foreground">Not synced yet.</span>
          <Button size="sm" variant="outline" class="text-foreground" :disabled="syncing || !data" @click="sync()">
            <RefreshCw :class="syncing && 'animate-spin'" />{{ syncing ? 'Syncing…' : 'Sync now' }}
          </Button>
        </div>
        <p v-if="syncError" class="mt-2 text-destructive" role="alert">{{ syncError }}</p>
      </AlertDescription>
    </Alert>

    <div class="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
      <Tabs v-model="status">
        <TabsList>
          <TabsTrigger value="Active">Active ({{ count('Active') }})</TabsTrigger>
          <TabsTrigger value="Revoked">Revoked ({{ count('Revoked') }})</TabsTrigger>
          <TabsTrigger value="all">All ({{ data?.rows.length ?? '…' }})</TabsTrigger>
        </TabsList>
      </Tabs>
      <div class="grid gap-2 sm:grid-cols-3 lg:flex">
        <div class="relative lg:w-56">
          <Search class="absolute top-2.5 left-2.5 size-4 text-muted-foreground" />
          <Input v-model="q" class="pl-8" placeholder="Search staff" aria-label="Search staff" />
        </div>
        <Select v-model="ministry">
          <SelectTrigger class="w-full lg:w-44" aria-label="Filter by ministry scope"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All scopes</SelectItem>
            <SelectItem value="none">Unscoped (all ministries)</SelectItem>
            <SelectItem v-for="m in data?.ministries ?? []" :key="m.id" :value="String(m.id)">{{ m.name }}</SelectItem>
          </SelectContent>
        </Select>
        <Select v-model="role">
          <SelectTrigger class="w-full lg:w-48" aria-label="Filter by role"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All roles</SelectItem>
            <SelectItem v-for="r in roles" :key="r.slug" :value="r.slug">{{ r.label }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>

    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >
    <template v-else>
      <p class="text-sm text-muted-foreground" aria-live="polite">
        {{ rows.length }} {{ rows.length === 1 ? 'person' : 'people' }}
      </p>
      <DataTable
        :columns="columns"
        :data="data ? rows : null"
        :loading="loading || syncing"
        :get-row-id="(r) => String(r.id)"
        :on-row-click="(r) => (openId = r.id)"
        empty-text="No staff match these filters."
      >
        <template #cell-name="{ row: r }">
          <div class="font-medium">{{ r.name }}</div>
          <div class="text-sm break-all text-muted-foreground">{{ r.email }}</div>
          <div class="mt-1 text-sm text-muted-foreground md:hidden">
            {{ r.roleLabel }} · {{ r.ministry?.name ?? 'All ministries' }}
          </div>
          <AccessBadge class="mt-1 sm:hidden" :on="r.healthAccess" :muted="r.status === 'Revoked'" />
        </template>
        <template #cell-sync="{ row: r }">
          <template v-if="r.status === 'Revoked'">
            <div class="font-medium">Not in organization</div>
            <div class="text-sm text-muted-foreground">Removed {{ r.revokedAt ? dateTime(r.revokedAt) : '' }}</div>
          </template>
          <template v-else-if="r.syncedAt">
            <div>Synced</div>
            <div class="text-sm text-muted-foreground">{{ ago(r.syncedAt) }}</div>
          </template>
          <span v-else class="text-muted-foreground">Pending first sync</span>
        </template>
        <template #cell-scope="{ row: r }">{{ r.ministry?.name ?? 'All ministries' }}</template>
        <template #cell-health="{ row: r }">
          <AccessBadge :on="r.healthAccess" :muted="r.status === 'Revoked'" />
        </template>
        <template #cell-signIn="{ row: r }">
          <span v-if="r.lastSignInAt">{{ dateTime(r.lastSignInAt) }}</span>
          <span v-else class="text-muted-foreground">Never</span>
        </template>
        <template #cell-status="{ row: r }">
          <Badge
            variant="outline"
            :class="
              r.status === 'Active'
                ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
                : 'border-transparent bg-muted text-muted-foreground'
            "
            >{{ r.status }}</Badge
          >
        </template>
      </DataTable>
    </template>

    <StaffSheet :id="openId" :ministries="data?.ministries ?? []" @close="openId = null" @saved="load" />
  </div>
</template>
