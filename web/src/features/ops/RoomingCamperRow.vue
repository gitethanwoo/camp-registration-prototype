<script setup lang="ts">
import { BedDouble, CircleCheck, EllipsisVertical, TriangleAlert } from '@lucide/vue'
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import type { RoomingCabin, RoomingCamper } from './types'

// A camper on the O3 board: cabinmate request status and a "Move to…" menu limited to the camper's gender.
const props = defineProps<{ camper: RoomingCamper; cabins: RoomingCabin[]; canEdit: boolean }>()
const emit = defineEmits<{ move: [cabinId: number | null] }>()
const choices = computed(() => props.cabins.filter((c) => c.gender === props.camper.gender))
</script>

<template>
  <li class="flex items-start gap-3 py-2" :data-testid="`room-camper-${camper.registrationId}`">
    <div class="min-w-0 flex-1">
      <p class="text-sm font-medium">
        {{ camper.name }}
        <Badge v-if="camper.needsReview" variant="outline" class="ml-1 border-amber-300 bg-amber-50 text-amber-800">
          Needs review
        </Badge>
      </p>
      <p class="text-xs text-muted-foreground">Grade {{ camper.grade }}</p>
      <p
        v-for="q in camper.requests"
        :key="q.withRegistrationId"
        class="mt-1 flex gap-1.5 text-xs"
        :class="q.status === 'Met' ? 'text-emerald-800' : 'text-amber-800'"
      >
        <CircleCheck v-if="q.status === 'Met'" class="size-3.5 shrink-0" aria-hidden="true" />
        <TriangleAlert v-else class="size-3.5 shrink-0" aria-hidden="true" />
        <span
          >Requested {{ q.with }} · {{ q.status }}<template v-if="q.status !== 'Met'">. {{ q.why }}</template></span
        >
      </p>
    </div>
    <DropdownMenu v-if="canEdit">
      <DropdownMenuTrigger as-child>
        <Button variant="ghost" size="icon" class="size-8" :aria-label="`Move ${camper.name} to another cabin`">
          <EllipsisVertical class="size-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>Move {{ camper.name }} to…</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          v-for="c in choices"
          :key="c.id"
          :disabled="c.id === camper.cabinId || c.taken >= c.beds"
          @select="emit('move', c.id)"
        >
          <BedDouble class="size-4" />{{ c.name }}
          <span class="ml-auto text-xs text-muted-foreground tabular-nums">{{ c.taken }} / {{ c.beds }}</span>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem :disabled="camper.cabinId === null" @select="emit('move', null)"
          >Take out of cabin</DropdownMenuItem
        >
      </DropdownMenuContent>
    </DropdownMenu>
  </li>
</template>
