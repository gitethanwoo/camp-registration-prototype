<script setup lang="ts">
import { CircleCheck, CircleX, FileText, HandHeart, Hourglass, TriangleAlert } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref, watch } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Separator } from '@/components/ui/separator'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, dateTime, money } from '@/lib/format'
import StatTile from './StatTile.vue'
import StatusBadge from './StatusBadge.vue'
import type { ScholarshipDetail, ScholarshipQueue, ScholarshipRow } from './types'

// O7 · Staff review of scholarship applications. The award is written to the registrations as a discount on the
// server, which refuses any award that would take scholarships plus discounts past 100% (FR-091).
type Tab = 'submitted' | 'approved' | 'denied' | 'all'
const tab = ref<Tab>('submitted')
const queue = ref<ScholarshipQueue | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)

async function load() {
  loading.value = true
  try {
    queue.value = await api.get<ScholarshipQueue>(`/admin/scholarships?status=${tab.value}`)
    loadError.value = null
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : "Couldn't load applications."
  } finally {
    loading.value = false
  }
}
onMounted(load)
watch(tab, load)

const columns: ColumnDef<ScholarshipRow>[] = [
  { id: 'family', header: 'Family' },
  { id: 'program', header: 'Program', meta: { class: 'hidden md:table-cell' } },
  { id: 'submitted', header: 'Submitted', meta: { class: 'hidden lg:table-cell', cellClass: 'whitespace-nowrap' } },
  { id: 'requested', header: 'Requested', meta: { class: 'text-right', cellClass: 'text-right tabular-nums' } },
  { id: 'status', header: 'Status' },
]
const label = (s: string) => (s === 'Submitted' ? 'Needs review' : s)

// ── Review sheet ──
const selected = ref<ScholarshipRow | null>(null)
const detail = ref<ScholarshipDetail | null>(null)
const award = ref('')
const note = ref('')
const busy = ref(false)
const actionError = ref<string | null>(null)

async function open(row: ScholarshipRow) {
  selected.value = row
  detail.value = null
  actionError.value = null
  note.value = ''
  try {
    const d = await api.get<ScholarshipDetail>(`/admin/scholarships/${row.id}`)
    detail.value = d
    award.value = String((d.status === 'Approved' ? d.awardCents : Math.min(d.requestedCents, d.maxAwardCents)) / 100)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : "Couldn't load this application."
  }
}

const awardCents = computed(() => {
  const n = Number(award.value)
  return Number.isFinite(n) && n > 0 ? Math.round(n * 100) : 0
})
// Mirrors the server's checks so staff see the problem before they press Approve.
const capProblem = computed(() => {
  const d = detail.value
  if (!d || !awardCents.value) return null
  if (awardCents.value > d.eligibleCents)
    return `Cannot approve: award ${money(awardCents.value)} exceeds ${money(d.eligibleCents)} eligible total; scholarships plus discounts cannot exceed 100%.`
  if (awardCents.value > d.owedCents)
    return `Cannot approve: award ${money(awardCents.value)} is more than the ${money(d.owedCents)} still owed.`
  return null
})
const balanceAfter = computed(() => (detail.value ? Math.max(0, detail.value.owedCents - awardCents.value) : 0))

async function decide(kind: 'approve' | 'deny') {
  const row = selected.value
  if (!row) return
  busy.value = true
  actionError.value = null
  try {
    if (kind === 'approve') {
      const r = await api.post<{ awardCents: number; balanceCents: number }>(`/admin/scholarships/${row.id}/approve`, {
        awardCents: awardCents.value,
        note: note.value.trim() || null,
      })
      toast.success(`Approved ${money(r.awardCents)} for the ${row.household} family.`, {
        description: `Their balance is now ${money(r.balanceCents)}.`,
      })
    } else {
      await api.post(`/admin/scholarships/${row.id}/deny`, { note: note.value.trim() })
      toast.success(`Application from the ${row.household} family denied.`)
    }
    selected.value = null
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : "That didn't go through. Try again."
  } finally {
    busy.value = false
  }
}
const kb = (n: number) =>
  n >= 1024 * 1024 ? `${(n / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(n / 1024))} KB`
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Scholarship reviews</h1>
      <p class="text-muted-foreground">Review financial assistance requests and apply awards to family balances.</p>
    </div>

    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >

    <div v-if="queue" class="grid grid-cols-2 gap-4 lg:grid-cols-4">
      <StatTile label="Needs review" :value="queue.counts.submitted"
        ><template #icon><Hourglass class="text-amber-600" /></template
      ></StatTile>
      <StatTile label="Approved" :value="queue.counts.approved"
        ><template #icon><CircleCheck class="text-emerald-600" /></template
      ></StatTile>
      <StatTile label="Denied" :value="queue.counts.denied"
        ><template #icon><CircleX /></template
      ></StatTile>
      <StatTile label="Awarded" :value="money(queue.counts.awardedCents)"
        ><template #icon><HandHeart /></template
      ></StatTile>
    </div>
    <div v-else-if="!loadError" class="grid grid-cols-2 gap-4 lg:grid-cols-4">
      <Skeleton v-for="i in 4" :key="i" class="h-24" />
    </div>

    <Tabs v-model="tab">
      <TabsList>
        <TabsTrigger value="submitted">Needs review</TabsTrigger>
        <TabsTrigger value="approved">Approved</TabsTrigger>
        <TabsTrigger value="denied">Denied</TabsTrigger>
        <TabsTrigger value="all">All</TabsTrigger>
      </TabsList>
    </Tabs>

    <DataTable
      :columns="columns"
      :data="queue?.rows ?? null"
      :loading="loading"
      :get-row-id="(r) => String(r.id)"
      :on-row-click="open"
      empty-text="No applications here."
    >
      <template #cell-family="{ row: r }">
        <div class="font-medium">{{ r.household }} family</div>
        <div class="text-xs text-muted-foreground">
          {{ r.campers.join(', ') }}<span class="md:hidden"> · {{ r.program }}</span>
        </div>
      </template>
      <template #cell-program="{ row: r }">
        <div>{{ r.program }}</div>
        <div class="font-mono text-xs text-muted-foreground">{{ r.confirmationCode }}</div>
      </template>
      <template #cell-submitted="{ row: r }">{{ date(r.submittedAt) }}</template>
      <template #cell-requested="{ row: r }">{{ money(r.requestedCents) }}</template>
      <template #cell-status="{ row: r }"><StatusBadge :status="label(r.status)" /></template>
    </DataTable>

    <Sheet
      :open="!!selected"
      @update:open="
        (v) => {
          if (!v) selected = null
        }
      "
    >
      <SheetContent v-if="selected" class="w-full overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle class="flex items-center gap-2"
            >{{ selected.household }} family <StatusBadge :status="label(detail?.status ?? selected.status)"
          /></SheetTitle>
          <SheetDescription>Submitted {{ date(selected.submittedAt) }} by {{ selected.submittedBy }}</SheetDescription>
        </SheetHeader>
        <div class="space-y-4 px-4 text-sm">
          <Skeleton v-if="!detail" class="h-64" />
          <template v-else>
            <section class="space-y-1">
              <h3 class="font-medium">{{ detail.program }} · {{ detail.session }}</h3>
              <p class="text-muted-foreground">
                {{ dateRange(detail.startDate, detail.endDate) }} ·
                <span class="font-mono">{{ detail.confirmationCode }}</span>
              </p>
              <div class="overflow-x-auto rounded-md border">
                <dl class="grid min-w-72 grid-cols-[1fr_auto] gap-x-4 gap-y-1 p-3">
                  <template v-for="c in detail.campers" :key="c.registrationId">
                    <dt>
                      {{ c.name }} <span class="text-muted-foreground">· Grade {{ c.grade }}</span>
                    </dt>
                    <dd class="text-right tabular-nums">{{ money(c.priceCents) }}</dd>
                    <dt v-if="c.discountCents" class="pl-3 text-muted-foreground">Other discounts</dt>
                    <dd v-if="c.discountCents" class="text-right text-muted-foreground tabular-nums">
                      −{{ money(c.discountCents) }}
                    </dd>
                  </template>
                  <dt class="border-t pt-1 font-medium">Eligible total</dt>
                  <dd class="border-t pt-1 text-right font-medium tabular-nums" data-testid="eligible">
                    {{ money(detail.eligibleCents) }}
                  </dd>
                  <dt class="text-muted-foreground">Still owed</dt>
                  <dd class="text-right tabular-nums">{{ money(detail.owedCents) }}</dd>
                  <dt class="text-muted-foreground">Requested</dt>
                  <dd class="text-right tabular-nums">{{ money(detail.requestedCents) }}</dd>
                </dl>
              </div>
            </section>

            <section class="space-y-1">
              <h3 class="font-medium">Household</h3>
              <p>{{ detail.household.contact }} · {{ detail.household.city }}</p>
              <p class="text-muted-foreground">{{ detail.household.email }} · {{ detail.household.phone }}</p>
              <p><span class="text-muted-foreground">Income range:</span> {{ detail.incomeBand }}</p>
            </section>

            <section class="space-y-1">
              <h3 class="font-medium">Reason</h3>
              <p class="whitespace-pre-line">{{ detail.reason }}</p>
            </section>

            <section v-if="detail.document" class="flex items-center gap-3 rounded-md border p-3">
              <FileText class="size-5 shrink-0 text-muted-foreground" />
              <div class="min-w-0 flex-1">
                <p class="truncate font-medium">{{ detail.document.fileName }}</p>
                <p class="text-xs text-muted-foreground">
                  {{ kb(detail.document.sizeBytes) }} · uploaded {{ date(detail.document.uploadedAt) }}
                </p>
              </div>
              <Button as-child variant="outline" size="sm">
                <a :href="`/api/admin/scholarships/${detail.id}/document`" target="_blank" rel="noopener">View</a>
              </Button>
            </section>

            <Separator />

            <template v-if="detail.status === 'Submitted'">
              <div class="space-y-1">
                <Label for="award">Award amount ($)</Label>
                <Input id="award" v-model="award" inputmode="decimal" type="number" min="0" step="1" />
                <p v-if="awardCents && !capProblem" class="text-xs text-muted-foreground">
                  Balance after award: {{ money(balanceAfter) }}
                </p>
              </div>
              <Alert v-if="capProblem" variant="destructive" data-testid="cap-check">
                <TriangleAlert />
                <AlertTitle>Over the limit</AlertTitle>
                <AlertDescription>{{ capProblem }}</AlertDescription>
              </Alert>
              <div class="space-y-1">
                <Label for="note">Note for the file</Label>
                <Textarea id="note" v-model="note" maxlength="1000" placeholder="Required when denying" />
              </div>
            </template>
            <section v-else class="space-y-1 rounded-md border p-3">
              <p class="font-medium">
                {{ detail.status }}{{ detail.status === 'Approved' ? ` · ${money(detail.awardCents)}` : '' }}
              </p>
              <p class="text-muted-foreground">
                {{ detail.decidedBy }} · {{ detail.decidedAt ? dateTime(detail.decidedAt) : '' }}
              </p>
              <p v-if="detail.decisionNote">{{ detail.decisionNote }}</p>
            </section>
          </template>
          <p v-if="actionError" class="text-destructive" role="alert">{{ actionError }}</p>
        </div>
        <SheetFooter
          v-if="detail?.status === 'Submitted'"
          class="sticky bottom-0 border-t bg-background flex-row justify-end gap-2"
        >
          <Button variant="outline" :disabled="busy || !note.trim()" @click="decide('deny')">Deny</Button>
          <Button :disabled="busy || !awardCents || !!capProblem" @click="decide('approve')"
            >Approve {{ awardCents ? money(awardCents) : '' }}</Button
          >
        </SheetFooter>
      </SheetContent>
    </Sheet>
  </div>
</template>
