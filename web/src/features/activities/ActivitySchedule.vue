<script setup lang="ts">
import { ListChecks, MapPin, TriangleAlert, UserRound, UsersRound } from '@lucide/vue'
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { api } from '@/lib/api'
import { dateRange } from '@/lib/format'
import { cn } from '@/lib/utils'
import { describe, useOpsData } from '@/features/ops/useOpsData'
import ScheduleCellSheet from './ScheduleCellSheet.vue'
import type { AssignResult, CellStatus, Schedule, ScheduleCell, ScheduleRow } from './types'

// O4 · Activity schedule builder (FR-81, 85). One grid per age block: 6 activities × 3 periods.
const blockId = ref<number | null>(null)
const {
  data: s,
  error,
  load,
  canEdit,
} = useOpsData<Schedule>(
  (id) => `/admin/ops/sessions/${id}/activities${blockId.value ? `?blockId=${blockId.value}` : ''}`,
)
const blockTab = computed({
  get: () => String(s.value?.block.id ?? ''),
  set: (v: string) => {
    blockId.value = Number(v)
    void load()
  },
})
const mobilePeriod = ref('1')

const tone: Record<CellStatus, string> = {
  Open: 'border-emerald-200 bg-emerald-50 text-emerald-800',
  Full: 'bg-muted text-muted-foreground',
  'Over capacity': 'border-amber-300 bg-amber-100 text-amber-900',
}
const flagged = (c: ScheduleCell, r: ScheduleRow) =>
  c.campers.filter((p) => p.doubleBooked || (p.grade !== null && (p.grade < r.gradeMin || p.grade > r.gradeMax))).length

// Cell detail
const open = ref<{ activityId: number; period: number } | null>(null)
const openRow = computed(() => s.value?.rows.find((r) => r.activityId === open.value?.activityId) ?? null)
const openCell = computed(() => openRow.value?.cells.find((c) => c.period === open.value?.period) ?? null)
function openSlot(slotId: number) {
  const row = s.value?.rows.find((r) => r.cells.some((c) => c.slotId === slotId))
  const cell = row?.cells.find((c) => c.slotId === slotId)
  if (row && cell) open.value = { activityId: row.activityId, period: cell.period }
}

// Assign from preferences: preview, then a person confirms.
const preview = ref<AssignResult | null>(null)
const busy = ref(false)
async function startAssign() {
  if (!s.value) return
  busy.value = true
  try {
    preview.value = await api.post<AssignResult>(
      `/admin/ops/sessions/${s.value.session.id}/activities/assign/preview`,
      {
        blockId: s.value.block.id,
      },
    )
  } catch (e) {
    toast.error(describe(e, "The preview didn't load. Try again."))
  } finally {
    busy.value = false
  }
}
async function confirmAssign() {
  if (!s.value) return
  busy.value = true
  try {
    const r = await api.post<AssignResult>(`/admin/ops/sessions/${s.value.session.id}/activities/assign`, {
      blockId: s.value.block.id,
    })
    toast.success(
      `Placed ${r.places} ${r.places === 1 ? 'period' : 'periods'} for ${r.campers} ${r.block} ${r.campers === 1 ? 'camper' : 'campers'}.` +
        (r.noRoom ? ` ${r.noRoom} had no room in any choice.` : ''),
    )
    preview.value = null
    await load()
  } catch (e) {
    toast.error(describe(e, "Assigning didn't finish. Nothing was saved; try again."))
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-7xl space-y-6">
    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >
    <template v-else-if="!s"><Skeleton class="h-10 w-72" /><Skeleton class="h-96 rounded-xl" /></template>
    <template v-else>
      <div class="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold tracking-tight">
            Activity schedule
            <span class="font-normal text-muted-foreground"
              >· {{ dateRange(s.session.startDate, s.session.endDate) }}</span
            >
          </h1>
          <p class="text-muted-foreground">
            {{ s.session.name }} · {{ s.block.name }} ({{ s.block.grades }}) · {{ s.block.campers }} campers
          </p>
        </div>
        <div v-if="canEdit" class="w-full sm:w-auto">
          <Button class="w-full" :disabled="busy || !s.pending.campers" @click="startAssign"
            ><ListChecks />Assign from preferences</Button
          >
          <p class="mt-1 text-xs text-muted-foreground">
            <template v-if="s.pending.campers"
              >{{ s.pending.campers }} {{ s.pending.campers === 1 ? 'camper has' : 'campers have' }} choices waiting.
              You confirm before anything saves.</template
            >
            <template v-else>Everyone who chose has a place.</template>
          </p>
        </div>
      </div>

      <Tabs v-model="blockTab">
        <TabsList class="max-w-full overflow-x-auto">
          <TabsTrigger v-for="b in s.blocks" :key="b.id" :value="String(b.id)" class="px-3"
            >{{ b.name }} · {{ b.grades.replace('Grades ', 'G') }} ({{ b.campers }})</TabsTrigger
          >
        </TabsList>
      </Tabs>

      <!-- Totals tie to the roster: placed + chosen but not placed + not chosen = campers in the block. -->
      <div class="grid gap-3 sm:grid-cols-3">
        <Card v-for="p in s.periods" :key="p.period" class="gap-2 py-4" :data-testid="`period-total-${p.period}`">
          <CardContent class="space-y-1 px-4 text-sm">
            <div class="flex items-baseline justify-between gap-2">
              <span class="font-medium">Period {{ p.period }}</span
              ><span class="text-xs text-muted-foreground">{{ p.time }}</span>
            </div>
            <div class="text-2xl font-semibold tabular-nums">
              {{ p.placed }}
              <span class="text-base font-normal text-muted-foreground">of {{ s.block.campers }} placed</span>
            </div>
            <p class="text-muted-foreground">
              {{ p.waiting }} chose, not placed · {{ p.notChosen }} not chosen · {{ p.capacity }} places
            </p>
          </CardContent>
        </Card>
      </div>

      <Card v-if="s.conflicts.length" class="gap-3 border-amber-200">
        <CardHeader
          ><CardTitle class="flex items-center gap-2 text-base"
            ><TriangleAlert class="size-5 text-amber-600" />{{ s.conflicts.length }}
            {{ s.conflicts.length === 1 ? 'conflict' : 'conflicts' }} to resolve</CardTitle
          ></CardHeader
        >
        <CardContent>
          <ul class="divide-y text-sm">
            <li v-for="(c, i) in s.conflicts" :key="i" class="flex flex-wrap items-center justify-between gap-2 py-2">
              <span class="flex min-w-0 items-center gap-2">
                <Badge
                  variant="outline"
                  :class="
                    c.kind === 'DoubleBooked'
                      ? 'border-red-200 bg-red-50 text-red-800'
                      : 'border-amber-200 bg-amber-50 text-amber-800'
                  "
                  >{{
                    { DoubleBooked: 'Double-booked', OverCapacity: 'Over capacity', OutsideGrades: 'Outside grades' }[
                      c.kind
                    ]
                  }}</Badge
                >
                <span class="min-w-0">{{ c.message }}</span>
              </span>
              <Button size="sm" variant="outline" @click="openSlot(c.slotIds[0] ?? 0)">Review</Button>
            </li>
          </ul>
        </CardContent>
      </Card>

      <!-- Desktop and tablet: the full grid. -->
      <div class="hidden overflow-hidden rounded-xl border md:block" role="grid" aria-label="Activity schedule">
        <div class="grid grid-cols-[12rem_repeat(3,minmax(0,1fr))] border-b bg-muted/40 text-sm font-medium" role="row">
          <div class="px-4 py-3" role="columnheader">Activity</div>
          <div v-for="p in s.periods" :key="p.period" class="px-4 py-3" role="columnheader">
            Period {{ p.period }} <span class="block text-xs font-normal text-muted-foreground">{{ p.time }}</span>
          </div>
        </div>
        <div
          v-for="r in s.rows"
          :key="r.activityId"
          class="grid grid-cols-[12rem_repeat(3,minmax(0,1fr))] border-b last:border-b-0"
          role="row"
        >
          <div class="flex items-center gap-3 px-4 py-3" role="rowheader">
            <img :src="r.imageUrl ?? ''" alt="" class="size-10 rounded-md object-cover" />
            <div>
              <div class="font-medium">{{ r.name }}</div>
              <div class="text-xs text-muted-foreground">{{ r.grades }}</div>
            </div>
          </div>
          <div v-for="c in r.cells" :key="c.slotId" class="p-2" role="gridcell">
            <Button
              variant="outline"
              :class="
                cn(
                  'h-auto w-full flex-col items-stretch gap-1 p-3 text-left font-normal whitespace-normal',
                  c.status === 'Over capacity' && 'border-amber-300 bg-amber-50/60',
                )
              "
              :aria-label="`${r.name}, Period ${c.period}: ${c.assigned} of ${c.capacity}, ${c.status}`"
              @click="open = { activityId: r.activityId, period: c.period }"
            >
              <span class="flex items-center justify-between gap-2">
                <span class="text-lg font-semibold tabular-nums">{{ c.assigned }} / {{ c.capacity }}</span>
                <Badge variant="outline" :class="tone[c.status]">{{ c.status }}</Badge>
              </span>
              <span v-if="flagged(c, r)" class="flex items-center gap-1 text-xs font-medium text-amber-800"
                ><TriangleAlert class="size-3.5" />{{ flagged(c, r) }} to review</span
              >
              <span class="flex items-center gap-1 text-xs text-muted-foreground"
                ><UserRound class="size-3.5 shrink-0" />{{ c.instructor }}</span
              >
              <span class="flex items-center gap-1 text-xs text-muted-foreground"
                ><MapPin class="size-3.5 shrink-0" />{{ c.space }}</span
              >
            </Button>
          </div>
        </div>
      </div>

      <!-- Phones: one period at a time. -->
      <div class="space-y-3 md:hidden">
        <Tabs v-model="mobilePeriod">
          <TabsList class="w-full">
            <TabsTrigger v-for="p in s.periods" :key="p.period" :value="String(p.period)"
              >Period {{ p.period }}</TabsTrigger
            >
          </TabsList>
        </Tabs>
        <template v-for="r in s.rows" :key="r.activityId">
          <Button
            v-for="c in r.cells.filter((x) => String(x.period) === mobilePeriod)"
            :key="c.slotId"
            variant="outline"
            class="h-auto w-full justify-start gap-3 p-3 text-left font-normal whitespace-normal"
            @click="open = { activityId: r.activityId, period: c.period }"
          >
            <img :src="r.imageUrl ?? ''" alt="" class="size-10 shrink-0 rounded-md object-cover" />
            <span class="min-w-0 flex-1">
              <span class="block font-medium">{{ r.name }}</span>
              <span class="block text-xs text-muted-foreground">{{ c.instructor }} · {{ c.space }}</span>
              <span v-if="flagged(c, r)" class="block text-xs font-medium text-amber-800"
                >{{ flagged(c, r) }} to review</span
              >
            </span>
            <span class="text-right">
              <span class="block font-semibold tabular-nums">{{ c.assigned }} / {{ c.capacity }}</span>
              <Badge variant="outline" :class="tone[c.status]">{{ c.status }}</Badge>
            </span>
          </Button>
        </template>
      </div>
      <p class="flex items-center gap-2 text-sm text-muted-foreground">
        <UsersRound class="size-4" />Counts include confirmed campers only. Families choose during registration or from
        their family home.
      </p>

      <ScheduleCellSheet
        :schedule="s"
        :row="openRow"
        :cell="openCell"
        :can-edit="canEdit"
        @close="open = null"
        @changed="load"
      />

      <AlertDialog :open="!!preview" @update:open="(v) => !v && (preview = null)">
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Assign {{ preview?.block }} from preferences?</AlertDialogTitle>
            <AlertDialogDescription>
              This places {{ preview?.campers }} {{ preview?.campers === 1 ? 'camper' : 'campers' }} in
              {{ preview?.places }} {{ preview?.places === 1 ? 'period' : 'periods' }}, first come first served, in the
              highest-ranked choice with room.
              <template v-if="preview?.noRoom"
                >{{ preview.noRoom }} {{ preview.noRoom === 1 ? 'period has' : 'periods have' }} no room in any choice
                and will stay open for you to place.</template
              >
              Staff moves and campers who haven't chosen are left alone. The change is recorded in the audit log.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel :disabled="busy">Cancel</AlertDialogCancel>
            <AlertDialogAction :disabled="busy" @click.prevent="confirmAssign"
              >Assign {{ preview?.places }} places</AlertDialogAction
            >
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </template>
  </div>
</template>
