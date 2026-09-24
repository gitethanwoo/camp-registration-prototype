<script setup lang="ts">
import { CircleCheck, CircleX, LogIn, LogOut, Search } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { useMediaQuery } from '@vueuse/core'
import { computed, ref, watch } from 'vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { dateRange } from '@/lib/format'
import CheckInPanel from './CheckInPanel.vue'
import CheckInStatusBadge from './CheckInStatusBadge.vue'
import StatTile from './StatTile.vue'
import type { CheckInBoard, CheckInRow, CheckInStatus } from './types'
import { genderLabel } from './types'
import { useOpsData } from './useOpsData'

// O5 · Arrival and pickup desk. Built for a tablet: one big search box, then one camper at a time.
const { data: b, error, load, canEdit } = useOpsData<CheckInBoard>((id) => `/admin/ops/sessions/${id}/check-in`)
const wide = useMediaQuery('(min-width: 1024px)')

type Mode = 'in' | 'out'
const mode = ref<Mode>('in')
const search = ref('')
const page = ref(0)
const pageSize = 20
const selectedId = ref<number | null>(null)
const statusesFor: Record<Mode, CheckInStatus[]> = {
  in: ['Blocked', 'Ready', 'Checked in'],
  out: ['Checked in', 'Checked out'],
}

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  const allowed = statusesFor[mode.value]
  return (b.value?.rows ?? [])
    .filter((x) => allowed.includes(x.status))
    .filter((x) => !q || x.name.toLowerCase().includes(q) || x.confirmationCode.toLowerCase().includes(q))
})
watch([search, mode], () => {
  page.value = 0
})
const pages = computed(() => Math.max(1, Math.ceil(filtered.value.length / pageSize)))
const pageRows = computed(() => filtered.value.slice(page.value * pageSize, (page.value + 1) * pageSize))
const selected = computed(() => b.value?.rows.find((x) => x.registrationId === selectedId.value) ?? null)

// Enter on the search box opens the only match, like a scanner would.
function openFirst() {
  const first = filtered.value[0]
  if (first) selectedId.value = first.registrationId
}
function setMode(v: string | number) {
  mode.value = v === 'out' ? 'out' : 'in'
}

const columns: ColumnDef<CheckInRow>[] = [
  { id: 'camper', header: 'Camper' },
  { accessorKey: 'grade', header: 'Grade', meta: { class: 'hidden sm:table-cell' } },
  { accessorKey: 'cabin', header: 'Cabin', meta: { class: 'hidden md:table-cell' } },
  { id: 'status', header: 'Status' },
  { id: 'items', header: 'Required items', meta: { class: 'hidden xl:table-cell' } },
]
</script>

<template>
  <div class="mx-auto max-w-7xl space-y-6">
    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >
    <template v-else-if="!b"><Skeleton class="h-10 w-72" /><Skeleton class="h-16 rounded-xl" /></template>
    <template v-else>
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Check-in and check-out</h1>
        <p class="text-muted-foreground">
          {{ b.session.program }} · {{ b.session.name }} · {{ dateRange(b.session.startDate, b.session.endDate) }}
        </p>
      </div>

      <div class="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatTile label="Ready" :value="b.counts.ready" :icon="CircleCheck" tone="good" />
        <StatTile label="Blocked" :value="b.counts.blocked" :icon="CircleX" tone="warn" />
        <StatTile label="Checked in" :value="b.counts.checkedIn" :of="b.counts.registered" :icon="LogIn" />
        <StatTile label="Checked out" :value="b.counts.checkedOut" :icon="LogOut" />
      </div>

      <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_24rem]">
        <section class="min-w-0 space-y-4">
          <div class="relative">
            <Search class="absolute top-1/2 left-4 size-6 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <Input
              v-model="search"
              class="h-14 pl-14 text-lg md:text-lg"
              placeholder="Search a name, or scan the confirmation code"
              aria-label="Search a name, or scan the confirmation code"
              autofocus
              @keydown.enter.prevent="openFirst"
            />
          </div>
          <Tabs :model-value="mode" @update:model-value="setMode">
            <TabsList>
              <TabsTrigger value="in">Check-in</TabsTrigger>
              <TabsTrigger value="out">Check-out</TabsTrigger>
            </TabsList>
          </Tabs>
          <DataTable
            :columns="columns"
            :data="pageRows"
            :get-row-id="(x) => String(x.registrationId)"
            :on-row-click="(x) => (selectedId = x.registrationId)"
            :empty-text="search ? 'No camper matches that name or code.' : 'Nobody here yet.'"
          >
            <template #cell-camper="{ row: x }">
              <div class="font-medium">{{ x.name }}</div>
              <div class="text-sm text-muted-foreground">{{ genderLabel(x.gender) }} · {{ x.confirmationCode }}</div>
            </template>
            <template #cell-status="{ row: x }"><CheckInStatusBadge :status="x.status" /></template>
            <template #cell-items="{ row: x }">
              <span v-if="x.blockers.length" class="text-sm text-amber-800">
                {{ x.blockers.map((k) => k.label).join(' · ') }}
              </span>
              <span v-else class="text-sm text-muted-foreground">None</span>
            </template>
          </DataTable>
          <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
            <span class="text-muted-foreground"
              >{{ filtered.length }} {{ filtered.length === 1 ? 'camper' : 'campers' }}</span
            >
            <div class="flex items-center gap-2">
              <Button variant="outline" size="sm" :disabled="page === 0" @click="page--">Previous</Button>
              <span class="tabular-nums">Page {{ page + 1 }} of {{ pages }}</span>
              <Button variant="outline" size="sm" :disabled="page + 1 >= pages" @click="page++">Next</Button>
            </div>
          </div>
        </section>

        <aside v-if="wide" class="lg:sticky lg:top-4 lg:self-start">
          <Card>
            <CardContent>
              <CheckInPanel v-if="selected" :row="selected" :can-edit="canEdit" @changed="load" />
              <p v-else class="py-10 text-center text-muted-foreground">
                Search or pick a camper to check them in or out.
              </p>
            </CardContent>
          </Card>
        </aside>
      </div>
    </template>

    <Sheet v-if="!wide" :open="selected !== null" @update:open="(v) => !v && (selectedId = null)">
      <SheetContent side="bottom" class="max-h-[90dvh] overflow-y-auto">
        <SheetHeader class="sr-only">
          <SheetTitle>{{ selected?.name }}</SheetTitle>
          <SheetDescription>Check-in and check-out</SheetDescription>
        </SheetHeader>
        <div class="p-4">
          <CheckInPanel v-if="selected" :row="selected" :can-edit="canEdit" @changed="load" />
        </div>
      </SheetContent>
    </Sheet>
  </div>
</template>
