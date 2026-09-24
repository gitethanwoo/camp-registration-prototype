<script setup lang="ts">
import { MapPin, TriangleAlert, UserRound } from '@lucide/vue'
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { api } from '@/lib/api'
import { describe } from '@/features/ops/useOpsData'
import type { Schedule, ScheduleCamper, ScheduleCell, ScheduleRow } from './types'

// O4 detail for one activity in one period: who's in it, conflicts, and moves within the period.
const props = defineProps<{
  schedule: Schedule
  row: ScheduleRow | null
  cell: ScheduleCell | null
  canEdit: boolean
}>()
const emit = defineEmits<{ close: []; changed: [] }>()
const busy = ref(false)
const time = computed(() => props.schedule.periods.find((p) => p.period === props.cell?.period)?.time ?? '')
const over = computed(() => (props.cell ? props.cell.assigned - props.cell.capacity : 0))

const outside = (c: ScheduleCamper) =>
  !!props.row && c.grade !== null && (c.grade < props.row.gradeMin || c.grade > props.row.gradeMax)
/** Where this camper could move in the same period: fits their grade and has room. */
function targets(c: ScheduleCamper) {
  const cell = props.cell
  if (!cell) return []
  return props.schedule.rows
    .filter(
      (r) =>
        r.activityId !== props.row?.activityId &&
        (c.grade === null || (c.grade >= r.gradeMin && c.grade <= r.gradeMax)),
    )
    .map((r) => ({ row: r, cell: r.cells.find((x) => x.period === cell.period) }))
    .filter((x): x is { row: ScheduleRow; cell: ScheduleCell } => !!x.cell && x.cell.assigned < x.cell.capacity)
}
/** The other activity a double-booked camper is in this period. */
const otherBooking = (c: ScheduleCamper) =>
  props.schedule.rows.find(
    (r) =>
      r.activityId !== props.row?.activityId &&
      r.cells.some(
        (x) => x.period === props.cell?.period && x.campers.some((p) => p.registrationId === c.registrationId),
      ),
  )?.name

async function move(c: ScheduleCamper, toSlotId: string) {
  const cell = props.cell
  if (!cell) return
  busy.value = true
  try {
    await api.post(`/admin/ops/sessions/${props.schedule.session.id}/activities/move`, {
      registrationId: c.registrationId,
      fromSlotId: cell.slotId,
      toSlotId: Number(toSlotId),
    })
    const to = props.schedule.rows.find((r) => r.cells.some((x) => x.slotId === Number(toSlotId)))
    toast.success(`Moved ${c.name} to ${to?.name ?? 'the new activity'} in Period ${cell.period}.`)
    emit('changed')
  } catch (e) {
    toast.error(describe(e, "The move didn't save. Refresh and try again."))
  } finally {
    busy.value = false
  }
}
async function keep(c: ScheduleCamper) {
  const cell = props.cell
  if (!cell) return
  busy.value = true
  try {
    await api.post(`/admin/ops/sessions/${props.schedule.session.id}/activities/keep`, {
      registrationId: c.registrationId,
      period: cell.period,
      keepSlotId: cell.slotId,
    })
    toast.success(`${c.name} stays in ${props.row?.name}; the other booking is removed.`)
    emit('changed')
  } catch (e) {
    toast.error(describe(e, "That didn't save. Refresh and try again."))
  } finally {
    busy.value = false
  }
}
const sorted = computed(() =>
  (props.cell?.campers ?? []).toSorted(
    (a, b) => Number(b.doubleBooked || outside(b)) - Number(a.doubleBooked || outside(a)),
  ),
)
</script>

<template>
  <Sheet :open="!!cell" @update:open="(v) => !v && emit('close')">
    <SheetContent v-if="cell && row" class="w-full overflow-y-auto sm:max-w-lg">
      <SheetHeader>
        <SheetTitle class="text-xl">{{ row.name }} · Period {{ cell.period }}</SheetTitle>
        <SheetDescription>{{ time }} · {{ row.grades }} · {{ schedule.block.name }}</SheetDescription>
      </SheetHeader>
      <div class="space-y-4 px-4 pb-6">
        <div class="grid grid-cols-3 gap-2 text-sm">
          <div class="rounded-lg border p-3">
            <div class="text-xl font-semibold tabular-nums">{{ cell.assigned }} / {{ cell.capacity }}</div>
            <div class="text-muted-foreground">Assigned</div>
          </div>
          <div class="rounded-lg border p-3">
            <UserRound class="mb-1 size-4 text-muted-foreground" />
            <div class="font-medium">{{ cell.instructor }}</div>
          </div>
          <div class="rounded-lg border p-3">
            <MapPin class="mb-1 size-4 text-muted-foreground" />
            <div class="font-medium">{{ cell.space }}</div>
          </div>
        </div>
        <div
          v-if="over > 0"
          class="flex gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900"
          role="status"
        >
          <TriangleAlert class="mt-0.5 size-4 shrink-0 text-amber-600" />
          Over capacity by {{ over }} {{ over === 1 ? 'camper' : 'campers' }}. Move someone to an activity with room.
        </div>

        <h3 class="text-sm font-medium">Campers ({{ cell.campers.length }})</h3>
        <p v-if="!cell.campers.length" class="text-sm text-muted-foreground">Nobody is placed here yet.</p>
        <ul class="divide-y rounded-lg border">
          <li v-for="c in sorted" :key="c.registrationId" class="space-y-2 p-3">
            <div class="flex flex-wrap items-center gap-2">
              <span class="font-medium">{{ c.name }}</span>
              <span class="text-sm text-muted-foreground">Grade {{ c.grade ?? '—' }}</span>
              <Badge v-if="c.doubleBooked" variant="outline" class="border-red-200 bg-red-50 text-red-800"
                >Double-booked</Badge
              >
              <Badge v-if="outside(c)" variant="outline" class="border-amber-200 bg-amber-50 text-amber-800"
                >Outside grades</Badge
              >
            </div>
            <p v-if="c.doubleBooked" class="text-sm text-red-800">
              Also in {{ otherBooking(c) ?? 'another activity' }} this period.
            </p>
            <p v-if="c.choices.length" class="text-xs text-muted-foreground">
              Ranked: <template v-for="(n, i) in c.choices" :key="n">{{ i ? ', ' : '' }}{{ i + 1 }}. {{ n }}</template>
            </p>
            <div v-if="canEdit" class="flex flex-wrap gap-2">
              <Button v-if="c.doubleBooked" size="sm" variant="outline" :disabled="busy" @click="keep(c)"
                >Keep in {{ row.name }}</Button
              >
              <Select :disabled="busy || !targets(c).length" @update:model-value="(v) => move(c, String(v))">
                <SelectTrigger size="sm" class="w-full sm:w-56" :aria-label="`Move ${c.name}`">
                  <SelectValue :placeholder="targets(c).length ? 'Move to…' : 'No other activity has room'" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="t in targets(c)" :key="t.cell.slotId" :value="String(t.cell.slotId)"
                    >{{ t.row.name }} · {{ t.cell.capacity - t.cell.assigned }} open</SelectItem
                  >
                </SelectContent>
              </Select>
            </div>
          </li>
        </ul>
      </div>
    </SheetContent>
  </Sheet>
</template>
