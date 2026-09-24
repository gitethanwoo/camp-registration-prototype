<script setup lang="ts">
import { ArrowLeft, Lock, Mail, Phone } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import FormAnswers from '@/features/forms/FormAnswers.vue'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, dateTime, money } from '@/lib/format'

interface Detail {
  id: number
  status: string
  grade: number
  gradeLabel: string
  createdAt: string
  participant: {
    id: number
    firstName: string
    lastName: string
    dateOfBirth: string
    gender: string
    dietary: string | null
    allergies: string | null
    adaNeeds: string | null
  }
  household: {
    id: number
    name: string
    email: string
    phone: string
    city: string
    salesforceId: string | null
    adults: { firstName: string; lastName: string; role: string; email: string | null }[]
    otherRegistrations: { id: number; participant: string; program: string; session: string; status: string }[]
  }
  program: { name: string; slug: string; healthMechanism: string }
  session: { id: number; name: string; startDate: string; endDate: string }
  pool: string
  answers: Record<string, string>
  healthStatus: string
  healthOnFile: boolean
  waivers: {
    title: string
    currentVersion: number
    status: string
    acceptedVersion: number | null
    signerName: string | null
    acceptedAt: string | null
  }[]
  money: {
    priceCents: number
    discountCents: number
    paidCents: number
    balanceCents: number
    confirmationCode: string
    option: string | null
    balanceDueDate: string
    orderParticipants: number
    operations:
      | {
          id: number
          kind: string
          amountCents: number
          succeeded: boolean
          processorRef: string
          cardLast4: string | null
          reason: string | null
          createdAt: string
        }[]
      | null
    installments: { sequence: number; dueDate: string; amountCents: number; status: string }[] | null
  }
  cancellation: { daysUntilStart: number; rule: string; suggestedRefundCents: number; maxRefundCents: number } | null
  audit: { actor: string; action: string; detail: string; createdAt: string }[]
  messages: { type: string; target: string; createdAt: string; processedAt: string | null }[]
}

type Operation = NonNullable<Detail['money']['operations']>[number]
type Installment = NonNullable<Detail['money']['installments']>[number]

const operationColumns: ColumnDef<Operation>[] = [
  {
    accessorKey: 'createdAt',
    header: 'Date',
    cell: ({ row }) => dateTime(row.original.createdAt),
    meta: { cellClass: 'whitespace-nowrap' },
  },
  {
    accessorKey: 'kind',
    header: 'Type',
    cell: ({ row }) => (row.original.succeeded ? row.original.kind : `${row.original.kind} (failed)`),
  },
  {
    accessorKey: 'cardLast4',
    header: 'Card',
    cell: ({ row }) => (row.original.cardLast4 ? `···${row.original.cardLast4}` : '—'),
    meta: { cellClass: 'tabular-nums text-muted-foreground' },
  },
  {
    accessorKey: 'processorRef',
    header: 'Reference',
    meta: { class: 'hidden lg:table-cell', cellClass: 'font-mono text-xs text-muted-foreground' },
  },
  {
    accessorKey: 'reason',
    header: 'Note',
    cell: ({ row }) => row.original.reason ?? '—',
    meta: { class: 'hidden md:table-cell', cellClass: 'max-w-48 truncate text-muted-foreground' },
  },
  {
    accessorKey: 'amountCents',
    header: 'Amount',
    cell: ({ row }) => `${row.original.kind === 'Refund' ? '−' : ''}${money(row.original.amountCents)}`,
    meta: {
      class: 'text-right',
      cellClass: (op) => ['tabular-nums', !op.succeeded && 'text-destructive line-through'],
    },
  },
]
const installmentColumns: ColumnDef<Installment>[] = [
  {
    accessorKey: 'sequence',
    header: 'Installment',
    cell: ({ row, table }) => `${row.original.sequence} of ${table.getCoreRowModel().rows.length}`,
  },
  { accessorKey: 'dueDate', header: 'Due', cell: ({ row }) => date(row.original.dueDate) },
  { accessorKey: 'status', header: 'Status' },
  {
    accessorKey: 'amountCents',
    header: 'Amount',
    cell: ({ row }) => money(row.original.amountCents),
    meta: { class: 'text-right', cellClass: 'tabular-nums' },
  },
]

const props = defineProps<{ id: number }>()
const r = ref<Detail | null>(null)
async function load() {
  r.value = await api.get<Detail>(`/admin/registrations/${props.id}`)
}
onMounted(load)

// ── Cancel + refund, on this screen (FR-40/49) ──
const cancelOpen = ref(false)
const reason = ref('')
const refund = ref('')
const cancelling = ref(false)
const cancelError = ref<string | null>(null)
function openCancel() {
  reason.value = ''
  refund.value = ((r.value?.cancellation?.suggestedRefundCents ?? 0) / 100).toFixed(2)
  cancelError.value = null
}
const refundCents = computed(() => Math.round(Number(refund.value || 0) * 100))
async function cancel() {
  if (!reason.value.trim()) {
    cancelError.value = 'Add a reason. It goes in the audit log.'
    return
  }
  cancelling.value = true
  cancelError.value = null
  try {
    await api.post(`/admin/registrations/${props.id}/cancel`, { reason: reason.value, refundCents: refundCents.value })
    cancelOpen.value = false
    toast.success(
      `Cancelled. ${refundCents.value ? `${money(refundCents.value)} refunded to card.` : 'No refund issued.'} Seat released.`,
    )
    await load()
  } catch (e) {
    cancelError.value =
      e instanceof ApiError ? e.message : "The registration wasn't cancelled. Check your connection and try again."
  } finally {
    cancelling.value = false
  }
}
</script>

<template>
  <div class="space-y-6">
    <Button variant="ghost" size="sm" as-child class="-ml-2.5 text-muted-foreground">
      <RouterLink to="/admin/registrations"><ArrowLeft />Registrations</RouterLink>
    </Button>
    <Skeleton v-if="!r" class="h-96 rounded-xl" />
    <template v-else>
      <div class="flex flex-wrap items-start gap-4">
        <div class="flex-1">
          <div class="flex flex-wrap items-center gap-3">
            <h1 class="text-2xl font-semibold tracking-tight">
              {{ r.participant.firstName }} {{ r.participant.lastName }}
            </h1>
            <StatusBadge :status="r.status" />
          </div>
          <p class="text-muted-foreground">
            {{ r.program.name }} · {{ r.session.name }} · {{ r.pool
            }}<template v-if="r.pool !== r.gradeLabel"> · {{ r.gradeLabel }}</template>
          </p>
          <p class="text-sm text-muted-foreground">
            Confirmation {{ r.money.confirmationCode }} · registered {{ dateTime(r.createdAt) }}
          </p>
        </div>
        <AlertDialog v-if="r.cancellation" v-model:open="cancelOpen">
          <AlertDialogTrigger as-child
            ><Button variant="outline" @click="openCancel">Cancel registration</Button></AlertDialogTrigger
          >
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Cancel {{ r.participant.firstName }}'s registration?</AlertDialogTitle>
              <AlertDialogDescription>
                Their {{ r.pool }} seat is released for the waitlist. The family is notified by email.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <div class="space-y-4">
              <div class="rounded-md border bg-muted/40 p-3 text-sm">
                <p class="font-medium">{{ r.cancellation.rule }}</p>
                <p class="text-muted-foreground">
                  {{ r.cancellation.daysUntilStart }} days until start · paid {{ money(r.money.paidCents) }} · suggested
                  refund {{ money(r.cancellation.suggestedRefundCents) }}
                </p>
              </div>
              <div class="space-y-2">
                <Label for="refund">Refund to card</Label>
                <Input id="refund" v-model="refund" inputmode="decimal" />
                <p class="text-xs text-muted-foreground">
                  Up to {{ money(r.cancellation.maxRefundCents) }}. Overrides are allowed and logged.
                </p>
              </div>
              <div class="space-y-2">
                <Label for="reason">Reason</Label>
                <Textarea
                  id="reason"
                  v-model="reason"
                  rows="2"
                  placeholder="e.g. Family schedule conflict, called 9/23"
                />
              </div>
              <p v-if="cancelError" class="text-sm text-destructive" role="alert">{{ cancelError }}</p>
            </div>
            <AlertDialogFooter>
              <AlertDialogCancel :disabled="cancelling">Keep registration</AlertDialogCancel>
              <Button variant="destructive" :disabled="cancelling" @click="cancel">
                {{
                  cancelling
                    ? 'Cancelling…'
                    : refundCents
                      ? `Cancel and refund ${money(refundCents)}`
                      : 'Cancel without refund'
                }}
              </Button>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </div>

      <div class="grid gap-6 xl:grid-cols-[1fr_340px]">
        <Tabs default-value="overview" class="min-w-0">
          <TabsList>
            <TabsTrigger value="overview">Overview</TabsTrigger>
            <TabsTrigger value="payments">Payments</TabsTrigger>
            <TabsTrigger value="activity">Activity</TabsTrigger>
          </TabsList>

          <TabsContent value="overview" class="mt-4 space-y-4">
            <Card>
              <CardHeader><CardTitle>Documents</CardTitle></CardHeader>
              <CardContent class="divide-y text-sm">
                <div class="flex items-center justify-between py-3 first:pt-0">
                  <div>
                    <p class="font-medium">
                      Health form {{ r.program.healthMechanism === 'CampDoc' ? '(CampDoc)' : '' }}
                    </p>
                    <p class="flex items-center gap-1 text-muted-foreground">
                      <template v-if="r.healthOnFile"
                        ><Lock class="size-3.5" />On file. Visible to camp health staff only.</template
                      >
                      <template v-else-if="r.program.healthMechanism === 'CampDoc'"
                        >Status synced from CampDoc.</template
                      >
                    </p>
                  </div>
                  <StatusBadge :status="r.healthStatus" />
                </div>
                <div
                  v-for="w in r.waivers"
                  :key="w.title"
                  class="flex items-center justify-between gap-4 py-3 last:pb-0"
                >
                  <div>
                    <p class="font-medium">{{ w.title }}</p>
                    <p class="text-muted-foreground">
                      <template v-if="w.acceptedAt"
                        >v{{ w.acceptedVersion }} signed by {{ w.signerName }} · {{ dateTime(w.acceptedAt) }}</template
                      >
                      <template v-else>Current version v{{ w.currentVersion }}</template>
                    </p>
                  </div>
                  <StatusBadge :status="w.status" />
                </div>
              </CardContent>
            </Card>
            <Card>
              <CardHeader><CardTitle>Answers</CardTitle></CardHeader>
              <CardContent class="space-y-4">
                <!-- K6: the questions this registration answered, labelled with the form version (forms slice). -->
                <FormAnswers :registration-id="id" />
                <Separator />
                <dl class="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
                  <div>
                    <dt class="text-muted-foreground">Allergies</dt>
                    <dd>{{ r.participant.allergies || 'None' }}</dd>
                  </div>
                  <div>
                    <dt class="text-muted-foreground">Dietary</dt>
                    <dd>{{ r.participant.dietary || 'None' }}</dd>
                  </div>
                </dl>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="payments" class="mt-4 space-y-4">
            <Card>
              <CardHeader>
                <CardTitle>Balance</CardTitle>
                <CardDescription
                  >{{ r.money.option }} · order covers {{ r.money.orderParticipants }}
                  {{ r.money.orderParticipants === 1 ? 'camper' : 'campers' }}</CardDescription
                >
              </CardHeader>
              <CardContent class="space-y-1 text-sm">
                <div class="flex justify-between">
                  <span>Price</span><span class="tabular-nums">{{ money(r.money.priceCents) }}</span>
                </div>
                <div v-if="r.money.discountCents" class="flex justify-between">
                  <span>Discount</span><span class="tabular-nums">−{{ money(r.money.discountCents) }}</span>
                </div>
                <div class="flex justify-between">
                  <span>Paid</span><span class="tabular-nums">{{ money(r.money.paidCents) }}</span>
                </div>
                <div class="flex justify-between font-medium">
                  <span>Balance</span><span class="tabular-nums">{{ money(r.money.balanceCents) }}</span>
                </div>
              </CardContent>
            </Card>
            <Card>
              <CardHeader
                ><CardTitle>Transactions</CardTitle
                ><CardDescription>Order-level; shared by everyone on this order.</CardDescription></CardHeader
              >
              <CardContent>
                <p v-if="!r.money.operations?.length" class="text-sm text-muted-foreground">No transactions.</p>
                <DataTable
                  v-else
                  :columns="operationColumns"
                  :data="r.money.operations"
                  :get-row-id="(op) => String(op.id)"
                />
              </CardContent>
            </Card>
            <Card v-if="r.money.installments?.length">
              <CardHeader><CardTitle>Scheduled payments</CardTitle></CardHeader>
              <CardContent>
                <DataTable
                  :columns="installmentColumns"
                  :data="r.money.installments"
                  :get-row-id="(i) => String(i.sequence)"
                >
                  <template #cell-status="{ row: i }"><StatusBadge :status="i.status" /></template>
                </DataTable>
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="activity" class="mt-4">
            <Card>
              <CardHeader><CardTitle>Audit log</CardTitle></CardHeader>
              <CardContent>
                <ol class="space-y-3 text-sm">
                  <li v-for="(a, i) in r.audit" :key="i" class="border-l-2 pl-3">
                    <p>{{ a.detail }}</p>
                    <p class="text-xs text-muted-foreground">
                      {{ a.actor }} · {{ dateTime(a.createdAt) }} · {{ a.action }}
                    </p>
                  </li>
                  <li v-if="!r.audit.length" class="text-muted-foreground">No activity recorded.</li>
                </ol>
              </CardContent>
            </Card>
            <Card class="mt-4">
              <CardHeader><CardTitle>Messages & sync</CardTitle></CardHeader>
              <CardContent class="divide-y text-sm">
                <div v-for="(m, i) in r.messages" :key="i" class="flex justify-between py-2">
                  <span>{{ m.type }} → {{ m.target }}</span>
                  <span class="text-muted-foreground">{{
                    m.processedAt ? `Sent ${dateTime(m.processedAt)}` : 'Queued'
                  }}</span>
                </div>
                <p v-if="!r.messages.length" class="text-muted-foreground">None.</p>
              </CardContent>
            </Card>
          </TabsContent>
        </Tabs>

        <aside class="space-y-4">
          <Card>
            <CardHeader
              ><CardTitle>{{ r.household.name }} household</CardTitle
              ><CardDescription>{{ r.household.city }}</CardDescription></CardHeader
            >
            <CardContent class="space-y-3 text-sm">
              <div v-for="a in r.household.adults" :key="a.firstName">
                <p class="font-medium">{{ a.firstName }} {{ a.lastName }}</p>
                <p class="text-muted-foreground">{{ a.role }}</p>
              </div>
              <div class="flex flex-col items-start">
                <Button variant="link" as-child class="h-auto px-0 py-1 text-foreground"
                  ><a :href="`mailto:${r.household.email}`"><Mail />{{ r.household.email }}</a></Button
                >
                <Button variant="link" as-child class="h-auto px-0 py-1 text-foreground"
                  ><a :href="`tel:${r.household.phone}`"><Phone />{{ r.household.phone }}</a></Button
                >
              </div>
              <p v-if="r.household.salesforceId" class="text-xs text-muted-foreground">
                Salesforce {{ r.household.salesforceId }}
              </p>
            </CardContent>
          </Card>
          <Card v-if="r.household.otherRegistrations.length">
            <CardHeader><CardTitle>Other registrations</CardTitle></CardHeader>
            <CardContent class="space-y-2 text-sm">
              <RouterLink
                v-for="o in r.household.otherRegistrations"
                :key="o.id"
                :to="`/admin/registrations/${o.id}`"
                class="flex items-center justify-between gap-2 rounded-md p-1 hover:bg-muted"
              >
                <span
                  >{{ o.participant }} <span class="text-muted-foreground">· {{ o.program }}</span></span
                >
                <StatusBadge :status="o.status" />
              </RouterLink>
            </CardContent>
          </Card>
          <Card>
            <CardHeader><CardTitle>Session</CardTitle></CardHeader>
            <CardContent class="text-sm text-muted-foreground"
              >{{ dateRange(r.session.startDate, r.session.endDate) }}<br />DOB
              {{ date(r.participant.dateOfBirth) }}</CardContent
            >
          </Card>
        </aside>
      </div>
    </template>
  </div>
</template>
