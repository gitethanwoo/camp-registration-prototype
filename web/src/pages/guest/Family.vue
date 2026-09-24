<script setup lang="ts">
import { CircleCheck, Circle, CreditCard, ExternalLink, FileSignature, HeartPulse } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateRange, dateTime, money } from '@/lib/format'
import type { ChecklistItem } from '@/lib/types'

interface FamilyData {
  registrations: {
    id: number
    participant: string
    program: string
    session: string
    startDate: string
    endDate: string
    pool: string
    status: string
    balanceCents: number
    confirmationCode: string
  }[]
  waitlist: {
    id: number
    participant: string
    program: string
    session: string
    pool: string
    position: number
    status: string
    offerExpiresAt: string | null
  }[]
  checklist: ChecklistItem[]
}

const data = ref<FamilyData | null>(null)
onMounted(async () => {
  data.value = await api.get<FamilyData>('/family')
})

const open = computed(() => data.value?.checklist.filter((c) => !c.done) ?? [])
const done = computed(() => data.value?.checklist.filter((c) => c.done) ?? [])
const icon = { waiver: FileSignature, health: HeartPulse, campdoc: HeartPulse, balance: CreditCard }

function act(item: ChecklistItem) {
  toast.info(
    item.kind === 'campdoc'
      ? 'CampDoc opens in a new tab in production (SSO handoff).'
      : `“${item.action}” isn't wired up in this prototype.`,
  )
}
</script>

<template>
  <div class="mx-auto max-w-4xl px-4 py-8 md:py-12">
    <h1 class="text-3xl font-semibold tracking-tight">Johnson family</h1>
    <p class="mt-1 text-muted-foreground">Maria & David · Avery and Mia</p>

    <div v-if="!data" class="mt-8 space-y-4">
      <Skeleton class="h-40 rounded-xl" /><Skeleton class="h-40 rounded-xl" />
    </div>
    <template v-else>
      <Card id="checklist" class="mt-8 scroll-mt-24">
        <CardHeader>
          <CardTitle>Checklist</CardTitle>
          <CardDescription>{{
            open.length
              ? `${open.length} ${open.length === 1 ? 'thing' : 'things'} to do before camp, across all your kids.`
              : 'You’re all set.'
          }}</CardDescription>
        </CardHeader>
        <CardContent>
          <p v-if="!data.checklist.length" class="text-sm text-muted-foreground">
            Register for a program and your to-dos will show up here.
          </p>
          <ul class="divide-y">
            <li v-for="c in open" :key="c.key" class="flex items-center gap-3 py-3">
              <component :is="icon[c.kind]" class="size-5 shrink-0 text-amber-600" />
              <div class="min-w-0 flex-1">
                <p class="text-sm font-medium">{{ c.participant }}: {{ c.title }}</p>
                <p class="text-xs text-muted-foreground">{{ c.context }}</p>
              </div>
              <Button size="sm" variant="outline" @click="act(c)">
                {{ c.action }}<ExternalLink v-if="c.kind === 'campdoc'" class="size-3.5" />
              </Button>
            </li>
            <li v-for="c in done" :key="c.key" class="flex items-center gap-3 py-3 text-muted-foreground">
              <CircleCheck class="size-5 shrink-0 text-emerald-600" />
              <div class="min-w-0 flex-1">
                <p class="text-sm">{{ c.participant }}: {{ c.title }}</p>
                <p class="text-xs">{{ c.context }}</p>
              </div>
            </li>
          </ul>
        </CardContent>
      </Card>

      <Card class="mt-6">
        <CardHeader><CardTitle>Registrations</CardTitle></CardHeader>
        <CardContent>
          <p v-if="!data.registrations.length" class="text-sm text-muted-foreground">
            None yet.
            <Button variant="link" as-child class="h-auto p-0"
              ><RouterLink to="/programs">Find a program</RouterLink></Button
            >.
          </p>
          <ul class="divide-y">
            <li v-for="r in data.registrations" :key="r.id" class="flex flex-wrap items-center gap-x-4 gap-y-1 py-3">
              <div class="min-w-0 flex-1">
                <p class="font-medium">{{ r.participant }}</p>
                <p class="text-sm text-muted-foreground">
                  {{ r.program }} · {{ r.pool }} · {{ dateRange(r.startDate, r.endDate) }}
                </p>
              </div>
              <span v-if="r.balanceCents > 0" class="text-sm text-muted-foreground tabular-nums"
                >{{ money(r.balanceCents) }} balance</span
              >
              <StatusBadge :status="r.status" />
              <Button variant="link" as-child class="h-auto p-0 font-mono"
                ><RouterLink :to="`/confirmation/${r.confirmationCode}`">{{ r.confirmationCode }}</RouterLink></Button
              >
            </li>
          </ul>
        </CardContent>
      </Card>

      <Card v-if="data.waitlist.length" class="mt-6">
        <CardHeader><CardTitle>Waitlists</CardTitle></CardHeader>
        <CardContent>
          <ul class="divide-y">
            <li v-for="w in data.waitlist" :key="w.id" class="flex flex-wrap items-center gap-x-4 gap-y-1 py-3">
              <Circle class="size-4 text-violet-500" />
              <div class="min-w-0 flex-1">
                <p class="font-medium">{{ w.participant }}</p>
                <p class="text-sm text-muted-foreground">{{ w.program }} · {{ w.pool }}</p>
                <p v-if="w.status === 'Offered' && w.offerExpiresAt" class="text-sm font-medium text-sky-800">
                  A spot is being held until {{ dateTime(w.offerExpiresAt) }}.
                </p>
              </div>
              <StatusBadge :status="w.status" :label="w.status === 'Waiting' ? `#${w.position} in line` : undefined" />
            </li>
          </ul>
        </CardContent>
      </Card>
    </template>
  </div>
</template>
