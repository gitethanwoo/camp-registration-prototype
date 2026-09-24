<script setup lang="ts">
import { ArrowRightLeft, CircleCheck, EllipsisVertical, Sparkles, TriangleAlert } from '@lucide/vue'
import { computed } from 'vue'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { initials } from '@/lib/format'
import type { GroupBoardView, GroupCamper } from './types'

// One camper on the O2 board, with a keyboard-reachable "Move to…" menu (dragging is optional).
const props = defineProps<{ camper: GroupCamper; groups: GroupBoardView['groups']; canEdit: boolean }>()
const emit = defineEmits<{ move: [groupId: number | null] }>()

const apart = computed(() => props.camper.requests.filter((q) => !q.met))
const met = computed(() => props.camper.requests.length > 0 && apart.value.length === 0)
const suggested = computed(() => props.groups.find((g) => g.id === props.camper.suggestedGroupId) ?? null)

function onDragStart(e: DragEvent) {
  e.dataTransfer?.setData('text/plain', String(props.camper.registrationId))
}
</script>

<template>
  <li
    class="rounded-lg border bg-card p-2.5 shadow-xs"
    :class="apart.length ? 'border-amber-300' : ''"
    :draggable="canEdit"
    :data-testid="`camper-${camper.registrationId}`"
    @dragstart="onDragStart"
  >
    <div class="flex items-center gap-2">
      <Avatar class="hidden size-8 2xl:flex"
        ><AvatarFallback class="text-xs">{{ initials(camper.name) }}</AvatarFallback></Avatar
      >
      <div class="min-w-0 flex-1">
        <p class="truncate text-sm font-medium">{{ camper.name }}</p>
        <p class="text-xs text-muted-foreground">Grade {{ camper.grade }}</p>
        <p v-if="met" class="flex items-center gap-1 text-xs text-emerald-700">
          <CircleCheck class="size-3" aria-hidden="true" />Request met
        </p>
      </div>
      <DropdownMenu v-if="canEdit">
        <DropdownMenuTrigger as-child>
          <Button variant="ghost" size="icon" class="size-8" :aria-label="`Move ${camper.name} to another group`">
            <EllipsisVertical class="size-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuLabel>Move {{ camper.name }} to…</DropdownMenuLabel>
          <DropdownMenuSeparator />
          <DropdownMenuItem
            v-for="g in groups"
            :key="g.id"
            :disabled="g.id === camper.groupId || g.count >= g.capacity"
            @select="emit('move', g.id)"
          >
            <ArrowRightLeft class="size-4" />{{ g.name }}
            <span class="ml-auto text-xs text-muted-foreground tabular-nums">{{ g.count }} / {{ g.capacity }}</span>
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem :disabled="camper.groupId === null" @select="emit('move', null)">
            Remove from group
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
    <p v-for="q in apart" :key="q.registrationId" class="mt-2 flex gap-1.5 text-xs text-amber-800">
      <TriangleAlert class="size-3.5 shrink-0" aria-hidden="true" />Asked to be with {{ q.name }} ({{ q.group }})
    </p>
    <p v-if="suggested" class="mt-2 flex gap-1.5 text-xs text-sky-800">
      <Sparkles class="size-3.5 shrink-0" aria-hidden="true" />Suggested: {{ suggested.name }}.
      {{ camper.suggestionReason }}
    </p>
  </li>
</template>
