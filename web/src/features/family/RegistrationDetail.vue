<script setup lang="ts">
import { ArrowLeft, ArrowRightLeft, CircleAlert, CircleCheck, ExternalLink, FileText, TriangleAlert } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, api } from '@/lib/api'
import { date, dateRange, money } from '@/lib/format'
import PaymentBadge from './PaymentBadge.vue'
import type { RegistrationDetail } from './types'

const props = defineProps<{ code: string }>()
const data = ref<RegistrationDetail | null>(null)
const notFound = ref(false)

async function load() {
  try {
    data.value = await api.get<RegistrationDetail>(`/family/registrations/${props.code}`)
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) notFound.value = true
    else throw e
  }
}
onMounted(load)

// F8 (staff-cx slice) moves one camper at a time. With one confirmed camper, go straight to their
// request; with several, the transfers page lists each camper with its own "Request transfer".
const transferable = computed(() => data.value?.participants.filter((p) => p.status === 'Confirmed') ?? [])
const transferPath = computed(() => {
  const only = transferable.value.length === 1 ? transferable.value[0] : undefined
  return only ? `/family/registrations/${only.registrationId}/transfer` : '/family/transfers'
})
const canTransfer = computed(() => !!data.value && !data.value.isPast && transferable.value.length > 0)

const active = computed(() => data.value?.participants.filter((p) => p.status !== 'Cancelled') ?? [])
const names = computed(() => {
  const n = active.value.map((p) => p.firstName)
  const last = active.value[0]?.lastName ?? ''
  return n.length <= 1 ? `${n[0] ?? ''} ${last}`.trim() : `${n.slice(0, -1).join(', ')} and ${n.at(-1)} ${last}`
})
const status = computed(() => active.value[0]?.status ?? 'Cancelled')

interface Row {
  key: string
  who: string
  what: string
  done: boolean
  state: string
  action?: { label: string; waiverId?: number; personName?: string; registrationId?: number; href?: string }
}
const rows = computed<Row[]>(() => {
  const d = data.value
  if (!d) return []
  const out: Row[] = []
  for (const p of d.participants.filter((x) => x.status === 'Confirmed')) {
    // Signed waivers collapse into one row per participant; each missing one gets its own row to sign.
    const missing = p.waivers.filter((w) => !w.signed)
    if (p.waivers.length && !missing.length)
      out.push({
        key: `w-${p.registrationId}`,
        who: p.firstName,
        what: p.waivers.length === 1 ? (p.waivers[0]?.title ?? 'Waiver') : `Waivers (${p.waivers.length} signed)`,
        done: true,
        state: 'Complete',
      })
    for (const w of missing)
      out.push({
        key: `w-${p.registrationId}-${w.id}`,
        who: p.firstName,
        what: w.title,
        done: false,
        state: 'Missing',
        action: { label: 'Review and sign', waiverId: w.id, personName: p.firstName, registrationId: p.registrationId },
      })
    if (p.healthStatus !== 'NotRequired') {
      const campdoc = d.program.healthMechanism === 'CampDoc'
      out.push({
        key: `h-${p.registrationId}`,
        who: p.firstName,
        what: campdoc ? 'Health forms in CampDoc' : 'Health form',
        done: p.healthStatus === 'Complete',
        state: p.healthStatus,
        action:
          campdoc && p.healthStatus !== 'Complete'
            ? { label: 'Open CampDoc', href: 'https://app.campdoc.com/' }
            : undefined,
      })
    }
  }
  return out
})

// Waiver signing
const signing = ref<{ waiverId: number; personName: string; registrationId: number } | null>(null)
const signOpen = ref(false)
const signer = ref('')
const agreed = ref(false)
const busy = ref(false)
const waiver = computed(() => data.value?.waivers.find((w) => w.id === signing.value?.waiverId))
function openSign(a: NonNullable<Row['action']>) {
  if (a.waiverId == null || a.registrationId == null) return
  signing.value = { waiverId: a.waiverId, personName: a.personName ?? '', registrationId: a.registrationId }
  signer.value = data.value?.signer ?? ''
  agreed.value = false
  signOpen.value = true
}
async function sign() {
  const s = signing.value
  const d = data.value
  if (!s || !d || !waiver.value) return
  const reg = d.participants.find((p) => p.registrationId === s.registrationId)
  busy.value = true
  try {
    // The API takes the person for per-participant waivers; a household waiver covers everyone.
    const person = waiver.value.perParticipant ? (reg?.personId ?? null) : null
    await api.post(`/family/registrations/${props.code}/waivers`, {
      waiverId: s.waiverId,
      personId: person,
      signerName: signer.value,
    })
    toast.success(`${waiver.value.title} signed${waiver.value.perParticipant ? ` for ${s.personName}` : ''}`)
    signOpen.value = false
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'The waiver wasn’t signed.')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-8 md:py-12">
    <Button variant="link" as-child class="h-auto p-0 text-muted-foreground">
      <RouterLink to="/family/registrations"><ArrowLeft />My registrations</RouterLink>
    </Button>

    <Alert v-if="notFound" variant="destructive" class="mt-6">
      <CircleAlert />
      <AlertDescription>No registration {{ code }} in your household.</AlertDescription>
    </Alert>

    <div v-else-if="!data" class="mt-6 grid gap-6 lg:grid-cols-3">
      <Skeleton class="h-64 rounded-xl" /><Skeleton class="h-64 rounded-xl" /><Skeleton class="h-64 rounded-xl" />
    </div>

    <template v-else>
      <h1 class="mt-4 text-3xl font-semibold tracking-tight md:text-4xl">{{ data.program.name }} registration</h1>
      <p class="mt-1 text-muted-foreground">
        {{ data.program.ministry }} · Confirmation <span class="font-mono">{{ data.confirmationCode }}</span>
      </p>

      <div class="mt-8 grid items-start gap-6 lg:grid-cols-3">
        <Card>
          <CardHeader><CardTitle>Registration details</CardTitle></CardHeader>
          <CardContent>
            <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-3 text-sm">
              <dt class="text-muted-foreground">Camp</dt>
              <dd>{{ data.program.name }}</dd>
              <dt class="text-muted-foreground">Session</dt>
              <dd>{{ data.session.name }}</dd>
              <dt class="text-muted-foreground">Dates</dt>
              <dd>{{ dateRange(data.session.startDate, data.session.endDate) }}</dd>
              <dt class="text-muted-foreground">Location</dt>
              <dd>{{ data.program.location }}</dd>
              <dt class="text-muted-foreground">Participants</dt>
              <dd>{{ names || '—' }}</dd>
              <dt class="text-muted-foreground">Status</dt>
              <dd><StatusBadge :status="status" /></dd>
            </dl>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>Participants</CardTitle></CardHeader>
          <CardContent>
            <ul class="divide-y">
              <li
                v-for="p in data.participants"
                :key="p.registrationId"
                class="flex items-center gap-3 py-3 first:pt-0"
              >
                <div class="min-w-0 flex-1">
                  <p class="font-medium">{{ p.firstName }} {{ p.lastName }}</p>
                  <p class="text-sm text-muted-foreground">
                    {{ [p.gradeLabel, p.pool].filter((x, i, all) => x && all.indexOf(x) === i).join(' · ') }}
                  </p>
                  <p v-if="p.movedTo" class="text-sm">
                    Moved to {{ p.movedTo.name }} · {{ dateRange(p.movedTo.startDate, p.movedTo.endDate) }}
                  </p>
                </div>
                <StatusBadge :status="p.status" />
              </li>
            </ul>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Payment summary</CardTitle>
            <CardAction><PaymentBadge :status="data.payment.paymentStatus" /></CardAction>
          </CardHeader>
          <CardContent class="space-y-3 text-sm">
            <div class="flex justify-between gap-4">
              <span
                >{{ data.payment.activeCount }} {{ data.payment.activeCount === 1 ? 'participant' : 'participants' }} ×
                {{ money(data.payment.priceCents) }}</span
              >
              <span class="tabular-nums">{{ money(data.payment.activeCount * data.payment.priceCents) }}</span>
            </div>
            <div v-if="data.payment.discountCents" class="flex justify-between gap-4 text-emerald-700">
              <span
                >Discount<template v-if="data.payment.discountCode"> ({{ data.payment.discountCode }})</template></span
              >
              <span class="tabular-nums">−{{ money(data.payment.discountCents) }}</span>
            </div>
            <div class="flex justify-between gap-4">
              <span>Paid</span><span class="tabular-nums">{{ money(data.payment.paidCents) }}</span>
            </div>
            <Separator />
            <div class="flex justify-between gap-4 text-base font-semibold">
              <span>Balance due</span><span class="tabular-nums">{{ money(data.payment.balanceCents) }}</span>
            </div>
            <p v-if="data.payment.installments.length" class="text-muted-foreground">
              {{ data.payment.installments.length }} × {{ money(data.payment.installments[0]?.amountCents) }} payment
              plan
            </p>
            <p v-else-if="data.payment.balanceCents > 0" class="text-muted-foreground">
              Due by {{ date(data.payment.balanceDueDate) }}
            </p>
            <div class="flex flex-wrap gap-2 pt-2">
              <Button v-if="canTransfer" as-child
                ><RouterLink :to="transferPath"><ArrowRightLeft />Request transfer</RouterLink></Button
              >
              <Button :variant="canTransfer ? 'outline' : 'default'" as-child>
                <RouterLink :to="`/family/registrations/${code}/payments`"
                  ><FileText />{{
                    data.payment.balanceCents > 0 ? 'Payments and balance' : 'View receipts'
                  }}</RouterLink
                >
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>

      <Card id="checklist" class="mt-6 scroll-mt-24">
        <CardHeader><CardTitle>Registration checklist</CardTitle></CardHeader>
        <CardContent>
          <p v-if="!rows.length" class="text-sm text-muted-foreground">
            {{ data.isPast ? 'This session has ended.' : 'Nothing to complete for this registration.' }}
          </p>
          <ul class="divide-y">
            <li
              v-for="r in rows"
              :key="r.key"
              class="grid grid-cols-[1fr_auto] items-center gap-x-4 gap-y-1 py-3 sm:grid-cols-[2fr_1fr_auto]"
            >
              <span class="text-sm">{{ r.who }} · {{ r.what }}</span>
              <span
                :class="[
                  'col-start-1 row-start-2 flex items-center gap-1.5 text-sm sm:col-start-2 sm:row-start-1',
                  r.done ? 'text-emerald-700' : 'text-amber-700',
                ]"
              >
                <CircleCheck v-if="r.done" class="size-4" /><TriangleAlert v-else class="size-4" />
                {{ r.state === 'NotRequired' ? 'Not required' : r.state }}
              </span>
              <div class="row-span-2 sm:row-span-1">
                <Button v-if="r.action?.href" size="sm" variant="outline" as-child>
                  <a :href="r.action.href" target="_blank" rel="noopener">{{ r.action.label }}<ExternalLink /></a>
                </Button>
                <Button v-else-if="r.action" size="sm" variant="outline" @click="openSign(r.action)">{{
                  r.action.label
                }}</Button>
              </div>
            </li>
          </ul>
        </CardContent>
      </Card>
    </template>

    <Dialog v-model:open="signOpen">
      <DialogContent v-if="waiver" class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ waiver.title }}</DialogTitle>
          <DialogDescription>
            Version {{ waiver.version }}, effective {{ date(waiver.effectiveDate) }}.
            {{ waiver.perParticipant ? `For ${signing?.personName}.` : 'Covers everyone on this registration.' }}
          </DialogDescription>
        </DialogHeader>
        <ScrollArea class="h-56 rounded-md border p-3">
          <p class="text-sm whitespace-pre-line">{{ waiver.body }}</p>
        </ScrollArea>
        <form id="sign-form" class="space-y-4" @submit.prevent="sign">
          <div class="flex items-start gap-2">
            <Checkbox id="agree" v-model="agreed" />
            <Label for="agree" class="leading-snug font-normal">I have read and agree to this waiver.</Label>
          </div>
          <div class="space-y-2">
            <Label for="signer">Type your full name to sign</Label>
            <Input id="signer" v-model="signer" autocomplete="name" />
          </div>
        </form>
        <DialogFooter>
          <Button variant="outline" @click="signOpen = false">Cancel</Button>
          <Button type="submit" form="sign-form" :disabled="busy || !agreed || !signer.trim()">Sign waiver</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
