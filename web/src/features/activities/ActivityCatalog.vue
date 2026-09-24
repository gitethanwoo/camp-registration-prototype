<script setup lang="ts">
import { Plus, Search } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref } from 'vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import AdminOnly from '@/features/setup/AdminOnly.vue'
import PublishBadge from '@/features/setup/PublishBadge.vue'
import { useSetupLoad } from '@/features/setup/useSetupLoad'
import ActivitySheet from './ActivitySheet.vue'
import type { Catalog, CatalogActivity } from './types'

// K8 · Activity catalog (FR-23, 24, 81). Admins only; every create and edit is audited.
const { data, loading, forbidden, error, load } = useSetupLoad<Catalog>(() => '/admin/setup/activities')
onMounted(load)

const search = ref('')
const status = ref('all')
const category = ref('all')
const rows = computed(() => {
  const q = search.value.trim().toLowerCase()
  return (data.value?.activities ?? []).filter(
    (a) =>
      (!q || `${a.name} ${a.instructor} ${a.space}`.toLowerCase().includes(q)) &&
      (status.value === 'all' || (status.value === 'active') === a.isActive) &&
      (category.value === 'all' || a.category === category.value),
  )
})

const columns: ColumnDef<CatalogActivity>[] = [
  { accessorKey: 'name', header: 'Activity' },
  { accessorKey: 'gradeLabel', header: 'Grades', meta: { class: 'hidden sm:table-cell' } },
  {
    accessorKey: 'defaultCapacity',
    header: 'Capacity per period',
    meta: { class: 'hidden md:table-cell text-right', cellClass: 'text-right tabular-nums' },
  },
  { id: 'staff', header: 'Staff', meta: { class: 'hidden lg:table-cell' } },
  { accessorKey: 'space', header: 'Space', meta: { class: 'hidden lg:table-cell' } },
  { id: 'status', header: 'Status' },
]

const openId = ref<number | 'new' | null>(null)
const selected = computed(() =>
  typeof openId.value === 'number' ? (data.value?.activities.find((a) => a.id === openId.value) ?? null) : null,
)
async function saved(id?: number) {
  await load()
  openId.value = id ?? null
}
</script>

<template>
  <AdminOnly v-if="forbidden" page="The activity catalog" />
  <div v-else class="mx-auto max-w-6xl space-y-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Activity catalog</h1>
        <p class="text-muted-foreground">
          What campers can choose in Overnight Camp. Grade limits and capacity here set what families see and what the
          schedule allows.
        </p>
      </div>
      <Button @click="openId = 'new'"><Plus />New activity</Button>
    </div>

    <div class="flex flex-col gap-2 sm:flex-row">
      <div class="relative sm:w-64">
        <Search class="absolute top-2.5 left-2.5 size-4 text-muted-foreground" />
        <Input v-model="search" class="pl-8" placeholder="Search activities" aria-label="Search activities" />
      </div>
      <Select v-model="status">
        <SelectTrigger class="w-full sm:w-36" aria-label="Filter by status"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All statuses</SelectItem>
          <SelectItem value="active">Active</SelectItem>
          <SelectItem value="inactive">Inactive</SelectItem>
        </SelectContent>
      </Select>
      <Select v-model="category">
        <SelectTrigger class="w-full sm:w-44" aria-label="Filter by category"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All categories</SelectItem>
          <SelectItem v-for="c in data?.categories ?? []" :key="c" :value="c">{{ c }}</SelectItem>
        </SelectContent>
      </Select>
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
      empty-text="No activities match."
    >
      <template #cell-name="{ row: a }">
        <div class="flex items-center gap-3">
          <img :src="a.imageUrl" alt="" class="size-12 shrink-0 rounded-md object-cover" />
          <div class="min-w-0">
            <div class="font-medium">{{ a.name }}</div>
            <div class="text-sm text-muted-foreground">
              {{ a.category }}<span class="sm:hidden"> · {{ a.gradeLabel }}</span>
            </div>
          </div>
        </div>
      </template>
      <template #cell-staff="{ row: a }">
        <div>1 staff : {{ a.staffRatio }} campers</div>
        <div v-if="a.instructor" class="text-sm text-muted-foreground">{{ a.instructor }}</div>
      </template>
      <template #cell-status="{ row: a }">
        <PublishBadge :state="a.isActive ? 'Active' : 'Inactive'" />
      </template>
    </DataTable>
    <p class="text-sm text-muted-foreground">
      Showing {{ rows.length }} of {{ data?.activities.length ?? 0 }} activities. Periods:
      <template v-for="(p, i) in data?.periods ?? []" :key="p.period"
        >{{ i ? ', ' : '' }}{{ p.period }} ({{ p.time }})</template
      >.
    </p>

    <ActivitySheet :open="openId !== null" :activity="selected" :catalog="data" @close="openId = null" @saved="saved" />
  </div>
</template>
