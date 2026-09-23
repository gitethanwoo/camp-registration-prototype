<script setup lang="ts">
import { AlertTriangle } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Progress } from '@/components/ui/progress'
import type { PoolAvailability } from '@/lib/types'
import { cn } from '@/lib/utils'

// P2 · Live availability, per pool. No seat-hold countdown (Won't Have).
defineProps<{ pools: PoolAvailability[], ctaLabel?: string, ctaDisabled?: boolean }>()
defineEmits<{ select: [pool: PoolAvailability] }>()
</script>

<template>
  <ul class="divide-y rounded-lg border">
    <li v-for="p in pools" :key="p.id" class="p-4">
      <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
        <div class="w-24 shrink-0">
          <div class="font-medium">{{ p.name }}</div>
          <div class="text-sm text-muted-foreground tabular-nums">{{ p.reserved }} / {{ p.capacity }}</div>
        </div>
        <div class="order-3 w-full min-w-0 sm:order-none sm:w-auto sm:flex-1">
          <Progress
            :model-value="Math.round((p.reserved / p.capacity) * 100)"
            :aria-label="`${p.name}: ${p.reserved} of ${p.capacity} taken`"
            :class="cn('h-2', p.state === 'full' && '[&>*]:bg-destructive', p.state === 'low' && '[&>*]:bg-amber-500', p.state === 'open' && '[&>*]:bg-emerald-600')"
          />
          <p :class="cn('mt-1 text-sm', p.state === 'full' ? 'font-medium text-destructive' : p.state === 'low' ? 'font-medium text-amber-700' : 'text-muted-foreground')">
            <template v-if="p.state === 'full'">Full · {{ p.waitlisted }} on waitlist</template>
            <template v-else-if="p.state === 'low'">Only {{ p.remaining }} {{ p.remaining === 1 ? 'spot' : 'spots' }} left</template>
            <template v-else>{{ p.remaining }} spots left</template>
          </p>
        </div>
        <Button
          v-if="ctaLabel"
          class="ml-auto shrink-0 sm:ml-0"
          :variant="p.state === 'full' ? 'outline' : 'default'"
          :disabled="ctaDisabled"
          @click="$emit('select', p)"
        >
          {{ p.state === 'full' ? 'Join waitlist' : ctaLabel }}
        </Button>
      </div>
      <div v-if="p.state === 'full'" class="mt-3 flex gap-2 rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
        <AlertTriangle class="mt-0.5 size-4 shrink-0" />
        <p>{{ p.name }} has reached capacity. Registering a camper in this group adds them to the waitlist; staff offer open spots in order and you'll be emailed with a deadline to confirm. You won't be charged unless you accept.</p>
      </div>
    </li>
  </ul>
</template>
