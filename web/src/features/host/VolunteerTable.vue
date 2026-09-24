<script setup lang="ts">
import { ChevronLeft, ChevronRight, Search } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, ref, watch } from 'vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { date } from '@/lib/format'
import type { VettingStatus, Volunteer, VolunteerList } from './types'
import VettingBadge from './VettingBadge.vue'

// The church's volunteers with search, vetting-status and role filters, 10 to a page.
const props = defineProps<{ list: VolunteerList | null }>()

const query = ref('')
const status = ref<'all' | VettingStatus>('all')
const role = ref('all')
const page = ref(0)
const pageSize = 10
const filtered = computed(() => {
  const q = query.value.trim().toLowerCase()
  return (props.list?.rows ?? []).filter(
    (v) =>
      (status.value === 'all' || v.vettingStatus === status.value) &&
      (role.value === 'all' || v.role === role.value) &&
      (!q || v.name.toLowerCase().includes(q) || v.email.toLowerCase().includes(q)),
  )
})
watch([query, status, role], () => (page.value = 0))
const pageCount = computed(() => Math.max(1, Math.ceil(filtered.value.length / pageSize)))
const pageRows = computed(() =>
  props.list ? filtered.value.slice(page.value * pageSize, (page.value + 1) * pageSize) : null,
)
const rangeLabel = computed(() => {
  const n = filtered.value.length
  if (!n) return '0 volunteers'
  const from = page.value * pageSize + 1
  return `${from}–${Math.min(from + pageSize - 1, n)} of ${n} ${n === 1 ? 'volunteer' : 'volunteers'}`
})

const columns: ColumnDef<Volunteer>[] = [
  { accessorKey: 'name', header: 'Name' },
  {
    accessorKey: 'email',
    header: 'Email',
    meta: { class: 'hidden sm:table-cell', cellClass: 'text-muted-foreground' },
  },
  {
    accessorKey: 'role',
    header: 'Role',
    meta: { class: 'hidden md:table-cell' },
  },
  { id: 'vettingStatus', header: 'Vetting status' },
  {
    id: 'createdAt',
    header: 'Added',
    meta: { class: 'hidden 2xl:table-cell', cellClass: 'text-muted-foreground' },
  },
]
const statuses: { value: VettingStatus; label: string }[] = [
  { value: 'Approved', label: 'Approved' },
  { value: 'InProgress', label: 'In progress' },
  { value: 'NotStarted', label: 'Not started' },
]
</script>

<template>
  <Card>
    <CardHeader class="space-y-3">
      <div>
        <CardTitle>Existing volunteers</CardTitle>
        <CardDescription v-if="list" data-testid="vetting-counts">
          {{ list.counts.approved }} approved · {{ list.counts.inProgress }} in progress ·
          {{ list.counts.notStarted }} not started
        </CardDescription>
      </div>
      <div class="grid gap-2 sm:grid-cols-[minmax(0,1fr)_11rem_10rem]">
        <div class="relative">
          <Search class="absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input v-model="query" placeholder="Search volunteers…" class="pl-8" aria-label="Search volunteers" />
        </div>
        <Select v-model="status">
          <SelectTrigger class="w-full" aria-label="Vetting status"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All vetting statuses</SelectItem>
            <SelectItem v-for="s in statuses" :key="s.value" :value="s.value">{{ s.label }}</SelectItem>
          </SelectContent>
        </Select>
        <Select v-model="role">
          <SelectTrigger class="w-full" aria-label="Role"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All roles</SelectItem>
            <SelectItem v-for="r in list?.roles ?? []" :key="r" :value="r">{{ r }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
    </CardHeader>
    <CardContent class="space-y-3">
      <DataTable
        :columns="columns"
        :data="pageRows"
        :get-row-id="(v) => String(v.id)"
        empty-text="No volunteers match."
      >
        <template #cell-name="{ row }">
          <span class="font-medium">{{ row.name }}</span>
          <span class="block text-xs font-normal text-muted-foreground sm:hidden">{{ row.email }}</span>
        </template>
        <template #cell-vettingStatus="{ row }"><VettingBadge :status="row.vettingStatus" /></template>
        <template #cell-createdAt="{ row }">{{ date(row.createdAt) }}</template>
      </DataTable>
      <div class="flex items-center justify-between gap-2 text-sm text-muted-foreground">
        <span data-testid="volunteer-range">{{ rangeLabel }}</span>
        <div class="flex items-center gap-1">
          <Button
            variant="outline"
            size="icon"
            class="size-8"
            :disabled="page === 0"
            aria-label="Previous page"
            @click="page--"
            ><ChevronLeft
          /></Button>
          <span class="px-2 tabular-nums">{{ page + 1 }} / {{ pageCount }}</span>
          <Button
            variant="outline"
            size="icon"
            class="size-8"
            :disabled="page >= pageCount - 1"
            aria-label="Next page"
            @click="page++"
            ><ChevronRight
          /></Button>
        </div>
      </div>
    </CardContent>
  </Card>
</template>
