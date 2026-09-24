<script setup lang="ts">
import { AlertTriangle } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Label } from '@/components/ui/label'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { date, dateTime, money } from '@/lib/format'
import type { Decision, DiscountQueue, DiscountRow } from './types'

// C8 · Host and partner discount codes stay inert until someone here approves them (FR-62, FR-63).
type Filter = 'pending' | 'approved' | 'rejected' | 'all'
const filter = ref<Filter>('pending')
const queue = ref<DiscountQueue | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)

async function load() {
  loading.value = true
  try {
    queue.value = await api.get<DiscountQueue>(`/admin/discounts?status=${filter.value}`)
    loadError.value = null
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : "Couldn't load the queue."
  } finally {
    loading.value = false
  }
}
onMounted(load)
function setFilter(v: string | number) {
  filter.value = v as Filter
  load()
}

const columns: ColumnDef<DiscountRow>[] = [
  { accessorKey: 'code', header: 'Code', meta: { cellClass: 'font-mono font-medium' } },
  { accessorKey: 'requesterType', header: 'Type', meta: { class: 'hidden md:table-cell' } },
  { id: 'requestedBy', header: 'Requested by', meta: { class: 'hidden sm:table-cell' } },
  { id: 'discount', header: 'Discount' },
  {
    accessorKey: 'program',
    header: 'Applies to',
    meta: { class: 'hidden lg:table-cell', cellClass: 'text-muted-foreground' },
  },
  { accessorKey: 'decision', header: 'Status' },
]
const tone: Record<Decision, string> = {
  Pending: 'border-amber-200 bg-amber-50 text-amber-800',
  Approved: 'border-emerald-200 bg-emerald-50 text-emerald-800',
  Rejected: 'bg-muted text-muted-foreground',
}

// ── Review sheet ──
const selected = ref<DiscountRow | null>(null)
const note = ref('')
const error = ref<string | null>(null)
const busy = ref(false)
const confirmApprove = ref(false)
const needsFinance = computed(() => !!selected.value?.overThreshold && !queue.value?.canApproveOverThreshold)

function open(row: DiscountRow) {
  selected.value = row
  note.value = ''
  error.value = null
}
function startApprove() {
  if (!selected.value) return
  if (selected.value.overThreshold && !note.value.trim()) {
    error.value = 'This code is over the threshold. Add a note saying why you are approving it.'
    return
  }
  error.value = null
  confirmApprove.value = true
}
async function decide(kind: 'approve' | 'reject') {
  const row = selected.value
  if (!row) return
  if (kind === 'reject' && !note.value.trim()) {
    error.value = 'Add a note for the requester saying why it was rejected.'
    return
  }
  busy.value = true
  error.value = null
  try {
    await api.post(`/admin/discounts/${row.id}/${kind}`, { note: note.value.trim() || null })
    toast.success(
      kind === 'approve'
        ? `${row.code} approved. It works at checkout now.`
        : `${row.code} rejected. ${row.requestedBy} will see your note.`,
    )
    confirmApprove.value = false
    selected.value = null
    await load()
  } catch (e) {
    confirmApprove.value = false
    error.value = e instanceof ApiError ? e.message : "That didn't go through. Try again."
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Discount approval queue</h1>
      <p class="text-muted-foreground">
        Codes requested by hosts and partners. A code does nothing at checkout until it's approved. Codes worth more
        than {{ queue?.thresholdPercent ?? 10 }}% of the session price need Finance.
      </p>
    </div>

    <Tabs :model-value="filter" @update:model-value="setFilter">
      <TabsList>
        <TabsTrigger value="pending">Pending ({{ queue?.counts.pending ?? '…' }})</TabsTrigger>
        <TabsTrigger value="approved">Approved</TabsTrigger>
        <TabsTrigger value="rejected">Rejected</TabsTrigger>
        <TabsTrigger value="all">All</TabsTrigger>
      </TabsList>
    </Tabs>

    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >
    <DataTable
      v-else
      :columns="columns"
      :data="queue?.rows"
      :loading="loading"
      :get-row-id="(r) => String(r.id)"
      :on-row-click="open"
      :empty-text="filter === 'pending' ? 'Nothing waiting for review.' : 'No codes here.'"
    >
      <template #cell-requesterType="{ row: r }">
        <Badge
          variant="outline"
          :class="
            r.requesterType === 'Host'
              ? 'border-sky-200 bg-sky-50 text-sky-800'
              : 'border-violet-200 bg-violet-50 text-violet-800'
          "
          >{{ r.requesterType }}</Badge
        >
      </template>
      <template #cell-requestedBy="{ row: r }">
        <div>{{ r.organization }}</div>
        <div class="text-sm text-muted-foreground">{{ r.requestedBy }}</div>
      </template>
      <template #cell-discount="{ row: r }">
        <div class="whitespace-nowrap">{{ r.description }}</div>
        <Badge
          v-if="r.overThreshold"
          variant="outline"
          class="mt-1 border-amber-300 bg-amber-50 font-normal text-amber-900"
          >Over threshold</Badge
        >
      </template>
      <template #cell-decision="{ row: r }">
        <Badge variant="outline" :class="tone[r.decision]">{{ r.decision }}</Badge>
      </template>
    </DataTable>

    <Sheet
      :open="!!selected"
      @update:open="
        (v) => {
          if (!v) selected = null
        }
      "
    >
      <SheetContent v-if="selected" class="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle class="font-mono text-xl">{{ selected.code }}</SheetTitle>
          <SheetDescription class="flex flex-wrap items-center gap-2">
            <Badge variant="outline" :class="tone[selected.decision]">{{ selected.decision }}</Badge>
            <span v-if="selected.overThreshold" class="text-amber-800"
              >Over the {{ queue?.thresholdPercent }}% threshold</span
            >
          </SheetDescription>
        </SheetHeader>

        <div class="space-y-5 px-4">
          <dl class="grid grid-cols-[8.5rem_1fr] gap-x-3 gap-y-2 text-sm">
            <dt class="text-muted-foreground">Requested by</dt>
            <dd>
              {{ selected.requestedBy }}, {{ selected.organization }} ({{ selected.requesterType.toLowerCase() }})
            </dd>
            <dt class="text-muted-foreground">Discount</dt>
            <dd>
              {{ selected.kind === 'Percent' ? 'Percentage' : 'Flat amount' }}: {{ selected.description }}
              <div class="text-muted-foreground">
                {{ selected.percentOfPrice }}% of the {{ money(selected.sessionPriceCents) }} session price
              </div>
            </dd>
            <dt class="text-muted-foreground">Applies to</dt>
            <dd>All {{ selected.program }} sessions</dd>
            <dt class="text-muted-foreground">Valid dates</dt>
            <dd>{{ date(selected.validFrom) }} – {{ date(selected.validTo) }}</dd>
            <dt class="text-muted-foreground">Max uses</dt>
            <dd>{{ selected.maxUses ?? 'No limit' }}</dd>
            <dt class="text-muted-foreground">Stacks with other codes</dt>
            <dd>{{ selected.stackable ? 'Yes' : 'No' }}</dd>
            <dt class="text-muted-foreground">Overrides other codes</dt>
            <dd>{{ selected.overridesOtherCodes ? 'Yes' : 'No' }}</dd>
            <dt class="text-muted-foreground">At checkout now</dt>
            <dd>{{ selected.codeStatus === 'Approved' ? 'Active' : 'Inactive (reads as an invalid code)' }}</dd>
            <dt class="text-muted-foreground">Requested</dt>
            <dd>{{ dateTime(selected.requestedAt) }}</dd>
          </dl>

          <div v-if="selected.requesterNote" class="rounded-md border bg-muted/40 p-3 text-sm">
            <p class="mb-1 text-xs font-medium text-muted-foreground">Note from requester</p>
            {{ selected.requesterNote }}
          </div>

          <div v-if="selected.decision !== 'Pending'" class="rounded-md border p-3 text-sm">
            <p class="font-medium">{{ selected.decision }} by {{ selected.reviewedBy }}</p>
            <p class="text-muted-foreground">{{ selected.reviewedAt ? dateTime(selected.reviewedAt) : '' }}</p>
            <p v-if="selected.reviewNote" class="mt-1">{{ selected.reviewNote }}</p>
          </div>

          <template v-else>
            <Alert v-if="needsFinance">
              <AlertTriangle />
              <AlertTitle>Finance approves this one</AlertTitle>
              <AlertDescription
                >{{ selected.code }} is over the {{ queue?.thresholdPercent }}% threshold. You can reject it, or leave
                it for Finance to approve.</AlertDescription
              >
            </Alert>
            <div class="space-y-2">
              <Label for="review-note">Note {{ selected.overThreshold ? '(required)' : '(required to reject)' }}</Label>
              <Textarea
                id="review-note"
                v-model="note"
                rows="3"
                maxlength="1000"
                placeholder="Why you're approving or rejecting it. The requester sees rejection notes."
              />
            </div>
            <p v-if="error" class="text-sm text-destructive" role="alert">{{ error }}</p>
          </template>
        </div>

        <SheetFooter v-if="selected.decision === 'Pending'" class="flex-row gap-2">
          <Button :disabled="busy || needsFinance" @click="startApprove">Approve</Button>
          <Button
            variant="outline"
            class="border-destructive/40 text-destructive"
            :disabled="busy"
            @click="decide('reject')"
            >Reject</Button
          >
        </SheetFooter>
      </SheetContent>
    </Sheet>

    <AlertDialog v-model:open="confirmApprove">
      <AlertDialogContent v-if="selected">
        <AlertDialogHeader>
          <AlertDialogTitle>Approve {{ selected.code }}?</AlertDialogTitle>
          <AlertDialogDescription>
            {{ selected.description }} goes live at checkout for {{ selected.program }} as soon as you approve it.
            <template v-if="selected.overThreshold">
              It's over the normal threshold; your note is kept with the approval.</template
            >
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="busy">Cancel</AlertDialogCancel>
          <Button :disabled="busy" @click="decide('approve')">{{ busy ? 'Approving…' : 'Approve' }}</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
