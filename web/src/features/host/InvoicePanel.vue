<script setup lang="ts">
import { AlertTriangle, CircleCheck, Lock } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, dateTime, money } from '@/lib/format'
import { cardComplete, emptyCard, newKey, tokenize } from './card'
import CardFields from './CardFields.vue'
import InvoiceBadge from './InvoiceBadge.vue'
import type { InvoiceDetail, PayResult } from './types'

// One invoice: line items, payment history, and Pay for the remaining balance. The amount is
// the server's balance; the page only sends a card token and a key for this attempt.
const props = defineProps<{ id: number }>()
const emit = defineEmits<{ paid: [] }>()

const detail = ref<InvoiceDetail | null>(null)
const error = ref<string | null>(null)
const card = ref(emptyCard())
const paying = ref(false)
const declined = ref<string | null>(null)
// One key per attempt: a retry after a network failure reuses it, a new card gets a new one.
let key = newKey()

async function load() {
  try {
    detail.value = await api.get<InvoiceDetail>(`/host/invoices/${props.id}`)
  } catch (e) {
    error.value =
      e instanceof ApiError && e.status === 404 ? 'That invoice was not found.' : "Couldn't load the invoice."
  }
}
onMounted(load)

const inv = computed(() => detail.value?.invoice ?? null)
const due = computed(() => (inv.value?.balanceCents ?? 0) > 0)

async function pay() {
  if (!inv.value) return
  paying.value = true
  declined.value = null
  try {
    const token = await tokenize(card.value)
    const r = await api.post<PayResult>(`/host/invoices/${props.id}/pay`, {
      idempotencyKey: key,
      cardToken: token,
    })
    key = newKey()
    toast.success(`${money(r.amountCents)} paid on card ending ${r.cardLast4}.`)
    card.value = emptyCard()
    await load()
    emit('paid')
  } catch (e) {
    if (e instanceof ApiError && e.status === 402) {
      key = newKey()
      declined.value = e.message
      await load()
    } else if (e instanceof ApiError && e.status === 409) {
      toast.error(e.message)
      await load()
      emit('paid')
    } else {
      toast.error(e instanceof ApiError ? e.message : "Couldn't reach the payment service. Try again.")
    }
  } finally {
    paying.value = false
  }
}
</script>

<template>
  <Card v-if="detail && inv" data-testid="invoice-detail">
    <CardHeader>
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0">
          <CardTitle class="text-xl"
            >Invoice <span class="whitespace-nowrap">{{ inv.number }}</span></CardTitle
          >
          <CardDescription class="mt-1">{{ detail.organization }}</CardDescription>
        </div>
        <div class="shrink-0 text-right">
          <InvoiceBadge :status="inv.status" />
          <p class="mt-1 text-xs text-muted-foreground">
            {{ due ? `Due ${date(inv.dueDate)}` : `Paid ${date(inv.paidOn)}` }}
          </p>
        </div>
      </div>
    </CardHeader>
    <CardContent class="space-y-5">
      <div>
        <p class="font-semibold">{{ inv.description }}</p>
        <p class="text-sm text-muted-foreground">
          <template v-if="detail.event"
            >{{ dateRange(detail.event.startDate, detail.event.endDate) }} · {{ detail.event.location }}</template
          >
          <template v-else>{{ inv.period }}</template>
        </p>
        <p class="text-sm text-muted-foreground">Issued {{ date(detail.issuedOn) }}</p>
      </div>
      <Separator />

      <section class="space-y-2">
        <h3 class="font-semibold">Line items</h3>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Description</TableHead>
              <TableHead class="text-right">Amount</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="l in detail.lines" :key="l.id">
              <TableCell class="whitespace-normal">{{ l.description }}</TableCell>
              <TableCell class="text-right tabular-nums">{{ money(l.amountCents) }}</TableCell>
            </TableRow>
          </TableBody>
          <TableFooter>
            <TableRow>
              <TableCell class="font-semibold">Total</TableCell>
              <TableCell class="text-right font-semibold tabular-nums">{{ money(inv.totalCents) }}</TableCell>
            </TableRow>
            <TableRow v-if="inv.paidCents > 0 && due">
              <TableCell>Paid so far</TableCell>
              <TableCell class="text-right tabular-nums">−{{ money(inv.paidCents) }}</TableCell>
            </TableRow>
            <TableRow v-if="inv.paidCents > 0 && due">
              <TableCell class="font-semibold">Balance due</TableCell>
              <TableCell class="text-right font-semibold tabular-nums">{{ money(inv.balanceCents) }}</TableCell>
            </TableRow>
          </TableFooter>
        </Table>
      </section>

      <form v-if="due" class="space-y-4" @submit.prevent="pay">
        <CardFields v-model="card" :disabled="paying" />
        <Button type="submit" size="lg" class="w-full" :disabled="paying || !cardComplete(card)"
          ><Lock />{{ paying ? 'Processing…' : `Pay ${money(inv.balanceCents)}` }}</Button
        >
        <Alert v-if="declined" variant="destructive" data-testid="pay-declined">
          <AlertTriangle />
          <AlertTitle>Payment declined; invoice remains due.</AlertTitle>
          <AlertDescription>{{ declined }}</AlertDescription>
        </Alert>
      </form>
      <Alert v-else class="border-emerald-200 bg-emerald-50 text-emerald-900" data-testid="invoice-paid">
        <CircleCheck />
        <AlertTitle>Paid in full</AlertTitle>
        <AlertDescription class="text-emerald-900"
          >{{ money(inv.totalCents) }} received. Nothing is due.</AlertDescription
        >
      </Alert>

      <section v-if="detail.payments.length" class="space-y-2">
        <h3 class="font-semibold">Payment history</h3>
        <ul class="divide-y rounded-lg border text-sm">
          <li v-for="p in detail.payments" :key="p.id" class="flex items-start justify-between gap-3 px-3 py-2">
            <div class="min-w-0">
              <p>
                {{ p.status === 'Succeeded' ? 'Paid' : 'Declined' }}
                <template v-if="p.cardLast4"> · card ending {{ p.cardLast4 }}</template>
              </p>
              <p class="text-xs text-muted-foreground">{{ dateTime(p.createdAt) }} · {{ p.paidBy }}</p>
            </div>
            <span :class="['tabular-nums', p.status === 'Declined' ? 'text-muted-foreground line-through' : '']">{{
              money(p.amountCents)
            }}</span>
          </li>
        </ul>
      </section>
    </CardContent>
  </Card>
  <Alert v-else-if="error" variant="destructive">
    <AlertTriangle />
    <AlertDescription>{{ error }}</AlertDescription>
  </Alert>
  <Skeleton v-else class="h-96 rounded-xl" />
</template>
