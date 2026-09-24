<script setup lang="ts">
import { ArrowLeft, CalendarX, ChevronRight, Sun, Users } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateRange, money } from '@/lib/format'
import PaymentBadge from './PaymentBadge.vue'
import type { RegistrationCard, RegistrationList } from './types'

const data = ref<RegistrationList | null>(null)
onMounted(async () => {
  data.value = await api.get<RegistrationList>('/family/registrations')
})

const who = ref('all')
const people = computed(() => {
  const all = [...(data.value?.upcoming ?? []), ...(data.value?.past ?? []), ...(data.value?.cancelled ?? [])]
  const byId = new Map<number, string>()
  for (const c of all) for (const p of c.participants) byId.set(p.id, p.name)
  return [...byId].map(([id, name]) => ({ id: String(id), name })).toSorted((a, b) => a.name.localeCompare(b.name))
})
const keep = (cards: RegistrationCard[]) =>
  who.value === 'all' ? cards : cards.filter((c) => c.participants.some((p) => String(p.id) === who.value))
const sections = computed(() => [
  { key: 'upcoming', title: 'Upcoming', cards: keep(data.value?.upcoming ?? []), empty: 'No upcoming registrations' },
  { key: 'past', title: 'Past', cards: keep(data.value?.past ?? []), empty: 'No past registrations' },
  {
    key: 'cancelled',
    title: 'Cancelled',
    cards: keep(data.value?.cancelled ?? []),
    empty: 'No cancelled registrations',
  },
])
const emptyHint: Record<string, string> = {
  upcoming: 'Registrations you make show up here with their balance and next steps.',
  past: 'Camps and retreats you’ve attended show up here.',
  cancelled: 'Cancelled registrations will appear here.',
}
const isAdultProgram = (c: RegistrationCard) => c.participants.every((p) => !p.gradeLabel)
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-8 md:py-12">
    <Button variant="link" as-child class="h-auto p-0 text-muted-foreground">
      <RouterLink to="/family"><ArrowLeft />My family</RouterLink>
    </Button>
    <h1 class="mt-4 text-3xl font-semibold tracking-tight md:text-4xl">My registrations</h1>

    <div v-if="!data" class="mt-8 space-y-4">
      <Skeleton class="h-32 rounded-xl" /><Skeleton class="h-32 rounded-xl" />
    </div>

    <template v-else>
      <div class="mt-6 max-w-md space-y-2">
        <Label for="who">Filter by participant</Label>
        <Select v-model="who">
          <SelectTrigger id="who" class="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All family members</SelectItem>
            <SelectItem v-for="p in people" :key="p.id" :value="p.id">{{ p.name }}</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <section v-for="s in sections" :key="s.key" class="mt-8" :aria-labelledby="`${s.key}-heading`">
        <h2 :id="`${s.key}-heading`" class="text-lg font-semibold">{{ s.title }} ({{ s.cards.length }})</h2>
        <div
          v-if="!s.cards.length"
          class="mt-3 flex items-center justify-center gap-3 rounded-xl border border-dashed p-6 text-center"
        >
          <CalendarX class="size-6 shrink-0 text-muted-foreground" />
          <div class="text-left">
            <p class="text-sm font-medium">{{ s.empty }}</p>
            <p class="text-xs text-muted-foreground">{{ emptyHint[s.key] }}</p>
          </div>
        </div>
        <ul class="mt-3 space-y-3">
          <li v-for="c in s.cards" :key="c.confirmationCode">
            <Card class="py-5">
              <CardContent
                class="grid gap-5 px-4 sm:px-6 md:grid-cols-[minmax(0,1.4fr)_minmax(0,2fr)_auto] md:items-center"
              >
                <div class="flex gap-4">
                  <span
                    :class="[
                      'flex size-12 shrink-0 items-center justify-center rounded-full',
                      isAdultProgram(c) ? 'bg-muted' : 'bg-emerald-50 text-emerald-700',
                    ]"
                  >
                    <Users v-if="isAdultProgram(c)" class="size-6" /><Sun v-else class="size-6" />
                  </span>
                  <div class="min-w-0">
                    <p class="text-xs text-muted-foreground">{{ c.ministry }}</p>
                    <p class="font-semibold">{{ c.program }}</p>
                    <p class="text-sm">{{ dateRange(c.startDate, c.endDate) }}</p>
                    <ul class="mt-1 text-sm text-muted-foreground">
                      <li v-for="p in c.participants" :key="p.id">
                        {{ p.name }}<template v-if="p.gradeLabel"> · {{ p.gradeLabel }}</template>
                        <span v-if="p.movedTo" class="block text-foreground">
                          Moved to {{ p.movedTo.name }} · {{ dateRange(p.movedTo.startDate, p.movedTo.endDate) }}
                        </span>
                        <template v-if="p.status !== c.status && p.status !== 'Confirmed'">
                          ·
                          <StatusBadge
                            :status="p.status"
                            :label="p.status === 'Waitlisted' && p.position ? `Waitlisted #${p.position}` : undefined"
                            class="align-middle"
                          />
                        </template>
                      </li>
                    </ul>
                  </div>
                </div>

                <dl class="grid grid-cols-3 gap-3 md:border-x md:px-6">
                  <div>
                    <dt class="text-xs text-muted-foreground">Total</dt>
                    <dd class="font-semibold tabular-nums">{{ money(c.totalCents) }}</dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Paid</dt>
                    <dd class="font-semibold tabular-nums">{{ money(c.paidCents) }}</dd>
                  </div>
                  <div>
                    <dt :class="['text-xs', c.balanceCents > 0 ? 'text-emerald-700' : 'text-muted-foreground']">
                      Balance due
                    </dt>
                    <dd :class="['font-semibold tabular-nums', c.balanceCents > 0 ? 'text-emerald-700 text-lg' : '']">
                      {{ money(c.balanceCents) }}
                    </dd>
                    <dd v-if="c.planInstallments && c.planEachCents" class="text-xs text-muted-foreground">
                      {{ c.planInstallments }} × {{ money(c.planEachCents) }} payment plan
                    </dd>
                  </div>
                </dl>

                <div class="flex items-center justify-between gap-3 md:flex-col md:items-end">
                  <div class="flex flex-wrap gap-1.5">
                    <StatusBadge :status="c.status" />
                    <PaymentBadge v-if="c.group !== 'cancelled'" :status="c.paymentStatus" />
                  </div>
                  <Button variant="outline" as-child>
                    <RouterLink
                      :to="`/family/registrations/${c.confirmationCode}`"
                      :aria-label="`View details for ${c.program}, ${c.confirmationCode}`"
                      >View details<ChevronRight
                    /></RouterLink>
                  </Button>
                </div>
              </CardContent>
            </Card>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>
