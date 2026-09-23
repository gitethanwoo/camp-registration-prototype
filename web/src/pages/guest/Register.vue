<script setup lang="ts">
import { ArrowLeft, Check, ChevronUp, HeartPulse, Info, Loader2, Lock, ShieldCheck, TriangleAlert } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { useRegistrationDraft } from '@/composables/useRegistrationDraft'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, money } from '@/lib/format'
import type { Participant, Question, Quote, RegisterContext } from '@/lib/types'
import { cn } from '@/lib/utils'

const props = defineProps<{ sessionId: number }>()
const route = useRoute()
const router = useRouter()
const { draft, rotateKey, clear } = useRegistrationDraft(props.sessionId)

const ctx = ref<RegisterContext | null>(null)
const loadError = ref<string | null>(null)
const quote = ref<Quote | null>(null)
const attempted = ref(false)

const scheduleColumns: ColumnDef<Quote['schedule'][number]>[] = [
  { accessorKey: 'label', header: 'Payment' },
  { accessorKey: 'dueDate', header: 'Due', cell: ({ row }) => row.original.dueDate ? date(row.original.dueDate) : 'Today', meta: { cellClass: 'text-muted-foreground' } },
  { accessorKey: 'amountCents', header: 'Amount', cell: ({ row }) => money(row.original.amountCents), meta: { class: 'text-right', cellClass: 'tabular-nums' } },
]

const steps = [
  { key: 'participants', label: 'Participants' },
  { key: 'questions', label: 'Questions' },
  { key: 'health', label: 'Health' },
  { key: 'waivers', label: 'Waivers' },
  { key: 'review', label: 'Review' },
  { key: 'payment', label: 'Payment' },
] as const
const step = computed(() => steps[draft.step]?.key ?? 'participants')

onMounted(async () => {
  try {
    ctx.value = await api.get<RegisterContext>(`/sessions/${props.sessionId}/register-context`)
  }
  catch (e) {
    loadError.value = (e as Error).message
    return
  }
  if (ctx.value.program.type !== 'Standard') {
    loadError.value = 'This program uses a different registration flow that is not part of this prototype.'
    return
  }
  // Drop anything that's no longer selectable (e.g. registered in another tab).
  draft.selected = draft.selected.filter(id => selectable(byId(id)))
  // Coming from a pool's Register button: preselect eligible kids in that pool.
  const poolId = Number(route.query.pool)
  if (!draft.selected.length && poolId)
    draft.selected = ctx.value.participants.filter(p => selectable(p) && p.pool?.id === poolId).map(p => p.id)
  if (!draft.signer) draft.signer = ctx.value.household.signer
  if (draft.paymentOption === 'Plan' && !ctx.value.session.planInstallments) draft.paymentOption = 'Deposit'
  if (draft.paymentOption === 'Deposit' && !ctx.value.session.depositCents) draft.paymentOption = 'Full'
  for (const p of ctx.value.participants) {
    draft.answers[p.id] ??= {}
    draft.health[p.id] ??= {
      dietary: p.basicHealth.dietary ?? '', allergies: p.basicHealth.allergies ?? '', adaNeeds: p.basicHealth.adaNeeds ?? '',
      medications: '', physicianName: '', physicianPhone: '', insuranceProvider: '',
    }
  }
})

const byId = (id: number) => ctx.value?.participants.find(p => p.id === id)
const selectable = (p?: Participant) => p?.status === 'eligible'
const selected = computed(() => draft.selected.map(byId).filter((p): p is Participant => !!p))
// A child whose pool is already full goes to the waitlist and isn't charged.
const willWaitlist = (p: Participant) => p.pool?.state === 'full'
const seatable = computed(() => selected.value.filter(p => !willWaitlist(p)))
const allWaitlisted = computed(() => selected.value.length > 0 && seatable.value.length === 0)

function toggle(p: Participant, on: boolean | 'indeterminate') {
  if (on === true && !draft.selected.includes(p.id)) draft.selected.push(p.id)
  if (on !== true) draft.selected = draft.selected.filter(id => id !== p.id)
}

// ── Questions ────────────────────────────────────────────────────────────
const participantQuestions = computed(() => ctx.value?.questions.filter(q => q.scope === 'Participant') ?? [])
const householdQuestions = computed(() => ctx.value?.questions.filter(q => q.scope === 'Household') ?? [])
const visible = (q: Question, answers: Record<string, string>) => !q.showWhenKey || answers[q.showWhenKey] === q.showWhenValue
const missing = (q: Question, answers: Record<string, string>) => q.required && visible(q, answers) && !answers[q.key]?.trim()

// ── Waivers ──────────────────────────────────────────────────────────────
const waiverKey = (waiverId: number, personId?: number) => `${waiverId}:${personId ?? 'household'}`

// ── Step validation (client-side mirror; the server re-validates everything) ──
const stepErrors = computed<string[]>(() => {
  const c = ctx.value
  if (!c) return []
  switch (step.value) {
    case 'participants':
      return selected.value.length ? [] : ['Choose at least one participant.']
    case 'questions': {
      const errs: string[] = []
      for (const p of selected.value) {
        const n = participantQuestions.value.filter(q => missing(q, draft.answers[p.id] ?? {})).length
        if (n) errs.push(`${p.firstName}: ${n} required ${n === 1 ? 'question' : 'questions'} unanswered.`)
      }
      const h = householdQuestions.value.filter(q => missing(q, draft.householdAnswers)).length
      if (h) errs.push(`Family: ${h} required ${h === 1 ? 'question' : 'questions'} unanswered.`)
      return errs
    }
    case 'health':
      if (c.program.healthMechanism !== 'Embedded') return []
      return selected.value.filter(p => !draft.health[p.id]?.physicianName.trim()).map(p => `${p.firstName}: add a physician name.`)
    case 'waivers': {
      const errs = c.waivers.flatMap(w => w.perParticipant
        ? selected.value.filter(p => !draft.agreed[waiverKey(w.id, p.id)]).map(p => `${w.title}: agree for ${p.firstName}.`)
        : draft.agreed[waiverKey(w.id)] ? [] : [`${w.title}: agree to continue.`])
      if (!draft.signer.trim()) errs.push('Type your full name to sign.')
      return errs
    }
    default:
      return []
  }
})

function next() {
  attempted.value = true
  if (stepErrors.value.length) return
  attempted.value = false
  draft.step = Math.min(draft.step + 1, steps.length - 1)
  window.scrollTo({ top: 0 })
}
function back() {
  attempted.value = false
  if (draft.step === 0) router.push(`/programs/${ctx.value?.program.slug ?? ''}`)
  else draft.step -= 1
  window.scrollTo({ top: 0 })
}
function goTo(i: number) {
  if (i < draft.step) { attempted.value = false; draft.step = i }
}

// ── Quote (server-priced) ────────────────────────────────────────────────
const discountInput = ref(draft.discountCode)
const quoting = ref(false)
let quoteSeq = 0
async function refreshQuote() {
  if (!ctx.value) return
  const seq = ++quoteSeq
  quoting.value = true
  try {
    const q = await api.post<Quote>(`/sessions/${props.sessionId}/quote`, {
      personIds: seatable.value.map(p => p.id), paymentOption: draft.paymentOption, discountCode: draft.discountCode || null,
    })
    if (seq === quoteSeq) quote.value = q // ignore responses that arrive out of order
  }
  finally { if (seq === quoteSeq) quoting.value = false }
}
watch(() => [ctx.value, seatable.value.map(p => p.id).join(), draft.paymentOption, draft.discountCode], refreshQuote)
function applyCode() {
  draft.discountCode = discountInput.value.trim().toUpperCase()
}
function removeCode() {
  draft.discountCode = ''
  discountInput.value = ''
}

// ── Payment (simulated Fiserv hosted fields) ─────────────────────────────
const card = ref({ number: '', expiry: '', cvc: '', zip: '' })
const processing = ref(false)
const decline = ref<string | null>(null)
const serverErrors = ref<string[]>([])

const cardComplete = computed(() => card.value.number.replace(/\D/g, '').length >= 15 && /^\d{2}\s*\/\s*\d{2}$/.test(card.value.expiry) && card.value.cvc.length >= 3 && card.value.zip.length >= 5)

async function pay() {
  if (processing.value || !ctx.value) return
  decline.value = null
  serverErrors.value = []
  processing.value = true
  try {
    // In production this token comes from Fiserv's iframe; the card number never touches our API.
    const token = allWaitlisted.value ? '' : (await api.post<{ token: string }>('/fiserv-sandbox/tokenize', { cardNumber: card.value.number })).token
    const c = ctx.value
    const res = await api.post<{ confirmationCode: string, status: string }>('/checkout', {
      idempotencyKey: draft.idempotencyKey,
      sessionId: props.sessionId,
      participants: selected.value.map(p => ({
        personId: p.id,
        answers: Object.fromEntries(participantQuestions.value.filter(q => visible(q, draft.answers[p.id] ?? {})).map(q => [q.key, draft.answers[p.id]?.[q.key] ?? ''])),
        health: c.program.healthMechanism === 'Embedded' ? draft.health[p.id] : null,
      })),
      householdAnswers: Object.fromEntries(householdQuestions.value.filter(q => visible(q, draft.householdAnswers)).map(q => [q.key, draft.householdAnswers[q.key] ?? ''])),
      waivers: c.waivers.flatMap((w): { waiverId: number, personId: number | null, signerName: string }[] => w.perParticipant
        ? selected.value.map(p => ({ waiverId: w.id, personId: p.id, signerName: draft.signer }))
        : [{ waiverId: w.id, personId: null, signerName: draft.signer }]),
      paymentOption: draft.paymentOption,
      discountCode: quote.value?.appliedDiscountCode ?? null,
      cardToken: token,
    })
    clear()
    router.replace(`/confirmation/${res.confirmationCode}`)
  }
  catch (e) {
    if (e instanceof ApiError && e.status === 402) {
      decline.value = (e.body as { message?: string })?.message ?? 'Your card was declined.'
      rotateKey() // next attempt is a new payment intent; the declined one is closed
    }
    else if (e instanceof ApiError && e.status === 400) {
      serverErrors.value = Object.values(e.errors).flat()
      if (!serverErrors.value.length) serverErrors.value = [e.message]
    }
    else {
      toast.error('We couldn’t reach the payment service. You have not been charged twice; try again.')
    }
  }
  finally { processing.value = false }
}

const paymentOptions = computed(() => {
  const s = ctx.value?.session
  if (!s || !quote.value) return []
  const n = Math.max(seatable.value.length, 1)
  const opts = []
  if (s.depositCents) opts.push({ value: 'Deposit', label: 'Pay deposit', detail: `${money(s.depositCents * n)} today, balance due ${date(s.balanceDueDate)}` })
  opts.push({ value: 'Full', label: 'Pay in full', detail: `${money(quote.value.totalCents)} today` })
  if (s.planInstallments) opts.push({ value: 'Plan', label: 'Payment plan', detail: `Deposit today, then ${s.planInstallments} monthly payments by ${date(s.balanceDueDate)}` })
  return opts
})
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-6 md:py-10">
    <div v-if="loadError" class="mx-auto max-w-lg">
      <Alert variant="destructive">
        <TriangleAlert class="size-4" />
        <AlertTitle>Can't start registration</AlertTitle>
        <AlertDescription>{{ loadError }}</AlertDescription>
      </Alert>
      <Button as-child variant="outline" class="mt-4"><RouterLink to="/programs">Back to programs</RouterLink></Button>
    </div>

    <div v-else-if="!ctx" class="grid gap-8 lg:grid-cols-[1fr_360px]">
      <Skeleton class="h-[480px] rounded-xl" /><Skeleton class="h-72 rounded-xl" />
    </div>

    <template v-else>
      <Button variant="ghost" size="sm" class="-ml-2.5 mb-4 text-muted-foreground" @click="back">
        <ArrowLeft />{{ draft.step === 0 ? ctx.program.name : 'Back' }}
      </Button>
      <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">Register for {{ ctx.program.name }}</h1>
      <p class="mt-1 text-muted-foreground">{{ ctx.session.name }} · {{ dateRange(ctx.session.startDate, ctx.session.endDate) }}</p>

      <!-- Step indicator -->
      <nav aria-label="Registration progress" class="mt-6">
        <p class="text-sm font-medium md:hidden">Step {{ draft.step + 1 }} of {{ steps.length }} · {{ steps[draft.step]!.label }}</p>
        <ol class="mt-2 flex gap-1 md:hidden" aria-hidden="true">
          <li v-for="(s, i) in steps" :key="s.key" :class="cn('h-1.5 flex-1 rounded-full', i <= draft.step ? 'bg-primary' : 'bg-muted')" />
        </ol>
        <ol class="hidden items-center gap-2 md:flex">
          <li v-for="(s, i) in steps" :key="s.key" class="flex flex-1 items-center gap-2 last:flex-none">
            <Button
              variant="ghost"
              :disabled="i >= draft.step"
              :aria-current="i === draft.step ? 'step' : undefined"
              class="h-auto p-0 font-normal hover:bg-transparent disabled:opacity-100"
              @click="goTo(i)"
            >
              <span :class="cn('flex size-7 items-center justify-center rounded-full border text-xs font-medium',
                                i < draft.step && 'border-primary bg-primary text-primary-foreground',
                                i === draft.step && 'border-primary text-foreground ring-2 ring-primary/20')">
                <Check v-if="i < draft.step" class="size-4" /><template v-else>{{ i + 1 }}</template>
              </span>
              <span :class="i === draft.step ? 'font-medium' : 'text-muted-foreground'">{{ s.label }}</span>
            </Button>
            <Separator v-if="i < steps.length - 1" :class="cn('flex-1', i < draft.step && 'bg-primary')" />
          </li>
        </ol>
      </nav>

      <div class="mt-8 grid grid-cols-[minmax(0,1fr)] gap-8 lg:grid-cols-[1fr_360px]">
        <section class="min-w-0 space-y-6">
          <!-- R1 · Participants -->
          <Card v-if="step === 'participants'">
            <CardHeader>
              <CardTitle>Who's going?</CardTitle>
              <CardDescription>We place each child in the right group from their grade in fall {{ ctx.session.startDate.slice(0, 4) }}.</CardDescription>
            </CardHeader>
            <CardContent class="space-y-3">
              <label
                v-for="p in ctx.participants" :key="p.id"
                :class="cn('flex items-start gap-4 rounded-lg border p-4 transition-colors',
                           selectable(p) ? 'cursor-pointer hover:bg-muted/50' : 'bg-muted/40',
                           draft.selected.includes(p.id) && 'border-primary bg-primary/5')"
              >
                <Checkbox
                  class="mt-1"
                  :aria-label="`Register ${p.firstName}`"
                  :model-value="draft.selected.includes(p.id)"
                  :disabled="!selectable(p)"
                  @update:model-value="v => toggle(p, v)"
                />
                <div class="min-w-0 flex-1">
                  <div class="flex flex-wrap items-center gap-2">
                    <span class="font-medium">{{ p.firstName }} {{ p.lastName }}</span>
                    <StatusBadge v-if="p.status === 'registered'" status="Confirmed" label="Already registered" />
                    <StatusBadge v-else-if="p.status === 'waitlisted'" status="Waitlisted" label="On waitlist" />
                  </div>
                  <p class="text-sm text-muted-foreground">{{ p.gradeLabel }} · {{ p.gender === 'Male' ? 'Boy' : 'Girl' }}</p>
                  <p v-if="p.status === 'ineligible'" class="mt-1 text-sm text-muted-foreground">{{ p.reason }}</p>
                  <p v-else-if="p.pool && selectable(p)" :class="cn('mt-1 text-sm', p.pool.state === 'full' ? 'font-medium text-destructive' : p.pool.state === 'low' ? 'font-medium text-amber-700' : 'text-muted-foreground')">
                    {{ p.pool.name }} ·
                    <template v-if="p.pool.state === 'full'">Full. {{ p.firstName }} will join the waitlist (#{{ p.pool.waitlisted + 1 }}) and won't be charged.</template>
                    <template v-else>{{ p.pool.remaining }} {{ p.pool.remaining === 1 ? 'spot' : 'spots' }} left</template>
                  </p>
                </div>
                <span v-if="selectable(p)" class="text-sm tabular-nums">{{ willWaitlist(p) ? 'Waitlist' : money(ctx.session.priceCents) }}</span>
              </label>
              <p class="text-sm text-muted-foreground">
                Spots aren't held while you fill out forms. We reserve them the moment you pay, and if a group fills first, that child joins the waitlist instead.
              </p>
            </CardContent>
          </Card>

          <!-- R3 · Questions -->
          <template v-if="step === 'questions'">
            <Card v-for="p in selected" :key="p.id">
              <CardHeader>
                <CardTitle>About {{ p.firstName }}</CardTitle>
              </CardHeader>
              <CardContent class="grid gap-5 sm:grid-cols-2">
                <template v-for="q in participantQuestions" :key="q.key">
                  <div v-if="visible(q, draft.answers[p.id]!)" class="space-y-2">
                    <Label :for="`q-${p.id}-${q.key}`">{{ q.label }}<span v-if="q.required" class="text-destructive"> *</span></Label>
                    <Select v-if="q.type === 'Select'" v-model="draft.answers[p.id]![q.key]">
                      <SelectTrigger :id="`q-${p.id}-${q.key}`" class="w-full" :aria-invalid="attempted && missing(q, draft.answers[p.id]!) || undefined">
                        <SelectValue placeholder="Choose…" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="o in q.options" :key="o" :value="o">{{ o }}</SelectItem>
                      </SelectContent>
                    </Select>
                    <RadioGroup v-else-if="q.type === 'YesNo'" v-model="draft.answers[p.id]![q.key]" class="flex gap-6" :aria-invalid="attempted && missing(q, draft.answers[p.id]!) || undefined">
                      <Label v-for="o in ['Yes', 'No']" :key="o" class="font-normal"><RadioGroupItem :value="o" :aria-label="o" />{{ o }}</Label>
                    </RadioGroup>
                    <Input v-else :id="`q-${p.id}-${q.key}`" v-model="draft.answers[p.id]![q.key]" :aria-invalid="attempted && missing(q, draft.answers[p.id]!) || undefined" />
                  </div>
                </template>
              </CardContent>
            </Card>
            <Card v-if="householdQuestions.length">
              <CardHeader>
                <CardTitle>About your family</CardTitle>
                <CardDescription>Asked once for everyone you're registering.</CardDescription>
              </CardHeader>
              <CardContent class="grid gap-5 sm:grid-cols-2">
                <template v-for="q in householdQuestions" :key="q.key">
                  <div v-if="visible(q, draft.householdAnswers)" class="space-y-2">
                    <Label :for="`hq-${q.key}`">{{ q.label }}<span v-if="q.required" class="text-destructive"> *</span></Label>
                    <Select v-if="q.type === 'Select'" v-model="draft.householdAnswers[q.key]">
                      <SelectTrigger :id="`hq-${q.key}`" class="w-full" :aria-invalid="attempted && missing(q, draft.householdAnswers) || undefined"><SelectValue placeholder="Choose…" /></SelectTrigger>
                      <SelectContent><SelectItem v-for="o in q.options" :key="o" :value="o">{{ o }}</SelectItem></SelectContent>
                    </Select>
                    <RadioGroup v-else-if="q.type === 'YesNo'" v-model="draft.householdAnswers[q.key]" class="flex gap-6">
                      <Label v-for="o in ['Yes', 'No']" :key="o" class="font-normal"><RadioGroupItem :value="o" :aria-label="o" />{{ o }}</Label>
                    </RadioGroup>
                    <Input v-else :id="`hq-${q.key}`" v-model="draft.householdAnswers[q.key]" :aria-invalid="attempted && missing(q, draft.householdAnswers) || undefined" />
                  </div>
                </template>
              </CardContent>
            </Card>
          </template>

          <!-- R6 · Health -->
          <template v-if="step === 'health'">
            <template v-if="ctx.program.healthMechanism === 'Embedded'">
              <Card v-for="p in selected" :key="p.id">
                <CardHeader>
                  <CardTitle class="flex items-center gap-2"><HeartPulse class="size-5 text-muted-foreground" />{{ p.firstName }}'s health form</CardTitle>
                  <CardDescription>Only camp health staff can see this. We prefilled what you've told us before.</CardDescription>
                </CardHeader>
                <CardContent class="grid gap-5 sm:grid-cols-2">
                  <div class="space-y-2">
                    <Label :for="`h-${p.id}-allergies`">Allergies</Label>
                    <Input :id="`h-${p.id}-allergies`" v-model="draft.health[p.id]!.allergies" placeholder="None" />
                  </div>
                  <div class="space-y-2">
                    <Label :for="`h-${p.id}-dietary`">Dietary needs</Label>
                    <Input :id="`h-${p.id}-dietary`" v-model="draft.health[p.id]!.dietary" placeholder="None" />
                  </div>
                  <div class="space-y-2 sm:col-span-2">
                    <Label :for="`h-${p.id}-meds`">Medications taken during camp hours</Label>
                    <Textarea :id="`h-${p.id}-meds`" v-model="draft.health[p.id]!.medications" placeholder="Name, dose, and time" rows="2" />
                  </div>
                  <div class="space-y-2 sm:col-span-2">
                    <Label :for="`h-${p.id}-ada`">Accessibility or support needs</Label>
                    <Input :id="`h-${p.id}-ada`" v-model="draft.health[p.id]!.adaNeeds" placeholder="None" />
                  </div>
                  <div class="space-y-2">
                    <Label :for="`h-${p.id}-doc`">Physician name<span class="text-destructive"> *</span></Label>
                    <Input :id="`h-${p.id}-doc`" v-model="draft.health[p.id]!.physicianName" :aria-invalid="attempted && !draft.health[p.id]!.physicianName.trim() || undefined" autocomplete="off" />
                  </div>
                  <div class="space-y-2">
                    <Label :for="`h-${p.id}-docphone`">Physician phone</Label>
                    <Input :id="`h-${p.id}-docphone`" v-model="draft.health[p.id]!.physicianPhone" type="tel" />
                  </div>
                  <div class="space-y-2 sm:col-span-2">
                    <Label :for="`h-${p.id}-ins`">Insurance provider</Label>
                    <Input :id="`h-${p.id}-ins`" v-model="draft.health[p.id]!.insuranceProvider" />
                  </div>
                </CardContent>
              </Card>
            </template>
            <Card v-else>
              <CardHeader>
                <CardTitle class="flex items-center gap-2"><HeartPulse class="size-5 text-muted-foreground" />Health forms are completed in CampDoc</CardTitle>
                <CardDescription>{{ ctx.program.name }} uses CampDoc for medical records and medication tracking.</CardDescription>
              </CardHeader>
              <CardContent class="space-y-3 text-sm">
                <p>After you register, we'll email a CampDoc link for {{ selected.map(p => p.firstName).join(' and ') }}. The form's status shows up on your family checklist and updates automatically when you finish.</p>
                <p class="text-muted-foreground">Nothing to fill out here. Continue to waivers.</p>
              </CardContent>
            </Card>
          </template>

          <!-- R7 · Waivers -->
          <template v-if="step === 'waivers'">
            <Card v-for="w in ctx.waivers" :key="w.id">
              <CardHeader>
                <CardTitle>{{ w.title }}</CardTitle>
                <CardDescription>Version {{ w.version }} · effective {{ date(w.effectiveDate) }}</CardDescription>
              </CardHeader>
              <CardContent class="space-y-4">
                <ScrollArea class="h-40 rounded-md border bg-muted/30 p-4">
                  <p class="text-sm leading-relaxed whitespace-pre-line">{{ w.body }}</p>
                </ScrollArea>
                <div class="space-y-2">
                  <template v-if="w.perParticipant">
                    <Label v-for="p in selected" :key="p.id" class="flex items-center gap-3 font-normal">
                      <Checkbox v-model="draft.agreed[waiverKey(w.id, p.id)]" :aria-label="`${w.title}: I agree on behalf of ${p.firstName}`" :aria-invalid="attempted && !draft.agreed[waiverKey(w.id, p.id)] || undefined" />
                      I agree on behalf of {{ p.firstName }}
                    </Label>
                  </template>
                  <Label v-else class="flex items-center gap-3 font-normal">
                    <Checkbox v-model="draft.agreed[waiverKey(w.id)]" :aria-label="`${w.title}: I agree for my family`" :aria-invalid="attempted && !draft.agreed[waiverKey(w.id)] || undefined" />
                    I agree for my family
                  </Label>
                </div>
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>Sign</CardTitle>
                <CardDescription>Typing your name is your electronic signature on every waiver above.</CardDescription>
              </CardHeader>
              <CardContent class="max-w-sm space-y-2">
                <Label for="signer">Full name</Label>
                <Input id="signer" v-model="draft.signer" autocomplete="name" :aria-invalid="attempted && !draft.signer.trim() || undefined" />
                <p class="text-xs text-muted-foreground">Signed {{ date(new Date().toISOString()) }} · {{ ctx.household.email }}</p>
              </CardContent>
            </Card>
          </template>

          <!-- R9 · Review -->
          <template v-if="step === 'review'">
            <Card>
              <CardHeader><CardTitle>Review</CardTitle></CardHeader>
              <CardContent class="divide-y text-sm">
                <div v-for="p in selected" :key="p.id" class="flex items-center justify-between gap-4 py-3 first:pt-0">
                  <div>
                    <div class="font-medium">{{ p.firstName }} {{ p.lastName }}</div>
                    <div class="text-muted-foreground">{{ p.pool?.name }} · answers, health, and waivers complete</div>
                  </div>
                  <StatusBadge v-if="willWaitlist(p)" status="Waitlisted" label="Joins waitlist" />
                  <span v-else class="tabular-nums">{{ money(ctx.session.priceCents) }}</span>
                </div>
                <div class="flex justify-between py-3 last:pb-0">
                  <span class="text-muted-foreground">Signed by</span><span>{{ draft.signer }}</span>
                </div>
              </CardContent>
            </Card>

            <Card v-if="!allWaitlisted">
              <CardHeader>
                <CardTitle>How would you like to pay?</CardTitle>
              </CardHeader>
              <CardContent>
                <RadioGroup v-model="draft.paymentOption" class="gap-3">
                  <Label
                    v-for="o in paymentOptions" :key="o.value"
                    :class="cn('flex cursor-pointer items-start gap-3 rounded-lg border p-4 font-normal', draft.paymentOption === o.value && 'border-primary bg-primary/5')"
                  >
                    <RadioGroupItem :value="o.value" class="mt-0.5" :aria-label="o.label" />
                    <span>
                      <span class="block font-medium">{{ o.label }}</span>
                      <span class="block text-sm text-muted-foreground">{{ o.detail }}</span>
                    </span>
                  </Label>
                </RadioGroup>

                <div v-if="quote && draft.paymentOption === 'Plan'" class="mt-4 space-y-2">
                  <DataTable :columns="scheduleColumns" :data="quote.schedule" />
                  <p class="text-xs text-muted-foreground">Charged automatically to the card you use today. Final payment is due before camp starts.</p>
                </div>

                <Separator class="my-6" />
                <form class="flex max-w-sm items-end gap-2" @submit.prevent="applyCode">
                  <div class="flex-1 space-y-2">
                    <Label for="code">Discount code</Label>
                    <Input id="code" v-model="discountInput" autocomplete="off" class="uppercase" :aria-invalid="!!quote?.discountError || undefined" aria-describedby="code-msg" />
                  </div>
                  <Button type="submit" variant="outline" :disabled="!discountInput.trim()">Apply</Button>
                </form>
                <p id="code-msg" class="mt-2 text-sm" aria-live="polite">
                  <span v-if="quote?.discountError" class="text-destructive">{{ quote.discountError }}</span>
                  <span v-else-if="quote?.appliedDiscountCode" class="text-emerald-700">
                    {{ quote.appliedDiscountCode }} applied: −{{ money(quote.discountCents) }}
                    <Button type="button" variant="link" size="sm" class="ml-1 h-auto p-0 text-muted-foreground" @click="removeCode">Remove</Button>
                  </span>
                </p>
              </CardContent>
            </Card>
            <Alert v-else>
              <Info class="size-4" />
              <AlertTitle>No payment today</AlertTitle>
              <AlertDescription>Everyone you selected is joining a waitlist. If a spot opens, staff will email you an offer with a deadline, and you'll pay then.</AlertDescription>
            </Alert>
          </template>

          <!-- R10 · Payment -->
          <template v-if="step === 'payment'">
            <Alert v-if="decline" variant="destructive" aria-live="assertive">
              <TriangleAlert class="size-4" />
              <AlertTitle>Payment declined</AlertTitle>
              <AlertDescription>{{ decline }} Your spots were released and you weren't charged.</AlertDescription>
            </Alert>
            <Alert v-if="serverErrors.length" variant="destructive">
              <TriangleAlert class="size-4" />
              <AlertTitle>Please fix the following</AlertTitle>
              <AlertDescription><ul class="list-disc pl-4"><li v-for="e in serverErrors" :key="e">{{ e }}</li></ul></AlertDescription>
            </Alert>

            <Card v-if="!allWaitlisted">
              <CardHeader>
                <CardTitle>Payment</CardTitle>
                <CardDescription>{{ money(quote?.dueTodayCents) }} due today</CardDescription>
              </CardHeader>
              <CardContent>
                <!-- Stand-in for Fiserv hosted payment fields (an iframe in production). -->
                <fieldset class="space-y-4 rounded-lg border bg-muted/20 p-4" :disabled="processing">
                  <legend class="flex items-center gap-1.5 px-1 text-xs text-muted-foreground"><Lock class="size-3" />Secure payment by Fiserv · sandbox</legend>
                  <div class="space-y-2">
                    <Label for="cc">Card number</Label>
                    <Input id="cc" v-model="card.number" inputmode="numeric" autocomplete="cc-number" placeholder="4242 4242 4242 4242" />
                  </div>
                  <div class="grid grid-cols-3 gap-3">
                    <div class="space-y-2"><Label for="exp">Expiry</Label><Input id="exp" v-model="card.expiry" autocomplete="cc-exp" placeholder="MM / YY" /></div>
                    <div class="space-y-2"><Label for="cvc">CVC</Label><Input id="cvc" v-model="card.cvc" inputmode="numeric" autocomplete="cc-csc" placeholder="123" /></div>
                    <div class="space-y-2"><Label for="zip">ZIP</Label><Input id="zip" v-model="card.zip" inputmode="numeric" autocomplete="postal-code" placeholder="30303" /></div>
                  </div>
                </fieldset>
                <p class="mt-3 text-xs text-muted-foreground">
                  Test cards: <Button variant="link" class="h-auto p-0 font-mono text-xs text-muted-foreground" @click="card = { number: '4242 4242 4242 4242', expiry: '12 / 29', cvc: '123', zip: '30303' }">4242 4242 4242 4242</Button> approves,
                  <Button variant="link" class="h-auto p-0 font-mono text-xs text-muted-foreground" @click="card = { number: '4000 0000 0000 0002', expiry: '12 / 29', cvc: '123', zip: '30303' }">4000 0000 0000 0002</Button> declines.
                </p>
              </CardContent>
            </Card>
            <Alert v-else>
              <Info class="size-4" />
              <AlertTitle>No payment today</AlertTitle>
              <AlertDescription>Confirm below to join the waitlist.</AlertDescription>
            </Alert>
          </template>

          <!-- Step errors + navigation -->
          <Alert v-if="attempted && stepErrors.length" variant="destructive" aria-live="polite">
            <TriangleAlert class="size-4" />
            <AlertTitle>Before you continue</AlertTitle>
            <AlertDescription><ul class="list-disc pl-4"><li v-for="e in stepErrors" :key="e">{{ e }}</li></ul></AlertDescription>
          </Alert>
          <div class="flex items-center justify-between gap-3">
            <Button variant="ghost" @click="back">Back</Button>
            <Button v-if="step !== 'payment'" size="lg" @click="next">
              {{ step === 'review' ? 'Continue to payment' : 'Continue' }}
            </Button>
            <Button v-else size="lg" :disabled="processing || (!allWaitlisted && !cardComplete)" @click="pay">
              <Loader2 v-if="processing" class="size-4 animate-spin" />
              <ShieldCheck v-else class="size-4" />
              {{ processing ? 'Processing…' : allWaitlisted ? 'Join waitlist' : `Pay ${money(quote?.dueTodayCents)}` }}
            </Button>
          </div>
        </section>

        <!-- Order summary: sticky card on desktop, bottom sheet on phone -->
        <aside class="hidden lg:block">
          <Card class="sticky top-24">
            <CardHeader>
              <CardTitle>Order summary</CardTitle>
              <CardDescription>{{ ctx.program.name }} · {{ dateRange(ctx.session.startDate, ctx.session.endDate) }}</CardDescription>
            </CardHeader>
            <CardContent>
              <div :class="cn('space-y-2 text-sm transition-opacity', quoting && 'opacity-60')">
                <p v-if="!selected.length" class="text-muted-foreground">No participants selected yet.</p>
                <div v-for="p in selected" :key="p.id" class="flex justify-between gap-2">
                  <span>{{ p.firstName }} <span class="text-muted-foreground">· {{ p.pool?.name }}</span></span>
                  <span v-if="willWaitlist(p)" class="text-muted-foreground">Waitlist</span>
                  <span v-else class="tabular-nums">{{ money(ctx.session.priceCents) }}</span>
                </div>
                <template v-if="quote && seatable.length">
                  <div v-if="quote.discountCents" class="flex justify-between text-emerald-700"><span>Discount ({{ quote.appliedDiscountCode }})</span><span class="tabular-nums">−{{ money(quote.discountCents) }}</span></div>
                  <Separator />
                  <div class="flex justify-between font-medium"><span>Total</span><span class="tabular-nums">{{ money(quote.totalCents) }}</span></div>
                  <div class="flex justify-between text-base font-semibold"><span>Due today</span><span class="tabular-nums">{{ money(quote.dueTodayCents) }}</span></div>
                  <div v-if="quote.remainingCents" class="flex justify-between text-muted-foreground"><span>Remaining</span><span class="tabular-nums">{{ money(quote.remainingCents) }}</span></div>
                </template>
              </div>
            </CardContent>
          </Card>
        </aside>
      </div>

      <div class="fixed inset-x-0 bottom-14 z-20 border-t bg-background px-4 py-3 lg:hidden">
        <Sheet>
          <SheetTrigger class="flex w-full items-center justify-between text-left">
            <span>
              <span class="block text-xs text-muted-foreground">{{ selected.length }} {{ selected.length === 1 ? 'participant' : 'participants' }} · Due today</span>
              <span class="font-semibold tabular-nums">{{ quote && seatable.length ? money(quote.dueTodayCents) : '$0' }}</span>
            </span>
            <span class="flex items-center gap-1 text-sm text-muted-foreground">Details<ChevronUp class="size-4" /></span>
          </SheetTrigger>
          <SheetContent side="bottom" class="rounded-t-xl">
            <SheetHeader><SheetTitle>Order summary</SheetTitle></SheetHeader>
            <div class="space-y-2 px-4 pb-6 text-sm">
              <div v-for="p in selected" :key="p.id" class="flex justify-between">
                <span>{{ p.firstName }} · {{ p.pool?.name }}</span>
                <span>{{ willWaitlist(p) ? 'Waitlist' : money(ctx.session.priceCents) }}</span>
              </div>
              <template v-if="quote && seatable.length">
                <div v-if="quote.discountCents" class="flex justify-between text-emerald-700"><span>Discount</span><span>−{{ money(quote.discountCents) }}</span></div>
                <Separator />
                <div class="flex justify-between font-medium"><span>Total</span><span>{{ money(quote.totalCents) }}</span></div>
                <div class="flex justify-between font-semibold"><span>Due today</span><span>{{ money(quote.dueTodayCents) }}</span></div>
              </template>
            </div>
          </SheetContent>
        </Sheet>
      </div>
      <div class="h-20 lg:hidden" />
    </template>
  </div>
</template>
