<script setup lang="ts">
import { BedDouble, CircleCheck, Hotel, House, Search, TriangleAlert, UserRound, Users } from '@lucide/vue'
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Progress } from '@/components/ui/progress'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateRange, dateTime } from '@/lib/format'
import RoomingCamperRow from './RoomingCamperRow.vue'
import StatTile from './StatTile.vue'
import type { Rooming, RoomingCabin } from './types'
import { describe, useOpsData } from './useOpsData'

// O3 · Rooming board. Cabins are single-gender; the server refuses a wrong-gender or over-full placement.
const { data: r, error, load, sessionId, canEdit } = useOpsData<Rooming>((id) => `/admin/ops/sessions/${id}/rooming`)

const search = ref('')
const gender = ref<'all' | 'Male' | 'Female'>('all')
const requestFilter = ref<'all' | 'notMet' | 'met'>('all')
const openCabinId = ref<number | null>(null)
const openCabin = computed(() => r.value?.cabins.find((c) => c.id === openCabinId.value) ?? null)
const pct = (n: number, of: number) => (of ? Math.round((n / of) * 100) : 0)

function visible(c: RoomingCabin) {
  const q = search.value.trim().toLowerCase()
  if (gender.value !== 'all' && c.gender !== gender.value) return false
  if (requestFilter.value === 'notMet' && c.requestsNotMet === 0) return false
  if (requestFilter.value === 'met' && c.requestsMet === 0) return false
  return !q || c.name.toLowerCase().includes(q) || c.campers.some((x) => x.name.toLowerCase().includes(q))
}
const sides = computed(() => {
  const x = r.value
  if (!x) return []
  return [
    {
      key: 'Male',
      title: 'Boys cabins',
      s: x.summary.boys,
      cabins: x.cabins.filter((c) => c.gender === 'Male' && visible(c)),
    },
    {
      key: 'Female',
      title: 'Girls cabins',
      s: x.summary.girls,
      cabins: x.cabins.filter((c) => c.gender === 'Female' && visible(c)),
    },
  ].filter((side) => gender.value === 'all' || gender.value === side.key)
})
const unassigned = computed(() => {
  const q = search.value.trim().toLowerCase()
  return (r.value?.unassigned ?? []).filter(
    (x) => (gender.value === 'all' || x.gender === gender.value) && (!q || x.name.toLowerCase().includes(q)),
  )
})
const cabinRequests = computed(() => {
  const c = openCabin.value
  if (!c) return []
  const seen = new Set<string>()
  return c.campers.flatMap((x) =>
    x.requests
      .filter((q) => {
        const key = [x.registrationId, q.withRegistrationId].toSorted((m, n) => m - n).join('-')
        if (seen.has(key)) return false
        seen.add(key)
        return true
      })
      .map((q) => ({ key: `${x.registrationId}-${q.withRegistrationId}`, a: x.name, ...q })),
  )
})

async function move(registrationId: number, cabinId: number | null) {
  const target = r.value?.cabins.find((c) => c.id === cabinId)
  try {
    const res = await api.put<{ previousCabinId: number | null }>(`/admin/ops/rooming/placements/${registrationId}`, {
      cabinId,
    })
    await load()
    toast.success(`Saved · moved to ${target?.name ?? 'Unassigned'}.`, {
      action: {
        label: 'Undo',
        onClick: async () => {
          await api.put(`/admin/ops/rooming/placements/${registrationId}`, { cabinId: res.previousCabinId })
          await load()
        },
      },
    })
  } catch (e) {
    toast.error(describe(e, "That move wasn't saved. Try again."))
  }
}

const reviewing = ref(false)
async function markReviewed() {
  const id = sessionId.value
  if (!id) return
  reviewing.value = true
  try {
    const res = await api.post<{ released: number }>(`/admin/ops/sessions/${id}/rooming/review`)
    await load()
    toast.success(
      res.released
        ? `Rooming marked reviewed. ${res.released} ${res.released === 1 ? 'bed was' : 'beds were'} freed up.`
        : 'Rooming marked reviewed.',
    )
  } catch (e) {
    toast.error(describe(e))
  } finally {
    reviewing.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-7xl space-y-6">
    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >
    <template v-else-if="!r">
      <Skeleton class="h-10 w-72" />
      <div class="grid gap-4 sm:grid-cols-3"><Skeleton v-for="i in 3" :key="i" class="h-28 rounded-xl" /></div>
    </template>
    <template v-else>
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Rooming board</h1>
        <p class="text-muted-foreground">
          {{ r.session.program }} · {{ r.session.name }} · {{ dateRange(r.session.startDate, r.session.endDate) }}
        </p>
      </div>

      <Card v-if="r.managedInOpera">
        <CardContent class="flex items-start gap-3">
          <Hotel class="mt-0.5 size-6 text-muted-foreground" aria-hidden="true" />
          <p class="text-muted-foreground">
            Rooms for this session come from Oracle Opera at the retreat center. Assign and change rooms there.
          </p>
        </CardContent>
      </Card>
      <Card v-else-if="r.summary.cabins === 0">
        <CardContent class="text-muted-foreground">
          This session has no cabins set up yet. Choose Overnight Camp Session 3 to see the rooming board.
        </CardContent>
      </Card>
      <template v-else>
        <div class="grid grid-cols-2 gap-4 lg:grid-cols-3 xl:grid-cols-6">
          <StatTile label="Total beds" :value="r.summary.beds" :icon="House" :hint="`${r.summary.cabins} cabins`" />
          <StatTile
            label="Registered"
            :value="r.summary.registered"
            :of="r.summary.capacity"
            :icon="Users"
            :percent="pct(r.summary.registered, r.summary.capacity)"
          />
          <StatTile
            label="Assigned beds"
            :value="r.summary.assigned"
            :of="r.summary.beds"
            :icon="BedDouble"
            :percent="pct(r.summary.assigned, r.summary.beds)"
          />
          <StatTile
            label="Unassigned"
            :value="r.summary.unassigned"
            :icon="UserRound"
            :tone="r.summary.unassigned ? 'warn' : 'good'"
          />
          <StatTile
            label="Boys cabins"
            :value="r.summary.boys.assigned"
            :of="r.summary.boys.beds"
            :icon="House"
            :percent="pct(r.summary.boys.assigned, r.summary.boys.beds)"
          />
          <StatTile
            label="Girls cabins"
            :value="r.summary.girls.assigned"
            :of="r.summary.girls.beds"
            :icon="House"
            :percent="pct(r.summary.girls.assigned, r.summary.girls.beds)"
          />
        </div>

        <Alert v-if="r.review.items.length" class="border-amber-300 bg-amber-50/60" data-testid="roster-changed">
          <TriangleAlert class="size-4" />
          <AlertTitle>
            Roster changed: {{ r.review.items.length }}
            {{ r.review.items.length === 1 ? 'item needs' : 'items need' }} re-review
          </AlertTitle>
          <AlertDescription>
            <p v-if="r.review.reviewedAt">
              Since {{ r.review.reviewedBy }} last reviewed rooming on {{ dateTime(r.review.reviewedAt) }}:
            </p>
            <ul class="mt-1 list-disc pl-5">
              <li v-for="i in r.review.items" :key="`${i.registrationId}-${i.reason}`">
                <span class="font-medium">{{ i.name }}</span
                >: {{ i.reason }}
              </li>
            </ul>
            <Button v-if="canEdit" size="sm" variant="outline" class="mt-2" :disabled="reviewing" @click="markReviewed">
              Mark reviewed
            </Button>
          </AlertDescription>
        </Alert>
        <p v-else-if="r.review.reviewedAt" class="flex items-center gap-2 text-sm text-muted-foreground">
          <CircleCheck class="size-4 text-emerald-600" aria-hidden="true" />Reviewed by {{ r.review.reviewedBy }} on
          {{ dateTime(r.review.reviewedAt) }}. Nothing has changed since.
        </p>

        <p class="text-sm text-muted-foreground" data-testid="request-summary">
          Cabinmate requests: {{ r.summary.requestsMet }} met · {{ r.summary.requestsNotMet }} not met<template
            v-if="r.summary.conflicts"
          >
            · {{ r.summary.conflicts }} can't be met (other gender, or the camper left)</template
          >.
        </p>
        <div class="flex flex-wrap gap-2">
          <div class="relative w-full sm:w-64">
            <Search class="absolute top-2.5 left-2.5 size-4 text-muted-foreground" aria-hidden="true" />
            <Input
              v-model="search"
              class="pl-8"
              placeholder="Search cabins or campers"
              aria-label="Search cabins or campers"
            />
          </div>
          <Select v-model="gender">
            <SelectTrigger class="w-full sm:w-40" aria-label="Filter by gender"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All cabins</SelectItem>
              <SelectItem value="Male">Boys cabins</SelectItem>
              <SelectItem value="Female">Girls cabins</SelectItem>
            </SelectContent>
          </Select>
          <Select v-model="requestFilter">
            <SelectTrigger class="w-full sm:w-52" aria-label="Filter by cabinmate requests"
              ><SelectValue
            /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All cabinmate requests</SelectItem>
              <SelectItem value="notMet">Requests not met</SelectItem>
              <SelectItem value="met">Requests met</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <section v-for="side in sides" :key="side.key" class="space-y-3">
          <div class="flex items-baseline justify-between">
            <h2 class="text-lg font-semibold">{{ side.title }} ({{ side.s.cabins }})</h2>
            <span class="text-sm text-muted-foreground tabular-nums">
              {{ side.s.assigned }} / {{ side.s.beds }} beds ({{ pct(side.s.assigned, side.s.beds) }}%)
            </span>
          </div>
          <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Button
              v-for="c in side.cabins"
              :key="c.id"
              variant="outline"
              class="h-auto flex-col items-stretch gap-2 p-4 text-left font-normal whitespace-normal"
              :data-testid="`cabin-${c.id}`"
              :aria-label="`${c.name}, ${c.taken} of ${c.beds} beds. Open details`"
              @click="openCabinId = c.id"
            >
              <span class="flex items-center gap-2 font-medium"
                ><House class="size-4" aria-hidden="true" />{{ c.name }}</span
              >
              <span class="text-sm text-muted-foreground tabular-nums">{{ c.taken }} / {{ c.beds }} beds</span>
              <Progress :model-value="pct(c.taken, c.beds)" class="h-1.5 [&>*]:bg-emerald-600" aria-hidden="true" />
              <span v-if="c.requestsNotMet" class="flex items-center gap-1.5 text-xs text-amber-800">
                <TriangleAlert class="size-3.5" aria-hidden="true" />{{ c.requestsNotMet }}
                {{ c.requestsNotMet === 1 ? 'request' : 'requests' }} not met
              </span>
              <span v-else class="flex items-center gap-1.5 text-xs text-emerald-800">
                <CircleCheck class="size-3.5" aria-hidden="true" />{{ c.requestsMet ? 'Requests met' : 'No requests' }}
              </span>
            </Button>
          </div>
          <p v-if="side.cabins.length === 0" class="text-sm text-muted-foreground">No cabins match these filters.</p>
        </section>

        <Card>
          <CardHeader
            ><CardTitle>Unassigned campers ({{ unassigned.length }})</CardTitle></CardHeader
          >
          <CardContent>
            <ul v-if="unassigned.length" class="divide-y" data-testid="unassigned">
              <RoomingCamperRow
                v-for="x in unassigned"
                :key="x.registrationId"
                :camper="x"
                :cabins="r.cabins"
                :can-edit="canEdit"
                @move="(id) => move(x.registrationId, id)"
              />
            </ul>
            <p v-else class="text-sm text-muted-foreground">Every camper has a bed.</p>
          </CardContent>
        </Card>
      </template>
    </template>

    <Sheet :open="openCabin !== null" @update:open="(v) => !v && (openCabinId = null)">
      <SheetContent class="w-full overflow-y-auto sm:max-w-md">
        <template v-if="openCabin && r">
          <SheetHeader>
            <SheetTitle>{{ openCabin.name }}</SheetTitle>
            <SheetDescription>
              {{ openCabin.taken }} / {{ openCabin.beds }} beds ({{ openCabin.beds - openCabin.taken }} open)
            </SheetDescription>
          </SheetHeader>
          <div class="space-y-6 px-4 pb-6">
            <section>
              <h3 class="text-sm font-semibold">Campers ({{ openCabin.campers.length }})</h3>
              <ul class="divide-y">
                <RoomingCamperRow
                  v-for="x in openCabin.campers"
                  :key="x.registrationId"
                  :camper="x"
                  :cabins="r.cabins"
                  :can-edit="canEdit"
                  @move="(id) => move(x.registrationId, id)"
                />
              </ul>
            </section>
            <section>
              <h3 class="text-sm font-semibold">Cabinmate requests</h3>
              <ul v-if="cabinRequests.length" class="mt-2 space-y-2">
                <li v-for="q in cabinRequests" :key="q.key" class="rounded-md border p-3 text-sm">
                  <p class="font-medium">{{ q.a }} and {{ q.with }} · {{ q.status }}</p>
                  <p class="text-muted-foreground">{{ q.why }}</p>
                </li>
              </ul>
              <p v-else class="mt-1 text-sm text-muted-foreground">No cabinmate requests in this cabin.</p>
            </section>
          </div>
        </template>
      </SheetContent>
    </Sheet>
  </div>
</template>
