<script setup lang="ts">
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

// Status vocabulary from the global contract, with the exact display words.
const labels: Record<string, string> = {
  Draft: 'Draft', ApplicationPending: 'Application pending', Approved: 'Approved', PaymentPending: 'Payment pending',
  Confirmed: 'Confirmed', Waitlisted: 'Waitlisted', OfferedSpot: 'Offered spot', Cancelled: 'Cancelled', Transferred: 'Transferred',
  Waiting: 'Waitlisted', Offered: 'Offered spot', Expired: 'Offer expired', Removed: 'Removed',
  Complete: 'Complete', Incomplete: 'Incomplete', Missing: 'Missing', NotRequired: 'Not required',
  Paid: 'Paid', Declined: 'Declined', Scheduled: 'Scheduled', Failed: 'Failed',
}
const tone: Record<string, string> = {
  Confirmed: 'bg-emerald-50 text-emerald-800 border-emerald-200', Complete: 'bg-emerald-50 text-emerald-800 border-emerald-200', Paid: 'bg-emerald-50 text-emerald-800 border-emerald-200',
  PaymentPending: 'bg-amber-50 text-amber-800 border-amber-200', Incomplete: 'bg-amber-50 text-amber-800 border-amber-200', Offered: 'bg-sky-50 text-sky-800 border-sky-200', OfferedSpot: 'bg-sky-50 text-sky-800 border-sky-200', Scheduled: 'bg-secondary text-secondary-foreground',
  Waitlisted: 'bg-violet-50 text-violet-800 border-violet-200', Waiting: 'bg-violet-50 text-violet-800 border-violet-200',
  Missing: 'bg-red-50 text-red-800 border-red-200', Declined: 'bg-red-50 text-red-800 border-red-200', Failed: 'bg-red-50 text-red-800 border-red-200',
  Cancelled: 'bg-muted text-muted-foreground', Removed: 'bg-muted text-muted-foreground', Expired: 'bg-muted text-muted-foreground', NotRequired: 'bg-muted text-muted-foreground',
}
const props = defineProps<{ status: string, label?: string }>()
const text = computed(() => props.label ?? labels[props.status] ?? props.status)
</script>

<template>
  <Badge variant="outline" :class="cn('font-medium', tone[status])">{{ text }}</Badge>
</template>
