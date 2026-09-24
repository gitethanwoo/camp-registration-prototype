<script setup lang="ts">
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'

// One place for the finance screens' status colors, so the same word always looks the same.
const props = defineProps<{ status: string }>()

const good = 'border-emerald-200 bg-emerald-50 text-emerald-800'
const waiting = 'border-amber-200 bg-amber-50 text-amber-800'
const bad = 'border-red-200 bg-red-50 text-red-800'
const info = 'border-sky-200 bg-sky-50 text-sky-800'
const muted = 'bg-muted text-muted-foreground'
const tones: Record<string, string> = {
  Matched: good,
  Posted: good,
  Approved: good,
  Paid: good,
  Unmatched: waiting,
  Pending: waiting,
  'Needs review': waiting,
  'In review': waiting,
  'In grace period': waiting,
  Resolved: info,
  'Retry scheduled': info,
  Scheduled: muted,
  Failed: bad,
  'Installment failed': bad,
  'Needs attention': bad,
  Denied: muted,
  'Not created': muted,
}
const tone = computed(() => tones[props.status] ?? muted)
</script>

<template>
  <Badge variant="outline" :class="['whitespace-nowrap', tone]">{{ status }}</Badge>
</template>
