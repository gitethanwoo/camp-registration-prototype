<script setup lang="ts">
import { Printer } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Separator } from '@/components/ui/separator'
import { date, dateRange, dateTime, money } from '@/lib/format'
import type { HistoryEntry, Payments } from './types'

const open = defineModel<boolean>('open', { required: true })
defineProps<{ entry: HistoryEntry | null; payments: Payments }>()

function print() {
  window.print()
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent v-if="entry" class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>{{ entry.kind === 'Refund' ? 'Refund receipt' : 'Receipt' }}</DialogTitle>
        <DialogDescription>Receipt {{ entry.receiptNumber }} · {{ dateTime(entry.createdAt) }}</DialogDescription>
      </DialogHeader>
      <div class="space-y-4 text-sm">
        <div>
          <p class="font-medium">WinShape Camps</p>
          <p class="text-muted-foreground">
            {{ payments.program }} · {{ payments.session.name }} ·
            {{ dateRange(payments.session.startDate, payments.session.endDate) }}
          </p>
        </div>
        <div>
          <p class="text-muted-foreground">Billed to</p>
          <p>{{ payments.household.payer }} · {{ payments.household.email }}</p>
        </div>
        <Separator />
        <dl class="space-y-2">
          <div v-for="l in payments.lines.filter((x) => !x.cancelled)" :key="l.name" class="flex justify-between gap-4">
            <dt>
              {{ l.name }}<span v-if="l.gradeLabel" class="text-muted-foreground"> ({{ l.gradeLabel }})</span>
            </dt>
            <dd class="tabular-nums">{{ money(l.priceCents - l.discountCents) }}</dd>
          </div>
          <div class="flex justify-between gap-4 text-muted-foreground">
            <dt>Registration total</dt>
            <dd class="tabular-nums">{{ money(payments.totalCents) }}</dd>
          </div>
        </dl>
        <Separator />
        <dl class="space-y-2">
          <div class="flex justify-between gap-4 font-medium">
            <dt>{{ entry.label }}</dt>
            <dd class="tabular-nums">{{ entry.kind === 'Refund' ? '−' : '' }}{{ money(entry.amountCents) }}</dd>
          </div>
          <div v-if="entry.cardLast4" class="flex justify-between gap-4 text-muted-foreground">
            <dt>Card</dt>
            <dd>Card ending {{ entry.cardLast4 }}</dd>
          </div>
          <div v-if="entry.processorRef" class="flex justify-between gap-4 text-muted-foreground">
            <dt>Fiserv reference</dt>
            <dd class="truncate font-mono text-xs">{{ entry.processorRef }}</dd>
          </div>
          <div class="flex justify-between gap-4 text-muted-foreground">
            <dt>Balance remaining today</dt>
            <dd class="tabular-nums">{{ money(payments.balanceCents) }}</dd>
          </div>
        </dl>
        <p class="text-xs text-muted-foreground">
          Confirmation {{ payments.confirmationCode }}. Balance due by {{ date(payments.session.balanceDueDate) }}.
        </p>
      </div>
      <DialogFooter>
        <Button variant="outline" @click="print"><Printer />Print</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
