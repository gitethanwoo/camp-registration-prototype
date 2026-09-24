<script setup lang="ts">
import { ChevronRight } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateRange } from '@/lib/format'
import PaymentBadge from './PaymentBadge.vue'
import type { FamilyListItem } from './types'

const apps = ref<FamilyListItem[] | null>(null)
onMounted(async () => {
  apps.value = await api.get<FamilyListItem[]>('/admittance/applications')
})
const link = (a: FamilyListItem) => (a.stage === 'Draft' ? `/apply/${a.session.id}` : `/applications/${a.id}`)
</script>

<template>
  <div class="mx-auto max-w-3xl px-4 py-6 md:py-10">
    <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">My applications</h1>
    <p class="mt-1 text-muted-foreground">Retreats and programs that review applications before confirming a spot.</p>
    <div class="mt-6 space-y-3">
      <Skeleton v-if="!apps" class="h-24 w-full rounded-xl" />
      <Card v-else-if="!apps.length">
        <CardContent class="py-10 text-center text-muted-foreground">
          You haven't applied to anything yet.
          <Button variant="link" as-child class="h-auto p-0"
            ><RouterLink to="/programs">Browse programs</RouterLink></Button
          >
        </CardContent>
      </Card>
      <RouterLink v-for="a in apps" v-else :key="a.id" :to="link(a)" class="block rounded-xl focus-visible:ring-2">
        <Card class="transition-colors hover:bg-muted/40">
          <CardContent class="flex items-center gap-4">
            <div class="min-w-0 flex-1 space-y-1">
              <p class="font-medium">{{ a.session.program.name }} · {{ a.session.name }}</p>
              <p class="text-sm text-muted-foreground">
                {{ dateRange(a.session.startDate, a.session.endDate) }} · {{ a.couple }}
              </p>
              <div class="flex flex-wrap gap-2 pt-1">
                <StatusBadge :status="a.status" />
                <PaymentBadge v-if="a.paymentState !== 'None'" :state="a.paymentState" />
              </div>
            </div>
            <ChevronRight class="size-5 text-muted-foreground" />
          </CardContent>
        </Card>
      </RouterLink>
    </div>
  </div>
</template>
