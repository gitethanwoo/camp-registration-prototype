<script setup lang="ts">
import { Info, RefreshCw } from '@lucide/vue'
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { nowMs } from '@/lib/clock'
import ActivityDetailSheet from './ActivityDetailSheet.vue'
import PeriodPicker from './PeriodPicker.vue'
import { isFull, periodProblem, toggleRank, useInstead } from './ranking'
import { useActivityDraft } from './useActivityDraft'

// R4 · Activity selection (FR-23, 24), a registration wizard step for sessions with an activity schedule.
const props = defineProps<{
  sessionId: number
  sessionName: string
  campers: { id: number; firstName: string }[]
  attempted: boolean
}>()
const acts = useActivityDraft(props.sessionId)
const tab = ref(String(props.campers[0]?.id ?? ''))
const clock = ref(nowMs())
let poll: number | undefined
let tick: number | undefined

const ids = computed(() => props.campers.map((c) => c.id))
onMounted(() => {
  void acts.refresh(ids.value)
  // Slots left move while families choose; re-check every 20 seconds.
  poll = window.setInterval(() => void acts.refresh(ids.value), 20_000)
  tick = window.setInterval(() => (clock.value = nowMs()), 1_000)
})
onUnmounted(() => {
  clearInterval(poll)
  clearInterval(tick)
})

const camper = computed(() => acts.options.value?.campers.find((c) => String(c.personId) === tab.value) ?? null)
const maxRanks = computed(() => acts.options.value?.maxRanks ?? 3)
const updated = computed(() => {
  const at = acts.updatedAt.value
  if (!at) return ''
  const secs = Math.round((clock.value - at.getTime()) / 1000)
  return secs < 5 ? 'Slots left updated just now' : `Slots left updated ${secs}s ago`
})
const needsWork = (personId: number) => {
  const c = acts.options.value?.campers.find((x) => x.personId === personId)
  return !!c?.periods.some((p) => periodProblem('', p, acts.ranked(personId, p.period)))
}

// Details sheet
const sheet = ref<{ activityId: number; period: number } | null>(null)
const sheetPeriod = computed(() => camper.value?.periods.find((p) => p.period === sheet.value?.period) ?? null)
const sheetSelected = computed(() =>
  sheet.value && camper.value
    ? acts.ranked(camper.value.personId, sheet.value.period).includes(sheet.value.activityId)
    : false,
)
const sheetCanSelect = computed(() => {
  const s = sheet.value
  const option = sheetPeriod.value?.options.find((o) => o.activityId === s?.activityId)
  if (!s || !camper.value || !option || isFull(option)) return false
  return acts.ranked(camper.value.personId, s.period).length < maxRanks.value
})

function toggle(period: number, id: number) {
  if (!camper.value) return
  const p = camper.value.personId
  acts.setRanked(p, period, toggleRank(acts.ranked(p, period), id, maxRanks.value))
}
function instead(period: number, id: number) {
  const c = camper.value
  const per = c?.periods.find((x) => x.period === period)
  if (!c || !per) return
  acts.setRanked(c.personId, period, useInstead(per, acts.ranked(c.personId, period), id))
}
function selectFromSheet() {
  const s = sheet.value
  if (s) toggle(s.period, s.activityId)
  sheet.value = null
}
</script>

<template>
  <Card>
    <CardHeader>
      <CardTitle>Choose activities</CardTitle>
      <CardDescription
        >Pick an activity for each period, for each camper. Rank a second and third choice in case your first fills up.
        We place campers when you pay.</CardDescription
      >
    </CardHeader>
    <CardContent class="space-y-5">
      <Tabs v-if="campers.length > 1" v-model="tab">
        <TabsList class="max-w-full overflow-x-auto">
          <TabsTrigger v-for="c in campers" :key="c.id" :value="String(c.id)" class="px-4">
            {{ c.firstName }}
            <span
              v-if="attempted && needsWork(c.id)"
              class="size-2 rounded-full bg-destructive"
              aria-label="needs choices"
            />
          </TabsTrigger>
        </TabsList>
      </Tabs>

      <Alert v-if="acts.loadError.value" variant="destructive">
        <AlertTitle>Slots left didn't load</AlertTitle>
        <AlertDescription>{{ acts.loadError.value }}</AlertDescription>
      </Alert>
      <div v-if="!acts.options.value" class="grid gap-4 md:grid-cols-3">
        <Skeleton v-for="i in 3" :key="i" class="h-72 rounded-xl" />
      </div>
      <template v-else-if="camper">
        <div>
          <h3 class="text-lg font-semibold">{{ camper.firstName }}</h3>
          <p class="text-sm text-muted-foreground">
            {{ sessionName }} · {{ camper.gradeLabel }}<template v-if="camper.block"> · {{ camper.block }}</template>
          </p>
        </div>
        <p v-if="!camper.block" class="text-sm text-muted-foreground">
          No activity schedule covers {{ camper.firstName }}'s grade. The camp office will plan activities with you.
        </p>
        <div v-else class="grid gap-4 md:grid-cols-3">
          <PeriodPicker
            v-for="p in camper.periods"
            :key="`${camper.personId}-${p.period}`"
            :period="p"
            :ranked="acts.ranked(camper.personId, p.period)"
            :max-ranks="maxRanks"
            :camper-name="camper.firstName"
            :invalid="attempted && !!periodProblem('', p, acts.ranked(camper.personId, p.period))"
            @toggle="(id) => toggle(p.period, id)"
            @use-instead="(id) => instead(p.period, id)"
            @details="(id) => (sheet = { activityId: id, period: p.period })"
          />
        </div>
      </template>
      <div class="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border bg-muted/30 p-3 text-sm">
        <Info class="size-4 shrink-0 text-muted-foreground" />
        <span class="min-w-0 flex-1"
          >Activities fill quickly. If a choice fills before you pay, we place your camper in their next choice.</span
        >
        <span class="text-xs text-muted-foreground" aria-live="polite">{{ updated }}</span>
        <Button variant="ghost" size="sm" class="h-7 px-2" @click="acts.refresh(ids)"
          ><RefreshCw class="size-3.5" />Refresh</Button
        >
      </div>
    </CardContent>
  </Card>
  <ActivityDetailSheet
    :activity-id="sheet?.activityId ?? null"
    :session-id="sessionId"
    :camper-name="camper?.firstName ?? ''"
    :block="camper?.block ?? null"
    :period="sheet?.period ?? null"
    :selected="sheetSelected"
    :can-select="sheetCanSelect"
    @close="sheet = null"
    @select="selectFromSheet"
  />
</template>
