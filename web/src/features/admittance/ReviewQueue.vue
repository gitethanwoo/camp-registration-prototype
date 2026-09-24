<script setup lang="ts">
import { Search } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref } from 'vue'
import { useAdminScope } from '@/composables/useAdminScope'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Progress } from '@/components/ui/progress'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { api } from '@/lib/api'
import { dateRange, dateTime, money } from '@/lib/format'
import ApplicationReader from './ApplicationReader.vue'
import PaymentBadge from './PaymentBadge.vue'
import { isPending, stageLabels, type Queue, type QueueRow, type StaffSession } from './types'

const { current, sessionId, select, ready } = useAdminScope()
const sessions = ref<StaffSession[] | null>(null)
const queue = ref<Queue | null>(null)
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    queue.value = await api.get<Queue>(`/admin/admittance/sessions/${sessionId.value}/applications`)
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  await ready
  sessions.value = await api.get<StaffSession[]>('/admin/admittance/sessions')
  // Only admittance sessions have applications. Jump the console scope to one if needed;
  // the layout re-mounts this page for the new session.
  if (current.value?.program.type !== 'Admittance') {
    const first = sessions.value[0]
    if (first) select(first.session.id)
    return
  }
  await load()
})

// ── Tabs and search ──
type Tab = 'review' | 'Approved' | 'Waitlisted' | 'Declined' | 'all'
const tab = ref<Tab>('review')
const q = ref('')
const tabs = computed(() => {
  const c = queue.value?.counts
  return [
    { value: 'review', label: 'Needs review', count: (c?.submitted ?? 0) + (c?.underReview ?? 0) },
    { value: 'Approved', label: 'Approved', count: c?.approved ?? 0 },
    { value: 'Waitlisted', label: 'Waitlisted', count: c?.waitlisted ?? 0 },
    { value: 'Declined', label: 'Declined', count: c?.declined ?? 0 },
    { value: 'all', label: 'All', count: c?.all ?? 0 },
  ] as const
})
const rows = computed(() => {
  if (!queue.value) return null
  const term = q.value.trim().toLowerCase()
  return queue.value.rows.filter(
    (r) =>
      (tab.value === 'all' || (tab.value === 'review' ? isPending(r.stage) : r.stage === tab.value)) &&
      (!term || r.couple.toLowerCase().includes(term) || r.email.toLowerCase().includes(term)),
  )
})

const columns: ColumnDef<QueueRow>[] = [
  { id: 'couple', header: 'Applicants', meta: { cellClass: 'max-w-64' } },
  { id: 'stage', header: 'Status', meta: { class: 'hidden sm:table-cell' } },
  { id: 'payment', header: 'Payment', meta: { class: 'hidden md:table-cell' } },
  {
    id: 'applied',
    header: 'Applied',
    cell: ({ row }) => (row.original.submittedAt ? dateTime(row.original.submittedAt) : '—'),
    meta: { class: 'hidden lg:table-cell', cellClass: 'whitespace-nowrap text-muted-foreground' },
  },
  {
    id: 'activity',
    header: 'Last activity',
    cell: ({ row }) => dateTime(row.original.lastActivity),
    meta: { class: 'hidden xl:table-cell', cellClass: 'whitespace-nowrap text-muted-foreground' },
  },
  { id: 'actions', header: '', meta: { class: 'w-20 text-right' } },
]

// ── Reader ──
const openId = ref<number | null>(null)
const readerOpen = ref(false)
function openRow(r: QueueRow) {
  openId.value = r.id
  readerOpen.value = true
}

const filled = computed(() => (queue.value ? Math.round((queue.value.reserved / queue.value.capacity) * 100) : 0))
</script>

<template>
  <div class="space-y-6">
    <Card v-if="sessions && !sessions.length">
      <CardContent class="py-10 text-center text-muted-foreground">
        No programs take applications right now.
      </CardContent>
    </Card>

    <template v-else>
      <div class="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
        <div>
          <h1 class="text-2xl font-semibold tracking-tight">Applications</h1>
          <p v-if="queue" class="text-sm text-muted-foreground">
            {{ queue.session.program.name }} · {{ queue.session.name }} ·
            {{ dateRange(queue.session.startDate, queue.session.endDate) }} · {{ money(queue.session.priceCents) }} per
            couple
          </p>
        </div>
        <div v-if="queue" class="w-full space-y-1 md:w-72">
          <div class="flex justify-between text-sm">
            <span>{{ queue.reserved }} of {{ queue.capacity }} couples confirmed</span>
            <span :class="queue.remaining === 0 ? 'font-medium text-destructive' : 'text-muted-foreground'">{{
              queue.remaining === 0 ? 'Full' : `${queue.remaining} left`
            }}</span>
          </div>
          <Progress :model-value="filled" :aria-label="`${filled}% of spots filled`" />
        </div>
      </div>

      <div class="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        <Tabs v-model="tab" class="min-w-0">
          <TabsList class="h-auto max-w-full flex-wrap justify-start">
            <TabsTrigger v-for="t in tabs" :key="t.value" :value="t.value" class="gap-1.5">
              {{ t.label }}
              <Badge variant="secondary" class="px-1.5 tabular-nums">{{ t.count }}</Badge>
            </TabsTrigger>
          </TabsList>
        </Tabs>
        <div class="relative w-full lg:w-72">
          <Search class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input v-model="q" placeholder="Search name or email" class="pl-8" aria-label="Search applications" />
        </div>
      </div>

      <DataTable
        :columns="columns"
        :data="rows"
        :loading="loading"
        :get-row-id="(r) => String(r.id)"
        :on-row-click="openRow"
        :empty-text="q ? 'No applications match that search.' : 'Nothing here right now.'"
      >
        <template #cell-couple="{ row }">
          <p class="truncate font-medium">{{ row.couple }}</p>
          <p class="truncate text-xs text-muted-foreground">{{ row.email }}</p>
          <div class="mt-1 flex flex-wrap gap-1 sm:hidden">
            <Badge variant="secondary">{{ stageLabels[row.stage] }}</Badge>
            <PaymentBadge :state="row.paymentState" />
          </div>
        </template>
        <template #cell-stage="{ row }">
          <Badge variant="secondary">{{ stageLabels[row.stage] }}</Badge>
        </template>
        <template #cell-payment="{ row }">
          <PaymentBadge :state="row.paymentState" />
        </template>
        <template #cell-actions="{ row }">
          <Button variant="outline" size="sm" @click.stop="openRow(row)">Review</Button>
        </template>
      </DataTable>
    </template>

    <ApplicationReader v-model:open="readerOpen" :id="openId" @changed="load" />
  </div>
</template>
