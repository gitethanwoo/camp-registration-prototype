<script setup lang="ts">
import { ArrowLeft, CircleAlert, CircleCheck, LoaderCircle, Receipt, TriangleAlert } from '@lucide/vue'
import { computed, onMounted, ref, useTemplateRef } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, api } from '@/lib/api'
import { date, dateRange, money } from '@/lib/format'
import FiservCardFields, { type CardFields } from './FiservCardFields.vue'
import PaymentBadge from './PaymentBadge.vue'
import ReceiptDialog from './ReceiptDialog.vue'
import type { HistoryEntry, Payments, PayResult } from './types'

const props = defineProps<{ code: string }>()
const data = ref<Payments | null>(null)
const notFound = ref(false)

async function load() {
  try {
    data.value = await api.get<Payments>(`/family/registrations/${props.code}/payments`)
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) notFound.value = true
    else throw e
  }
}
onMounted(load)

const active = computed(() => data.value?.lines.filter((l) => !l.cancelled) ?? [])
const firstLabel = computed(() => data.value?.history.find((h) => h.kind === 'Charge')?.label ?? 'Paid')

// ── Paying ───────────────────────────────────────────────────────────────
const payOpen = ref(false)
const retrySequence = ref<number | null>(null)
const card = ref<CardFields>({ number: '', expiry: '', cvc: '', zip: '' })
const fields = useTemplateRef<InstanceType<typeof FiservCardFields>>('fields')
const processing = ref(false)
const decline = ref<string | null>(null)
// One key per attempt: a double-click or a network retry reuses it, so the card is charged once.
// After a decline the next attempt is a new payment, so it gets a new key.
let key = crypto.randomUUID()

const amount = computed(() => {
  const d = data.value
  if (!d) return 0
  if (retrySequence.value == null) return d.balanceCents
  const i = d.installments.find((x) => x.sequence === retrySequence.value)
  return Math.min(i?.amountCents ?? 0, d.balanceCents)
})

function openPay(sequence: number | null) {
  retrySequence.value = sequence
  decline.value = null
  payOpen.value = true
}

async function pay() {
  if (processing.value || !fields.value?.complete) return
  processing.value = true
  decline.value = null
  try {
    const cardToken = await fields.value.tokenize()
    const res = await api.post<PayResult>(`/family/registrations/${props.code}/pay`, {
      idempotencyKey: key,
      cardToken,
      installmentSequence: retrySequence.value,
    })
    key = crypto.randomUUID()
    payOpen.value = false
    card.value = { number: '', expiry: '', cvc: '', zip: '' }
    toast.success(`${money(res.amountCents)} paid with card ending ${res.cardLast4}`)
    await load()
  } catch (e) {
    if (e instanceof ApiError && e.status === 402) {
      key = crypto.randomUUID()
      decline.value = `${e.message || 'Card declined.'} Your balance hasn’t changed.`
    } else if (e instanceof ApiError && e.status === 409) {
      payOpen.value = false
      toast.info(e.message)
      await load()
    } else {
      decline.value = e instanceof Error ? e.message : 'The payment didn’t go through.'
    }
  } finally {
    processing.value = false
  }
}

// ── Receipts ─────────────────────────────────────────────────────────────
const receipt = ref<HistoryEntry | null>(null)
const receiptOpen = ref(false)
function showReceipt(h: HistoryEntry) {
  receipt.value = h
  receiptOpen.value = true
}
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-8 md:py-12">
    <Button variant="link" as-child class="h-auto p-0 text-muted-foreground">
      <RouterLink :to="`/family/registrations/${code}`"><ArrowLeft />Registration {{ code }}</RouterLink>
    </Button>

    <Alert v-if="notFound" variant="destructive" class="mt-6">
      <CircleAlert />
      <AlertDescription>No registration {{ code }} in your household.</AlertDescription>
    </Alert>

    <div v-else-if="!data" class="mt-6 grid gap-6 lg:grid-cols-[2fr_3fr]">
      <Skeleton class="h-80 rounded-xl" /><Skeleton class="h-80 rounded-xl" />
    </div>

    <template v-else>
      <h1 class="mt-4 text-3xl font-semibold tracking-tight md:text-4xl">{{ data.program }} payments</h1>

      <div class="mt-8 grid items-start gap-6 lg:grid-cols-[2fr_3fr]">
        <Card>
          <CardHeader>
            <CardTitle>{{ data.program }}</CardTitle>
            <CardDescription>{{ dateRange(data.session.startDate, data.session.endDate) }}</CardDescription>
            <CardAction><PaymentBadge :status="data.paymentStatus" /></CardAction>
          </CardHeader>
          <CardContent class="space-y-3 text-sm">
            <div v-for="l in data.lines" :key="l.name" class="flex justify-between gap-4">
              <span :class="l.cancelled ? 'text-muted-foreground line-through' : ''"
                >{{ l.name }}<span v-if="l.gradeLabel" class="text-muted-foreground"> ({{ l.gradeLabel }})</span></span
              >
              <span class="tabular-nums">{{ money(l.priceCents) }}</span>
            </div>
            <Separator />
            <div v-if="data.discountCents" class="flex justify-between gap-4 text-emerald-700">
              <span
                >Discount<template v-if="data.discountCode"> ({{ data.discountCode }})</template></span
              >
              <span class="tabular-nums">−{{ money(data.discountCents) }}</span>
            </div>
            <div class="flex justify-between gap-4">
              <span>Total ({{ active.length }} {{ active.length === 1 ? 'participant' : 'participants' }})</span>
              <span class="tabular-nums">{{ money(data.totalCents) }}</span>
            </div>
            <div class="flex justify-between gap-4">
              <span>Paid</span><span class="tabular-nums">{{ money(data.paidCents) }}</span>
            </div>
            <div class="flex justify-between gap-4 text-base font-semibold">
              <span>Balance remaining</span><span class="tabular-nums">{{ money(data.balanceCents) }}</span>
            </div>
            <p v-if="data.installments.length" class="text-muted-foreground">
              Payment plan: {{ data.installments.length }} × {{ money(data.installments[0]?.amountCents) }}, last one
              {{ date(data.session.balanceDueDate) }}
            </p>
            <p v-else-if="data.balanceCents > 0" class="text-muted-foreground">
              Due by {{ date(data.session.balanceDueDate) }}
            </p>
            <Button v-if="data.balanceCents > 0" class="mt-2 w-full" @click="openPay(null)">
              Pay {{ money(data.balanceCents) }} now
            </Button>
            <p v-else class="flex items-center gap-2 pt-1 text-emerald-700">
              <CircleCheck class="size-4" />Paid in full
            </p>
          </CardContent>
        </Card>

        <div class="space-y-6">
          <Alert v-if="data.failedInstallment" class="border-amber-300 bg-amber-50 text-amber-900">
            <TriangleAlert />
            <AlertTitle class="text-base">{{ date(data.failedInstallment.dueDate) }} installment failed.</AlertTitle>
            <AlertDescription class="text-amber-900">
              <p>
                Retry within 7 days, through {{ date(data.failedInstallment.graceUntil) }}, to keep your spot. Your
                other payments stay on schedule.
              </p>
              <Button size="sm" class="mt-2" @click="openPay(data.failedInstallment.sequence)"
                >Retry {{ money(data.failedInstallment.amountCents) }}</Button
              >
            </AlertDescription>
          </Alert>

          <Card>
            <CardHeader><CardTitle>Payment history</CardTitle></CardHeader>
            <CardContent>
              <p v-if="!data.history.length" class="text-sm text-muted-foreground">No payments yet.</p>
              <ul class="divide-y">
                <li v-for="h in data.history" :key="h.id" class="flex items-center gap-x-4 gap-y-2 py-3">
                  <span class="hidden w-28 shrink-0 text-sm text-muted-foreground sm:block">{{
                    date(h.createdAt)
                  }}</span>
                  <div class="min-w-0 flex-1">
                    <p class="text-sm font-medium">
                      {{ h.label }} · {{ h.kind === 'Refund' ? '−' : '' }}{{ money(h.amountCents) }}
                    </p>
                    <p class="text-xs text-muted-foreground">
                      <span class="sm:hidden">{{ date(h.createdAt) }} · </span
                      ><template v-if="h.cardLast4">Card ending {{ h.cardLast4 }}</template>
                    </p>
                  </div>
                  <PaymentBadge :status="h.kind === 'Refund' ? 'Refunded' : 'Paid'" />
                  <Button
                    size="sm"
                    variant="outline"
                    :aria-label="`Receipt for ${h.label} on ${date(h.createdAt)}`"
                    @click="showReceipt(h)"
                    ><Receipt />Receipt</Button
                  >
                </li>
              </ul>
            </CardContent>
          </Card>

          <Card v-if="data.installments.length">
            <CardHeader>
              <CardTitle>Scheduled payments</CardTitle>
              <CardDescription v-if="data.nextCharge"
                >Next: {{ money(data.nextCharge.amountCents) }} on {{ date(data.nextCharge.dueDate) }}, charged to the
                card you paid the {{ firstLabel.toLowerCase() }} with.</CardDescription
              >
            </CardHeader>
            <CardContent>
              <ul class="divide-y">
                <li v-for="i in data.installments" :key="i.sequence" class="flex items-center gap-4 py-3">
                  <span class="w-28 text-sm">{{ date(i.dueDate) }}</span>
                  <span class="w-16 text-sm tabular-nums">{{ money(i.amountCents) }}</span>
                  <StatusBadge :status="i.status" :label="i.status === 'Failed' ? 'Installment failed' : undefined" />
                  <Button v-if="i.status === 'Failed'" size="sm" class="ml-auto" @click="openPay(i.sequence)"
                    >Retry</Button
                  >
                </li>
              </ul>
            </CardContent>
          </Card>
        </div>
      </div>

      <ReceiptDialog v-model:open="receiptOpen" :entry="receipt" :payments="data" />
    </template>

    <Dialog v-model:open="payOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{{ retrySequence == null ? 'Pay balance' : `Retry installment ${retrySequence}` }}</DialogTitle>
          <DialogDescription>
            {{ money(amount) }} for {{ data?.program }}.
            <template v-if="retrySequence == null && data?.installments.length"
              >Paying the whole balance cancels the remaining scheduled payments.</template
            >
          </DialogDescription>
        </DialogHeader>
        <form id="pay-form" @submit.prevent="pay">
          <FiservCardFields ref="fields" v-model="card" :disabled="processing" />
        </form>
        <Alert v-if="decline" variant="destructive" role="alert">
          <CircleAlert />
          <AlertDescription>{{ decline }}</AlertDescription>
        </Alert>
        <DialogFooter>
          <Button variant="outline" :disabled="processing" @click="payOpen = false">Cancel</Button>
          <Button type="submit" form="pay-form" :disabled="processing || !fields?.complete">
            <LoaderCircle v-if="processing" class="animate-spin" />Pay {{ money(amount) }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
