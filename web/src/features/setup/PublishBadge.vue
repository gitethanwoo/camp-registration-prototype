<script setup lang="ts">
import { Badge } from '@/components/ui/badge'

// Draft, Pending approval, Published; and the same words for rule and version states.
const props = defineProps<{ state: string; label?: string }>()
const tone: Record<string, string> = {
  Draft: 'bg-muted text-muted-foreground',
  PendingApproval: 'border-amber-200 bg-amber-50 text-amber-800',
  'Pending approval': 'border-amber-200 bg-amber-50 text-amber-800',
  Published: 'border-emerald-200 bg-emerald-50 text-emerald-800',
  Active: 'border-emerald-200 bg-emerald-50 text-emerald-800',
  Inactive: 'bg-muted text-muted-foreground',
  Archived: 'bg-muted text-muted-foreground',
}
const words: Record<string, string> = {
  PendingApproval: 'Pending approval',
  Published: 'Published',
  Archived: 'Retired',
}
</script>

<template>
  <Badge variant="outline" :class="tone[props.state] ?? ''">{{
    props.label ?? words[props.state] ?? props.state
  }}</Badge>
</template>
