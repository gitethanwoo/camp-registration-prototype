<script setup lang="ts">
import { ArrowRight, CircleCheck, CreditCard, FileSignature, HeartPulse, ListOrdered, Users } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import PoolAvailabilityList from '@/components/PoolAvailability.vue'
import { useAdminScope } from '@/composables/useAdminScope'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { dateRange, money } from '@/lib/format'
import type { PoolAvailability } from '@/lib/types'

interface Overview {
  session: { id: number; name: string; startDate: string; endDate: string; program: string; healthMechanism: string }
  capacity: number
  registered: number
  held: number
  waitlisted: number
  ready: number
  needsAttention: number
  breakdown: { health: number; waivers: number; balance: number }
  newToday: number
  collectedCents: number
  outstandingCents: number
  pools: PoolAvailability[]
}

const { sessionId, ready } = useAdminScope()
const o = ref<Overview | null>(null)
onMounted(async () => {
  await ready
  if (sessionId.value) o.value = await api.get<Overview>(`/admin/sessions/${sessionId.value}/overview`)
})
</script>

<template>
  <div class="space-y-6">
    <div v-if="!o" class="grid gap-4 md:grid-cols-4"><Skeleton v-for="i in 4" :key="i" class="h-28 rounded-xl" /></div>
    <template v-else>
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ o.session.program }} · {{ o.session.name }}</h1>
        <p class="text-muted-foreground">{{ dateRange(o.session.startDate, o.session.endDate) }}</p>
      </div>

      <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Card>
          <CardHeader class="pb-2"
            ><CardDescription class="flex items-center gap-2"
              ><Users class="size-4" />Registered</CardDescription
            ></CardHeader
          >
          <CardContent>
            <p class="text-3xl font-semibold tabular-nums">
              {{ o.registered }}<span class="text-base font-normal text-muted-foreground"> / {{ o.capacity }}</span>
            </p>
            <p class="text-sm text-muted-foreground">
              {{ o.newToday }} new today<template v-if="o.held"> · {{ o.held }} held</template>
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader class="pb-2"
            ><CardDescription class="flex items-center gap-2"
              ><CircleCheck class="size-4" />Ready for camp</CardDescription
            ></CardHeader
          >
          <CardContent>
            <p class="text-3xl font-semibold tabular-nums">{{ o.ready }}</p>
            <p class="text-sm text-muted-foreground">{{ o.needsAttention }} need attention</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader class="pb-2"
            ><CardDescription class="flex items-center gap-2"
              ><ListOrdered class="size-4" />Waitlisted</CardDescription
            ></CardHeader
          >
          <CardContent>
            <p class="text-3xl font-semibold tabular-nums">{{ o.waitlisted }}</p>
            <Button variant="link" as-child class="h-auto p-0 text-muted-foreground"
              ><RouterLink to="/admin/waitlist">Manage waitlists</RouterLink></Button
            >
          </CardContent>
        </Card>
        <Card>
          <CardHeader class="pb-2"
            ><CardDescription class="flex items-center gap-2"
              ><CreditCard class="size-4" />Collected</CardDescription
            ></CardHeader
          >
          <CardContent>
            <p class="text-3xl font-semibold tabular-nums">{{ money(o.collectedCents) }}</p>
            <p class="text-sm text-muted-foreground">{{ money(o.outstandingCents) }} outstanding</p>
          </CardContent>
        </Card>
      </div>

      <div class="grid gap-6 xl:grid-cols-[1fr_1.3fr]">
        <Card>
          <CardHeader>
            <CardTitle>Needs attention</CardTitle>
            <CardDescription>{{ o.needsAttention }} unique campers. Some have more than one open item.</CardDescription>
          </CardHeader>
          <CardContent class="space-y-2">
            <RouterLink
              v-for="item in [
                {
                  key: 'health',
                  label:
                    o.session.healthMechanism === 'CampDoc'
                      ? 'CampDoc health forms incomplete'
                      : 'Health forms incomplete',
                  count: o.breakdown.health,
                  icon: HeartPulse,
                },
                { key: 'waivers', label: 'Waivers missing', count: o.breakdown.waivers, icon: FileSignature },
                { key: 'balance', label: 'Balance due', count: o.breakdown.balance, icon: CreditCard },
              ]"
              :key="item.key"
              :to="{ path: '/admin/registrations', query: { attention: item.key } }"
              class="flex items-center gap-3 rounded-lg border p-3 transition-colors hover:bg-muted/50"
            >
              <component :is="item.icon" class="size-5 text-muted-foreground" />
              <span class="flex-1 text-sm">{{ item.label }}</span>
              <span class="font-semibold tabular-nums">{{ item.count }}</span>
              <ArrowRight class="size-4 text-muted-foreground" />
            </RouterLink>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>Capacity by group</CardTitle>
            <CardDescription
              >Live seat counts. Held seats include pending payments and open waitlist offers.</CardDescription
            >
          </CardHeader>
          <CardContent><PoolAvailabilityList :pools="o.pools" /></CardContent>
        </Card>
      </div>
    </template>
  </div>
</template>
