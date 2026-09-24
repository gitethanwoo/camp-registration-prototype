<script setup lang="ts">
import { EyeOff, HeartPulse } from '@lucide/vue'
import { Badge } from '@/components/ui/badge'

// Health-data access for one staff member: full details, or completion status only.
defineProps<{ on: boolean; muted?: boolean }>()
</script>

<template>
  <Badge
    variant="outline"
    :class="
      muted
        ? 'border-transparent bg-muted text-muted-foreground'
        : on
          ? 'border-sky-200 bg-sky-50 text-sky-800'
          : 'text-muted-foreground'
    "
  >
    <HeartPulse v-if="on" aria-hidden="true" />
    <EyeOff v-else aria-hidden="true" />
    {{ on ? 'Health details' : 'Completion status only' }}
  </Badge>
</template>
