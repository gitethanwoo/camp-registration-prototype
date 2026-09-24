<script setup lang="ts">
import {
  CalendarDays,
  ChevronRight,
  CircleCheck,
  ExternalLink,
  KeyRound,
  ListChecks,
  Plus,
  TriangleAlert,
} from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateRange, date, initials, money } from '@/lib/format'
import PaymentBadge from './PaymentBadge.vue'
import type { Overview } from './types'

const data = ref<Overview | null>(null)
const failed = ref(false)

async function load() {
  failed.value = false
  try {
    data.value = await api.get<Overview>('/family/overview')
  } catch {
    failed.value = true
  }
}
onMounted(load)

const tones = [
  'bg-rose-100 text-rose-800',
  'bg-sky-100 text-sky-800',
  'bg-emerald-100 text-emerald-800',
  'bg-violet-100 text-violet-800',
  'bg-amber-100 text-amber-800',
]
const children = computed(() => data.value?.members.filter((m) => !m.isAdult) ?? [])
const todo = computed(() => data.value?.checklist.filter((c) => !c.done) ?? [])
// Open items first, so the next thing to do is at the top.
const checklist = computed(() => [...todo.value, ...(data.value?.checklist.filter((c) => c.done) ?? [])])
const plans = computed(() => data.value?.registrations.filter((r) => r.plan || r.balanceCents > 0) ?? [])
const isExternal = (href: string) => href.startsWith('http')

function role(m: Overview['members'][number]) {
  if (!m.isAdult) return 'Camper'
  if (m.role === 'Primary') return 'Primary'
  return m.role ?? 'Adult'
}
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-8 md:py-12">
    <div class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 class="text-3xl font-semibold tracking-tight md:text-4xl">My family</h1>
        <p class="mt-1 text-muted-foreground">Manage your campers, registrations, and to-dos.</p>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button variant="outline" size="sm" as-child
          ><RouterLink to="/family/registrations"><ListChecks />My registrations</RouterLink></Button
        >
        <Button variant="outline" size="sm" as-child
          ><RouterLink to="/family/access"><KeyRound />Household access</RouterLink></Button
        >
      </div>
    </div>

    <Card v-if="failed" class="mt-8">
      <CardContent class="flex flex-wrap items-center justify-between gap-3">
        <p class="text-sm">Your family couldn’t be loaded. Check your connection and try again.</p>
        <Button size="sm" variant="outline" @click="load">Try again</Button>
      </CardContent>
    </Card>

    <div v-else-if="!data" class="mt-8 grid gap-6 lg:grid-cols-3">
      <Skeleton class="h-40 rounded-xl lg:col-span-2" /><Skeleton class="h-40 rounded-xl" />
      <Skeleton class="h-72 rounded-xl lg:col-span-2" />
    </div>

    <div v-else class="mt-8 grid items-start gap-6 lg:grid-cols-3">
      <div class="space-y-6 lg:col-span-2">
        <section aria-label="Family members">
          <ul class="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
            <li v-for="(m, i) in data.members" :key="m.id">
              <RouterLink
                :to="`/family/members/${m.id}`"
                class="flex h-full flex-col items-center rounded-xl border bg-card p-4 text-center transition-colors hover:bg-muted/50 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
              >
                <Avatar class="size-14">
                  <AvatarFallback :class="['text-lg font-semibold', tones[i % tones.length]]">{{
                    initials(`${m.firstName} ${m.lastName}`)
                  }}</AvatarFallback>
                </Avatar>
                <span class="mt-3 font-medium">{{ m.firstName }} {{ m.lastName }}</span>
                <span class="text-sm text-muted-foreground">{{ role(m) }}</span>
                <span v-if="m.gradeLabel" class="text-sm text-muted-foreground">{{ m.gradeLabel }}</span>
              </RouterLink>
            </li>
            <li>
              <RouterLink
                to="/family/members/new"
                class="flex h-full min-h-36 flex-col items-center justify-center gap-2 rounded-xl border border-dashed p-4 text-center text-sm text-muted-foreground transition-colors hover:bg-muted/50 hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
              >
                <span class="flex size-10 items-center justify-center rounded-full bg-muted"
                  ><Plus class="size-5"
                /></span>
                Add a child
              </RouterLink>
            </li>
          </ul>
        </section>

        <Card id="checklist" class="scroll-mt-24">
          <CardHeader>
            <CardTitle>Checklist</CardTitle>
            <CardDescription>
              <template v-if="!data.checklist.length">Register for a program and your to-dos show up here.</template>
              <template v-else-if="todo.length"
                >{{ todo.length }} {{ todo.length === 1 ? 'thing' : 'things' }} to do before camp, across all your
                kids.</template
              >
              <template v-else>Everything is done for every registration.</template>
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div v-if="!data.checklist.length" class="flex flex-wrap items-center gap-3">
              <Button v-if="!children.length" as-child
                ><RouterLink to="/family/members/new"><Plus />Add your first child</RouterLink></Button
              >
              <Button :variant="children.length ? 'default' : 'outline'" as-child
                ><RouterLink to="/programs">Find a program</RouterLink></Button
              >
            </div>
            <ul v-else class="divide-y">
              <li v-for="c in checklist" :key="c.key" class="flex flex-wrap items-center gap-3 py-3">
                <CircleCheck v-if="c.done" class="size-5 shrink-0 text-emerald-600" />
                <TriangleAlert v-else class="size-5 shrink-0 text-amber-600" />
                <div class="min-w-0 flex-1">
                  <p :class="['text-sm', c.done ? 'text-muted-foreground' : 'font-medium']">
                    {{ c.participant }} · {{ c.title }}
                  </p>
                  <p class="text-xs text-muted-foreground">{{ c.detail }} · {{ c.context }}</p>
                </div>
                <Button
                  v-if="c.done && c.kind !== 'balance'"
                  size="sm"
                  variant="ghost"
                  disabled
                  class="text-emerald-700"
                  >Complete</Button
                >
                <Button v-else-if="isExternal(c.href)" size="sm" :variant="c.done ? 'ghost' : 'default'" as-child>
                  <a :href="c.href" target="_blank" rel="noopener">{{ c.action }}<ExternalLink class="size-3.5" /></a>
                </Button>
                <Button v-else size="sm" :variant="c.done ? 'ghost' : 'default'" as-child>
                  <RouterLink :to="c.href">{{ c.action }}</RouterLink>
                </Button>
              </li>
            </ul>
          </CardContent>
        </Card>
      </div>

      <aside class="space-y-6">
        <Card v-for="r in data.registrations" :key="r.confirmationCode">
          <CardHeader>
            <CardTitle class="flex items-center gap-2"><CalendarDays class="size-5" />{{ r.program }}</CardTitle>
            <CardDescription>{{ dateRange(r.startDate, r.endDate) }} · {{ r.participants }}</CardDescription>
            <CardAction>
              <Badge variant="outline" class="border-emerald-200 bg-emerald-50 text-emerald-800"
                >{{ r.count }} {{ r.count === 1 ? 'camper' : 'campers' }}</Badge
              >
            </CardAction>
          </CardHeader>
          <CardContent>
            <Button variant="link" class="h-auto p-0" as-child>
              <RouterLink :to="`/family/registrations/${r.confirmationCode}`"
                >View registration<ChevronRight class="size-4"
              /></RouterLink>
            </Button>
          </CardContent>
        </Card>

        <Card v-for="r in plans" :key="`plan-${r.confirmationCode}`">
          <CardHeader>
            <CardTitle>{{ r.plan ? 'Payment plan' : 'Balance' }}</CardTitle>
            <CardDescription>{{ r.program }}</CardDescription>
            <CardAction><PaymentBadge :status="r.paymentStatus" /></CardAction>
          </CardHeader>
          <CardContent class="space-y-3">
            <p class="font-medium tabular-nums">{{ money(r.paidCents) }} of {{ money(r.totalCents) }} total</p>
            <p v-if="r.plan" class="text-sm text-muted-foreground">
              {{ r.plan.installments }} payments of {{ money(r.plan.eachCents) }}
              <template v-if="r.plan.nextDueDate"
                >· next {{ money(r.plan.nextAmountCents) }} on {{ date(r.plan.nextDueDate) }}</template
              >
            </p>
            <Progress
              :model-value="r.totalCents ? Math.round((r.paidCents / r.totalCents) * 100) : 0"
              :aria-label="`${money(r.paidCents)} of ${money(r.totalCents)} paid`"
              class="h-2.5 [&>*]:bg-emerald-600"
            />
            <div class="flex items-center justify-between gap-3">
              <Button size="sm" variant="outline" as-child>
                <RouterLink :to="`/family/registrations/${r.confirmationCode}/payments`">
                  {{ r.balanceCents > 0 ? 'Pay balance' : 'View payments' }}
                </RouterLink>
              </Button>
              <span class="text-sm text-muted-foreground tabular-nums"
                >{{ money(r.paidCents) }} / {{ money(r.totalCents) }}</span
              >
            </div>
          </CardContent>
        </Card>

        <Card v-if="data.checklist.length && !todo.length" class="border-emerald-200 bg-emerald-50/50">
          <CardContent class="flex items-start gap-4">
            <CircleCheck class="size-10 shrink-0 text-emerald-600" />
            <div>
              <p class="font-semibold">You’re all set for camp!</p>
              <p class="text-sm text-muted-foreground">
                Waivers, health forms, and payments are complete for
                {{ data.registrations.map((r) => r.participants).join(' and ') }}.
              </p>
            </div>
          </CardContent>
        </Card>

        <Card v-if="!data.registrations.length">
          <CardHeader>
            <CardTitle>No upcoming camps</CardTitle>
            <CardDescription>Registrations for {{ data.seasonYear }} show up here once you sign up.</CardDescription>
          </CardHeader>
          <CardContent class="flex flex-wrap gap-2">
            <Button size="sm" variant="outline" as-child
              ><RouterLink to="/programs">Browse programs</RouterLink></Button
            >
            <Button size="sm" variant="ghost" as-child
              ><RouterLink to="/family/registrations">Past registrations</RouterLink></Button
            >
          </CardContent>
        </Card>
      </aside>
    </div>
  </div>
</template>
