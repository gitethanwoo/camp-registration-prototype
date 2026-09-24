<script setup lang="ts">
import { Calendar, FileText, HeartPulse, Info, MapPin, RefreshCw, Tag, Wallet } from '@lucide/vue'
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import PoolAvailabilityList from '@/components/PoolAvailability.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateRange, money } from '@/lib/format'
import type { PoolAvailability, ProgramDetail } from '@/lib/types'

const props = defineProps<{ slug: string }>()
const router = useRouter()
const program = ref<ProgramDetail | null>(null)
const asOf = ref<Date | null>(null)
const now = ref(Date.now())
const stale = ref(false)
let poll: number | undefined
let tick: number | undefined

onMounted(async () => {
  program.value = await api.get<ProgramDetail>(`/programs/${props.slug}`)
  asOf.value = new Date()
  // NFR-1: near-real-time. Poll every 10s; if the API is unreachable, keep showing the
  // last-known numbers with a visible "as of" indicator instead of blanking the page.
  poll = window.setInterval(refresh, 10_000)
  tick = window.setInterval(() => (now.value = Date.now()), 1_000)
})
onUnmounted(() => {
  clearInterval(poll)
  clearInterval(tick)
})

async function refresh() {
  if (!program.value) return
  try {
    for (const s of program.value.sessions) {
      const a = await api.get<{ pools: PoolAvailability[] }>(`/sessions/${s.id}/availability`)
      s.pools = a.pools
    }
    asOf.value = new Date()
    stale.value = false
  } catch {
    stale.value = true
  }
}

const updatedLabel = computed(() => {
  if (!asOf.value) return ''
  const secs = Math.round((now.value - asOf.value.getTime()) / 1000)
  if (stale.value)
    return `Availability as of ${asOf.value.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })}`
  return secs < 5 ? 'Updated just now' : `Updated ${secs}s ago`
})

const standard = computed(() => program.value?.type === 'Standard')
const audience = computed(() => {
  const pools = program.value?.sessions.flatMap((s) => s.pools) ?? []
  return pools.length && pools.every((p) => p.name.match(/^(Couples|Attendees)$/)) ? 'adults' : 'campers'
})

function register(sessionId: number) {
  router.push(`/register/${sessionId}`)
}
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-6 md:py-10">
    <div v-if="!program" class="grid gap-8 lg:grid-cols-[1fr_420px]">
      <Skeleton class="h-96 rounded-xl" /><Skeleton class="h-96 rounded-xl" />
    </div>

    <div v-else class="grid grid-cols-[minmax(0,1fr)] gap-8 lg:grid-cols-[1fr_440px]">
      <section class="min-w-0">
        <div class="aspect-[21/9] overflow-hidden rounded-xl bg-muted">
          <img
            v-if="!program.imageUrl.includes('retreat') && !program.imageUrl.includes('leaders')"
            :src="program.imageUrl"
            alt=""
            class="size-full object-cover"
          />
        </div>
        <p class="mt-6 text-sm text-muted-foreground">
          {{ program.ministry
          }}<template v-if="program.hostOrganization"> · Hosted by {{ program.hostOrganization }}</template>
        </p>
        <h1 class="mt-1 text-3xl font-semibold tracking-tight md:text-4xl">{{ program.name }}</h1>
        <p class="mt-2 text-lg text-muted-foreground">{{ program.tagline }}</p>

        <dl v-for="s in program.sessions" :key="s.id" class="mt-6 grid gap-3 text-sm sm:grid-cols-2">
          <div class="flex items-center gap-2">
            <Calendar class="size-4 text-muted-foreground" />
            <dt class="sr-only">Dates</dt>
            <dd>{{ s.name }} · {{ dateRange(s.startDate, s.endDate) }}</dd>
          </div>
          <div class="flex items-center gap-2">
            <MapPin class="size-4 text-muted-foreground" />
            <dt class="sr-only">Location</dt>
            <dd>{{ program.location }}</dd>
          </div>
          <div class="flex items-center gap-2">
            <Tag class="size-4 text-muted-foreground" />
            <dt class="sr-only">Price</dt>
            <dd>
              {{ money(s.priceCents) }} per
              {{ audience === 'adults' ? (program.type === 'Admittance' ? 'couple' : 'attendee') : 'camper' }}
            </dd>
          </div>
          <div v-if="s.depositCents" class="flex items-center gap-2">
            <Wallet class="size-4 text-muted-foreground" />
            <dt class="sr-only">Deposit</dt>
            <dd>
              {{ money(s.depositCents) }} deposit<template v-if="s.planInstallments">
                · or {{ s.planInstallments }}-payment plan</template
              >
            </dd>
          </div>
        </dl>

        <p class="mt-6 max-w-prose leading-relaxed">{{ program.description }}</p>

        <Card class="mt-8">
          <CardHeader>
            <CardTitle>Requirements</CardTitle>
            <CardDescription>Completed during registration or before the session starts.</CardDescription>
          </CardHeader>
          <CardContent>
            <ul class="divide-y rounded-md border">
              <li v-for="r in program.requirements" :key="r" class="flex items-center gap-3 px-4 py-3 text-sm">
                <HeartPulse v-if="r.startsWith('Health')" class="size-4 text-muted-foreground" />
                <FileText v-else class="size-4 text-muted-foreground" />
                {{ r }}
              </li>
            </ul>
            <p v-if="program.healthMechanism === 'CampDoc'" class="mt-3 text-sm text-muted-foreground">
              Overnight Camp health forms are completed in CampDoc after you register. We'll give you the link.
            </p>
          </CardContent>
        </Card>
      </section>

      <aside class="lg:sticky lg:top-24 lg:self-start">
        <Card>
          <CardHeader>
            <CardTitle>Availability</CardTitle>
            <CardDescription class="flex items-center gap-1.5" aria-live="polite">
              <RefreshCw :class="['size-3.5', stale && 'text-amber-600']" />
              <span :class="stale && 'text-amber-700'">{{ updatedLabel }}</span>
            </CardDescription>
          </CardHeader>
          <CardContent class="space-y-4">
            <template v-for="s in program.sessions" :key="s.id">
              <p v-if="program.sessions.length > 1" class="text-sm font-medium">{{ s.name }}</p>
              <PoolAvailabilityList :pools="s.pools" />
              <Button v-if="standard" class="w-full" @click="register(s.id)">
                {{ program.sessions.length > 1 ? `Register for ${s.name}` : 'Register' }}
              </Button>
              <Button v-else-if="program.type === 'Admittance'" class="w-full" @click="router.push(`/apply/${s.id}`)">
                {{ program.sessions.length > 1 ? `Apply for ${s.name}` : 'Apply' }}
              </Button>
              <Button v-else-if="program.type === 'Cohort'" class="w-full" @click="router.push(`/groups/new/${s.id}`)">
                Register your group
              </Button>
            </template>
            <Alert v-if="!standard">
              <Info class="size-4" />
              <AlertTitle>{{
                program.type === 'Admittance' ? 'Application required' : 'Registered by a group leader'
              }}</AlertTitle>
              <AlertDescription>
                {{
                  program.type === 'Admittance'
                    ? 'Couples apply first; your card is authorized, not charged, until approval.'
                    : 'The group leader registers the cohort and each attendee completes forms by secure link.'
                }}
              </AlertDescription>
            </Alert>
            <p v-else class="text-xs text-muted-foreground">
              You'll pick which of your children to register next. We place each child in the right group by grade.
            </p>
          </CardContent>
        </Card>
      </aside>
    </div>
  </div>
</template>
