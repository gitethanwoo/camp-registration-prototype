<script setup lang="ts">
import { CircleCheck, CircleDashed, Sparkles, TriangleAlert, Users } from '@lucide/vue'
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import GroupCard from './GroupCard.vue'
import StatTile from './StatTile.vue'
import type { GroupBoardView } from './types'
import { describe, useOpsData } from './useOpsData'

// O2 · Group assignment board. Every move saves at once with Undo; Auto-suggest only proposes moves.
const poolId = ref<number | null>(null)
const {
  data: b,
  error,
  load,
  sessionId,
  canEdit,
} = useOpsData<GroupBoardView>(
  (id) => `/admin/ops/sessions/${id}/groups${poolId.value ? `?poolId=${poolId.value}` : ''}`,
)
const busy = ref(false)

const poolChoice = computed({
  get: () => String(b.value?.poolId ?? ''),
  set: (v: string) => {
    poolId.value = Number(v)
    phoneGroup.value = 'first'
    load()
  },
})

function poolLabel(p: GroupBoardView['pools'][number] | undefined) {
  if (!p) return ''
  return p.needsReview ? `${p.name} · needs review` : p.name
}

const UNASSIGNED = 'unassigned'
const columns = computed(() => {
  const v = b.value
  if (!v) return []
  const cols = v.groups.map((g) => ({
    key: String(g.id),
    id: g.id as number | null,
    name: g.name,
    count: g.count,
    capacity: g.capacity as number | null,
    campers: v.campers.filter((c) => c.groupId === g.id),
  }))
  const loose = v.campers.filter((c) => c.groupId === null)
  // Campers still waiting for a group come first.
  if (loose.length)
    cols.unshift({ key: UNASSIGNED, id: null, name: 'Unassigned', count: loose.length, capacity: null, campers: loose })
  return cols
})
// On a phone one group shows at a time.
const phoneGroup = ref('first')
const shownKey = computed(() => (phoneGroup.value === 'first' ? columns.value[0]?.key : phoneGroup.value))

async function move(registrationId: number, groupId: number | null, quiet = false) {
  const camper = b.value?.campers.find((c) => c.registrationId === registrationId)
  if (!camper || camper.groupId === groupId) return
  const target = b.value?.groups.find((g) => g.id === groupId)
  try {
    const res = await api.put<{ previousGroupId: number | null }>(`/admin/ops/groups/placements/${registrationId}`, {
      groupId,
    })
    await load()
    if (quiet) return
    toast.success(`Saved · ${camper.name} is now in ${target?.name ?? 'Unassigned'}.`, {
      action: { label: 'Undo', onClick: () => move(registrationId, res.previousGroupId, true) },
    })
  } catch (e) {
    toast.error(describe(e, "That move wasn't saved. Try again."))
    await load()
  }
}
function onDrop(e: DragEvent, groupId: number | null) {
  const id = Number(e.dataTransfer?.getData('text/plain'))
  if (id) move(id, groupId)
}

async function suggestion(action: 'suggest' | 'suggestions/approve' | 'suggestions/dismiss') {
  const id = sessionId.value
  const pool = b.value?.poolId
  if (!id || !pool) return
  busy.value = true
  try {
    const res = await api.post<{ suggested?: number; moved?: number; dismissed?: number }>(
      `/admin/ops/sessions/${id}/groups/${action}`,
      { poolId: pool },
    )
    poolId.value = pool
    await load()
    if (res.suggested === 0)
      toast.info('Nothing to suggest: every camper is in a group and every request that can be met is met.')
    else if (res.suggested)
      toast.success(`${res.suggested} suggested ${res.suggested === 1 ? 'move' : 'moves'}. Review and approve below.`)
    if (res.moved) toast.success(`Approved ${res.moved} ${res.moved === 1 ? 'move' : 'moves'}.`)
    if (res.dismissed) toast.info('Suggestions dismissed. Nothing was moved.')
  } catch (e) {
    toast.error(describe(e))
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
    <template v-else-if="!b"><Skeleton class="h-10 w-72" /><Skeleton class="h-96 rounded-xl" /></template>
    <Card v-else-if="b.groups.length === 0">
      <CardHeader><CardTitle>Group assignments</CardTitle></CardHeader>
      <CardContent class="text-muted-foreground">
        This session has no activity groups set up yet. Choose Overnight Camp Session 3 to see the group board.
      </CardContent>
    </Card>
    <template v-else>
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold tracking-tight">Group assignments</h1>
          <p class="text-muted-foreground">{{ b.poolName }} · {{ b.totals?.total }} campers</p>
        </div>
        <div class="flex w-full flex-wrap items-end gap-3 sm:w-auto">
          <div class="w-full space-y-1 sm:w-64">
            <Label for="pool">Capacity pool</Label>
            <Select v-model="poolChoice">
              <SelectTrigger id="pool" class="w-full">
                <SelectValue>{{ poolLabel(b.pools.find((p) => p.id === b?.poolId)) }}</SelectValue>
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="p in b.pools" :key="p.id" :value="String(p.id)">
                  {{ poolLabel(p) }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div v-if="canEdit" class="w-full sm:w-auto">
            <Button variant="outline" class="w-full" :disabled="busy" @click="suggestion('suggest')">
              <Sparkles class="size-4" />Auto-suggest assignments
            </Button>
            <p class="mt-1 text-xs text-muted-foreground">Suggestions need a person to approve them.</p>
          </div>
        </div>
      </div>

      <div class="grid grid-cols-2 gap-4 sm:grid-cols-3">
        <StatTile label="Assigned" :value="b.totals?.assigned ?? 0" :icon="CircleCheck" tone="good" />
        <StatTile label="Unassigned" :value="b.totals?.unassigned ?? 0" :icon="CircleDashed" />
        <StatTile
          class="col-span-2 sm:col-span-1"
          label="Requests apart"
          :value="b.separated?.length ?? 0"
          :icon="TriangleAlert"
          :tone="b.separated?.length ? 'warn' : 'neutral'"
          hint="Friends who asked to be together"
        />
      </div>

      <Alert v-if="b.suggestions" class="border-sky-200 bg-sky-50/60">
        <Sparkles class="size-4" />
        <AlertTitle
          >{{ b.suggestions }} suggested {{ b.suggestions === 1 ? 'move' : 'moves' }} waiting for approval</AlertTitle
        >
        <AlertDescription>
          <p>Each suggested camper shows where it would go and why. Nothing moves until you approve.</p>
          <div v-if="canEdit" class="mt-2 flex flex-wrap gap-2">
            <Button size="sm" :disabled="busy" @click="suggestion('suggestions/approve')">Approve suggestions</Button>
            <Button size="sm" variant="outline" :disabled="busy" @click="suggestion('suggestions/dismiss')"
              >Dismiss</Button
            >
          </div>
        </AlertDescription>
      </Alert>
      <Alert
        v-for="p in b.separated"
        :key="`${p.a.registrationId}-${p.b.registrationId}`"
        class="border-amber-300 bg-amber-50/60"
      >
        <TriangleAlert class="size-4" />
        <AlertTitle>Friends in different groups</AlertTitle>
        <AlertDescription>
          {{ p.a.name }} ({{ p.a.group }}) and {{ p.b.name }} ({{ p.b.group }}) asked to be together. Move one of them,
          or use Auto-suggest.
        </AlertDescription>
      </Alert>

      <div class="space-y-1 md:hidden">
        <Label for="phone-group">Showing</Label>
        <Select v-model="phoneGroup">
          <SelectTrigger id="phone-group" class="w-full"><SelectValue placeholder="Choose a group" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="first" class="hidden">{{ columns[0]?.name }}</SelectItem>
            <SelectItem v-for="c in columns" :key="c.key" :value="c.key">
              {{ c.name }} · {{ c.count }}<template v-if="c.capacity"> / {{ c.capacity }}</template>
            </SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div class="grid gap-4 md:auto-cols-[minmax(12.5rem,1fr)] md:grid-flow-col md:overflow-x-auto md:pb-2">
        <section
          v-for="c in columns"
          :key="c.key"
          :class="c.key === shownKey ? 'block' : 'hidden md:block'"
          class="rounded-xl border bg-muted/30 p-2"
          :aria-label="c.name"
          :data-testid="`group-${c.key}`"
          @dragover.prevent
          @drop="onDrop($event, c.id)"
        >
          <header class="mb-3 flex items-baseline justify-between px-1">
            <h2 class="font-semibold">{{ c.name }}</h2>
            <span class="text-sm text-muted-foreground tabular-nums">
              <Users class="mr-1 inline size-3.5" aria-hidden="true" />{{ c.count
              }}<template v-if="c.capacity"> / {{ c.capacity }}</template>
            </span>
          </header>
          <ul class="space-y-2">
            <GroupCard
              v-for="camper in c.campers"
              :key="camper.registrationId"
              :camper="camper"
              :groups="b.groups"
              :can-edit="canEdit"
              @move="(g) => move(camper.registrationId, g)"
            />
          </ul>
          <p v-if="c.campers.length === 0" class="px-1 py-6 text-center text-sm text-muted-foreground">
            Nobody here yet.
          </p>
        </section>
      </div>
    </template>
  </div>
</template>
