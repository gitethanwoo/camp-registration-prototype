<script setup lang="ts">
import { AlertTriangle, ArrowRight, CircleCheck, Info, TriangleAlert, XCircle } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Label } from '@/components/ui/label'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { dateTime, money } from '@/lib/format'
import type { StaffTransferDetail, StaffTransferRow, TransferStatus } from './types'

// C10 · Staff approve or deny session transfers. Approving moves the seat and adjusts payment in one step.
type Filter = 'pending' | 'approved' | 'denied' | 'all'
const filter = ref<Filter>('pending')
const data = ref<{ counts: Record<Filter, number>; rows: StaffTransferRow[] } | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)

async function load() {
  loading.value = true
  try {
    data.value = await api.get(`/admin/transfers?status=${filter.value}`)
    loadError.value = null
  } catch (e) {
    loadError.value =
      e instanceof ApiError && e.status === 403
        ? 'Only the Customer Experience team reviews transfer requests.'
        : "Couldn't load transfer requests."
  } finally {
    loading.value = false
  }
}
onMounted(load)
function setFilter(v: string | number) {
  filter.value = v as Filter
  load()
}

const tone: Record<TransferStatus, string> = {
  Pending: 'border-amber-200 bg-amber-50 text-amber-800',
  Approved: 'border-emerald-200 bg-emerald-50 text-emerald-800',
  Denied: 'bg-muted text-muted-foreground',
}
function signed(cents: number) {
  if (cents > 0) return `+${money(cents)}`
  if (cents < 0) return `−${money(-cents)}`
  return money(0)
}

const columns: ColumnDef<StaffTransferRow>[] = [
  { accessorKey: 'participant', header: 'Camper', meta: { cellClass: 'align-top' } },
  { id: 'from', header: 'From', meta: { class: 'hidden md:table-cell', cellClass: 'align-top' } },
  { id: 'to', header: 'To', meta: { cellClass: 'align-top' } },
  {
    accessorKey: 'priceDifferenceCents',
    header: 'Price difference',
    cell: ({ row }) => signed(row.original.priceDifferenceCents),
    meta: { class: 'hidden sm:table-cell', cellClass: 'align-top tabular-nums' },
  },
  { accessorKey: 'status', header: 'Status', meta: { cellClass: 'align-top' } },
  {
    accessorKey: 'requestedBy',
    header: 'Requested by',
    meta: { class: 'hidden lg:table-cell', cellClass: 'align-top' },
  },
]

// ── Review sheet ──
const detail = ref<StaffTransferDetail | null>(null)
const openId = ref<number | null>(null)
const note = ref('')
const busy = ref(false)
const error = ref<string | null>(null)

async function open(row: StaffTransferRow) {
  openId.value = row.id
  detail.value = null
  note.value = ''
  error.value = null
  detail.value = await api.get<StaffTransferDetail>(`/admin/transfers/${row.id}`)
}
function close() {
  openId.value = null
  detail.value = null
}
async function decide(kind: 'approve' | 'deny') {
  const d = detail.value
  if (!d) return
  if (kind === 'deny' && !note.value.trim()) {
    error.value = "Give the family a reason. They'll see it on their request."
    return
  }
  busy.value = true
  error.value = null
  try {
    const res = await api.post<{ refundCents?: number }>(`/admin/transfers/${d.request.id}/${kind}`, {
      note: note.value.trim() || null,
    })
    toast.success(
      kind === 'approve'
        ? `${d.request.participant} moved to ${d.request.toDates}.${res?.refundCents ? ` ${money(res.refundCents)} refunded.` : ''}`
        : `Request denied. ${d.request.participant} stays in ${d.request.fromDates}.`,
    )
    close()
    await load()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "That didn't go through. Nothing was changed."
    detail.value = await api.get<StaffTransferDetail>(`/admin/transfers/${d.request.id}`)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Transfer requests</h1>
      <p class="text-muted-foreground">Families asking to move to another session of the same program.</p>
    </div>

    <Tabs :model-value="filter" @update:model-value="setFilter">
      <TabsList>
        <TabsTrigger value="pending">Open ({{ data?.counts.pending ?? '…' }})</TabsTrigger>
        <TabsTrigger value="approved">Approved</TabsTrigger>
        <TabsTrigger value="denied">Denied</TabsTrigger>
        <TabsTrigger value="all">All</TabsTrigger>
      </TabsList>
    </Tabs>

    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >
    <DataTable
      v-else
      :columns="columns"
      :data="data?.rows"
      :loading="loading"
      :get-row-id="(r) => String(r.id)"
      :on-row-click="open"
      :empty-text="filter === 'pending' ? 'No open requests.' : 'No requests here.'"
    >
      <template #cell-participant="{ row: r }">
        <div class="font-medium">{{ r.participant }}</div>
        <div class="text-sm text-muted-foreground">{{ r.program }}</div>
      </template>
      <template #cell-from="{ row: r }">
        <div>{{ r.fromSession }}</div>
        <div class="text-sm text-muted-foreground">{{ r.fromDates }}</div>
      </template>
      <template #cell-to="{ row: r }">
        <div>{{ r.toSession }}</div>
        <div class="text-sm text-muted-foreground">{{ r.toDates }}</div>
        <div
          v-if="r.status === 'Pending' && r.toPool"
          class="text-xs"
          :class="r.blocked ? 'font-medium text-destructive' : 'text-muted-foreground'"
        >
          {{ r.toPool }}: {{ r.toReserved }} / {{ r.toCapacity }}{{ r.blocked ? ' · blocked' : '' }}
        </div>
      </template>
      <template #cell-status="{ row: r }">
        <Badge variant="outline" :class="tone[r.status]">{{ r.status }}</Badge>
      </template>
    </DataTable>

    <Sheet
      :open="openId !== null"
      @update:open="
        (v) => {
          if (!v) close()
        }
      "
    >
      <SheetContent class="w-full overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle class="flex flex-wrap items-center gap-2">
            Transfer request
            <Badge v-if="detail" variant="outline" :class="tone[detail.request.status]">{{
              detail.request.status
            }}</Badge>
          </SheetTitle>
          <SheetDescription v-if="detail">
            {{ detail.request.participant }} ·
            <RouterLink :to="`/admin/households/${detail.request.householdId}`" class="underline"
              >household #{{ detail.request.householdId }}</RouterLink
            >
          </SheetDescription>
        </SheetHeader>

        <div v-if="!detail" class="px-4"><Skeleton class="h-72 rounded-lg" /></div>
        <div v-else class="space-y-5 px-4">
          <div class="grid grid-cols-[1fr_auto_1fr] items-stretch gap-2">
            <div class="rounded-lg border p-3 text-sm">
              <p class="text-xs text-muted-foreground">From</p>
              <p class="font-medium">{{ detail.request.fromSession }}</p>
              <p>{{ detail.request.fromDates }}</p>
              <p class="text-muted-foreground">
                {{ detail.from.pool }} · {{ detail.from.reserved }} / {{ detail.from.capacity }}
              </p>
              <p class="mt-2 font-medium tabular-nums">{{ money(detail.registration.priceCents) }}</p>
            </div>
            <ArrowRight class="size-4 self-center text-muted-foreground" />
            <div
              class="rounded-lg border p-3 text-sm"
              :class="detail.check && !detail.check.canMove ? 'border-destructive/50 bg-destructive/5' : ''"
            >
              <p class="text-xs text-muted-foreground">To</p>
              <p class="font-medium">{{ detail.request.toSession }}</p>
              <p>{{ detail.request.toDates }}</p>
              <template v-if="detail.check">
                <p class="text-muted-foreground">
                  {{ detail.check.pool ?? 'No matching pool'
                  }}<template v-if="detail.check.pool">
                    · {{ detail.check.reserved }} / {{ detail.check.capacity }}</template
                  >
                </p>
                <p class="mt-2 font-medium tabular-nums">{{ money(detail.check.priceCents) }}</p>
                <p
                  class="text-xs"
                  :class="detail.check.spotsLeft ? 'text-emerald-700' : 'font-medium text-destructive'"
                >
                  {{
                    detail.check.spotsLeft ? `${detail.check.spotsLeft} of ${detail.check.capacity} spots left` : 'Full'
                  }}
                  · checked just now
                </p>
              </template>
            </div>
          </div>

          <Alert v-if="detail.check && !detail.check.canMove" variant="destructive">
            <XCircle />
            <AlertTitle class="line-clamp-none">Can't approve this request</AlertTitle>
            <AlertDescription>
              <p v-for="b in detail.check.blockers" :key="b">{{ b }}</p>
              <p>Deny it with a reason, or wait for a seat to open.</p>
            </AlertDescription>
          </Alert>

          <dl v-if="detail.check" class="space-y-1 rounded-lg bg-muted/40 p-3 text-sm">
            <div class="flex justify-between">
              <dt>Price difference</dt>
              <dd class="font-medium tabular-nums">{{ signed(detail.check.priceDifferenceCents) }}</dd>
            </div>
            <div class="flex justify-between">
              <dt>Paid so far</dt>
              <dd class="tabular-nums">{{ money(detail.registration.paidCents) }}</dd>
            </div>
            <div class="flex justify-between">
              <dt>Payment adjustment</dt>
              <dd class="font-medium tabular-nums">
                {{
                  detail.check.refundCents
                    ? `${money(detail.check.refundCents)} refund`
                    : `Balance ${money(detail.check.newBalanceCents)}`
                }}
              </dd>
            </div>
          </dl>
          <dl v-else class="space-y-1 rounded-lg bg-muted/40 p-3 text-sm">
            <div class="flex justify-between">
              <dt>Price difference</dt>
              <dd class="tabular-nums">{{ signed(detail.request.priceDifferenceCents) }}</dd>
            </div>
            <div v-if="detail.request.refundCents" class="flex justify-between">
              <dt>Refunded</dt>
              <dd class="tabular-nums">{{ money(detail.request.refundCents) }}</dd>
            </div>
          </dl>

          <div v-if="detail.check" class="space-y-2">
            <p class="text-sm font-medium">Destination requirements</p>
            <ul class="space-y-2 text-sm">
              <li v-for="r in detail.check.requirements" :key="r.label" class="flex gap-2">
                <CircleCheck v-if="r.state === 'Ok'" class="mt-0.5 size-4 shrink-0 text-emerald-700" />
                <TriangleAlert v-else-if="r.state === 'Review'" class="mt-0.5 size-4 shrink-0 text-amber-600" />
                <XCircle v-else class="mt-0.5 size-4 shrink-0 text-destructive" />
                <span
                  ><span class="font-medium">{{ r.label }}:</span> {{ r.detail }}</span
                >
              </li>
            </ul>
          </div>

          <div class="space-y-1 text-sm">
            <p class="font-medium">Request details</p>
            <p>
              <span class="text-muted-foreground">Requested by</span> {{ detail.request.requestedBy }} ·
              {{ dateTime(detail.request.createdAt) }}
            </p>
            <p class="rounded-md border bg-muted/30 p-2">{{ detail.request.reason }}</p>
          </div>

          <div v-if="detail.request.status !== 'Pending'" class="rounded-md border p-3 text-sm">
            <p class="font-medium">{{ detail.request.status }} by {{ detail.request.decidedBy }}</p>
            <p class="text-muted-foreground">
              {{ detail.request.decidedAt ? dateTime(detail.request.decidedAt) : '' }}
            </p>
            <p v-if="detail.request.decisionNote" class="mt-1">{{ detail.request.decisionNote }}</p>
          </div>

          <template v-else>
            <Alert>
              <Info />
              <AlertDescription>
                Approving moves the existing registration, its forms and payment history to the new session. The seat
                move, any refund and the payment plan update happen together; if one fails, nothing changes.
              </AlertDescription>
            </Alert>
            <div class="space-y-2">
              <Label for="decision-note">Reason (required to deny; the family sees it)</Label>
              <Textarea
                id="decision-note"
                v-model="note"
                rows="3"
                maxlength="500"
                placeholder="e.g. Grade 4 in June 19–23 is full."
              />
            </div>
            <p v-if="error" class="flex gap-2 text-sm text-destructive" role="alert">
              <AlertTriangle class="mt-0.5 size-4 shrink-0" />{{ error }}
            </p>
          </template>
        </div>

        <SheetFooter v-if="detail?.request.status === 'Pending'" class="flex-row gap-2">
          <Button variant="outline" :disabled="busy" @click="decide('deny')">Deny request</Button>
          <Button :disabled="busy || !detail.check?.canMove" @click="decide('approve')">{{
            busy ? 'Working…' : 'Approve transfer'
          }}</Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  </div>
</template>
