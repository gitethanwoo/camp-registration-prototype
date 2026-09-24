<script setup lang="ts">
import { Check, CircleAlert, Clock, Info, Loader2, MessageSquare, PartyPopper, TriangleAlert } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, dateTime, money } from '@/lib/format'
import { cn } from '@/lib/utils'
import { cardComplete, emptyCard, newKey, tokenize } from './card'
import CardFields from './CardFields.vue'
import PaymentBadge from './PaymentBadge.vue'
import { statusLabels, type FamilyApplication } from './types'

const props = defineProps<{ id: number }>()
const router = useRouter()
const app = ref<FamilyApplication | null>(null)
const loadError = ref<string | null>(null)

async function load() {
  try {
    const a = await api.get<FamilyApplication>(`/admittance/applications/${props.id}`)
    // A draft has no status yet; send the family back to finish it.
    if (a.stage === 'Draft') {
      router.replace(`/apply/${a.session.id}`)
      return
    }
    app.value = a
  } catch (e) {
    loadError.value =
      e instanceof ApiError && e.status === 404
        ? "We couldn't find that application on your account."
        : (e as Error).message
  }
}
onMounted(load)

const decided = computed(() => ['Approved', 'Declined', 'Waitlisted'].includes(app.value?.stage ?? ''))
const amount = computed(() => money(app.value?.payment.amountCents))
const card4 = computed(() => (app.value?.payment.cardLast4 ? ` ending ${app.value.payment.cardLast4}` : ''))

// Three steps, as on the concept: submitted, under review, decision.
const progress = computed(() => {
  const a = app.value
  if (!a) return []
  const decisions: Record<string, string> = {
    Approved: `Approved ${date(a.decidedAt)}`,
    Declined: 'Not approved',
    Waitlisted: 'Waitlisted',
  }
  const decision = decisions[a.stage] ?? 'Pending'
  let review = 'In progress'
  if (decided.value) review = 'Done'
  else if (a.stage === 'InfoRequested') review = 'Waiting on your answer'
  return [
    { label: 'Submitted', detail: date(a.submittedAt), state: 'done' },
    {
      label: 'Under review',
      detail: review,
      state: decided.value ? 'done' : 'current',
    },
    { label: 'Decision', detail: decision, state: decided.value ? 'done' : 'upcoming' },
  ] as const
})

const paymentText = computed(() => {
  const a = app.value
  if (!a) return ''
  const p = a.payment
  switch (p.state) {
    case 'Authorized':
    case 'Expiring':
      return `Your card${card4.value} is authorized for ${amount.value}. It will be charged only if your application is approved.`
    case 'Expired':
      return `Your card authorization for ${amount.value} expired. Nothing was charged. Re-enter payment details to keep your place.`
    case 'CardNeeded':
      return `You're approved and your spot is held. Your earlier authorization lapsed, so enter a card to pay ${amount.value} and confirm.`
    case 'Paid':
      return `${amount.value} paid on your card${card4.value}.`
    case 'Voided':
      return `The ${amount.value} authorization was released. You weren't charged.`
    default:
      return ''
  }
})

const nextStep = computed(() => {
  const a = app.value
  if (!a) return ''
  if (a.stage === 'InfoRequested')
    return "Answer our team's question below. We'll pick your application back up as soon as you do."
  if (a.payment.canUpdateCard)
    return a.stage === 'Approved'
      ? 'Enter a card below to pay and confirm your spot.'
      : 'Re-enter payment details below to keep your place.'
  if (a.status === 'Confirmed')
    return `You're confirmed. Confirmation ${a.confirmationCode}. We'll email arrival details the week before the retreat.`
  if (a.stage === 'Declined') return "Your application wasn't approved this time, and your card was never charged."
  if (a.stage === 'Waitlisted')
    return "You're on the waitlist, and your card isn't held. We'll email you if a spot opens."
  return "No action needed while we review. We'll email you when there's a decision."
})

// ── Reply to a question ──
const reply = ref('')
const replying = ref(false)
const replyError = ref<string | null>(null)
async function sendReply() {
  if (!reply.value.trim()) {
    replyError.value = 'Write a reply before sending.'
    return
  }
  replying.value = true
  replyError.value = null
  try {
    await api.post(`/admittance/applications/${props.id}/reply`, { message: reply.value })
    toast.success('Reply sent. Your application is back in review.')
    reply.value = ''
    await load()
  } catch (e) {
    replyError.value = (e as Error).message
  } finally {
    replying.value = false
  }
}

// ── New card ──
const card = ref(emptyCard())
let key = newKey()
const updating = ref(false)
const cardError = ref<string | null>(null)
async function updateCard() {
  updating.value = true
  cardError.value = null
  try {
    const token = await tokenize(card.value)
    await api.post(`/admittance/applications/${props.id}/reauthorize`, { cardToken: token, idempotencyKey: key })
    const wasApproved = app.value?.stage === 'Approved'
    toast.success(
      wasApproved ? `Paid ${amount.value}. You're confirmed.` : 'Card updated. It is authorized, not charged.',
    )
    card.value = emptyCard()
    key = newKey()
    await load()
  } catch (e) {
    cardError.value = (e as Error).message
    if (e instanceof ApiError && e.status === 402) key = newKey()
  } finally {
    updating.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-3xl px-4 py-6 md:py-10">
    <Alert v-if="loadError" variant="destructive">
      <CircleAlert class="size-4" />
      <AlertTitle>We couldn't open this application</AlertTitle>
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>

    <template v-else-if="!app">
      <Skeleton class="h-8 w-72" />
      <Skeleton class="mt-6 h-48 w-full rounded-xl" />
      <Skeleton class="mt-6 h-32 w-full rounded-xl" />
    </template>

    <template v-else>
      <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">{{ app.session.program.name }} application</h1>
      <div class="mt-1 text-muted-foreground">
        <p>{{ app.session.program.ministry }} · {{ app.session.name }}</p>
        <p>
          {{ dateRange(app.session.startDate, app.session.endDate) }} · {{ money(app.session.priceCents) }} per couple
        </p>
        <p>{{ app.couple }}</p>
      </div>

      <div class="mt-6 space-y-6">
        <Card>
          <CardHeader>
            <CardDescription>Application status</CardDescription>
            <CardTitle class="text-2xl">{{ statusLabels[app.status] ?? app.status }}</CardTitle>
          </CardHeader>
          <CardContent class="space-y-6">
            <ol class="grid gap-4 sm:grid-cols-3" aria-label="Application progress">
              <li v-for="(s, i) in progress" :key="s.label" class="flex items-start gap-3 sm:flex-col">
                <div class="flex items-center gap-2 sm:w-full">
                  <span
                    :class="
                      cn(
                        'flex size-8 shrink-0 items-center justify-center rounded-full border text-xs font-medium',
                        s.state === 'done' && 'border-primary bg-primary text-primary-foreground',
                        s.state === 'current' && 'border-primary text-primary ring-2 ring-primary/20',
                        s.state === 'upcoming' && 'text-muted-foreground',
                      )
                    "
                  >
                    <Check v-if="s.state === 'done'" class="size-4" />
                    <Clock v-else-if="s.state === 'current'" class="size-4" />
                    <template v-else>{{ i + 1 }}</template>
                  </span>
                  <span
                    v-if="i < progress.length - 1"
                    :class="cn('hidden h-px flex-1 sm:block', s.state === 'done' ? 'bg-primary' : 'bg-border')"
                  />
                </div>
                <div>
                  <p class="font-medium">{{ s.label }}</p>
                  <p class="text-sm text-muted-foreground">{{ s.detail }}</p>
                </div>
              </li>
            </ol>

            <Separator />
            <section class="space-y-2 text-sm">
              <h2 class="text-muted-foreground">Payment status</h2>
              <PaymentBadge :state="app.payment.state" />
              <p>{{ paymentText }}</p>
              <p
                v-if="app.payment.expiresAt && (app.payment.state === 'Authorized' || app.payment.state === 'Expiring')"
                class="text-muted-foreground"
              >
                The authorization holds until {{ date(app.payment.expiresAt) }}. If we need longer, we'll ask you to
                re-enter your card.
              </p>
            </section>

            <Separator />
            <section class="space-y-2 text-sm">
              <h2 class="text-muted-foreground">Next step</h2>
              <p class="flex gap-2">
                <PartyPopper v-if="app.status === 'Confirmed'" class="mt-0.5 size-4 shrink-0 text-emerald-700" />
                <Info v-else class="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                <span>{{ nextStep }}</span>
              </p>
              <p v-if="app.decisionNote" class="whitespace-pre-line rounded-md bg-muted/50 p-3">
                {{ app.decisionNote }}
              </p>
              <p v-if="app.stage === 'Declined' || app.stage === 'Waitlisted'" class="text-muted-foreground">
                Questions? Reply to the email we sent you and our team will get back to you.
              </p>
              <p v-if="app.infoResponse && app.stage === 'UnderReview'" class="text-muted-foreground">
                You answered our question on {{ app.infoRespondedAt ? dateTime(app.infoRespondedAt) : '' }}.
              </p>
            </section>
          </CardContent>
        </Card>

        <Card v-if="app.stage === 'InfoRequested'" class="border-amber-300">
          <CardHeader>
            <CardTitle class="flex items-center gap-2"
              ><MessageSquare class="size-5" />Our team has a question</CardTitle
            >
            <CardDescription>Asked {{ app.infoRequestedAt ? dateTime(app.infoRequestedAt) : '' }}</CardDescription>
          </CardHeader>
          <CardContent class="space-y-4">
            <p class="whitespace-pre-line rounded-md bg-muted/50 p-3 text-sm">{{ app.infoRequest }}</p>
            <div class="space-y-2">
              <Label for="reply">Your answer</Label>
              <Textarea
                id="reply"
                v-model="reply"
                rows="4"
                maxlength="2000"
                :aria-invalid="!!replyError || undefined"
              />
              <p v-if="replyError" class="text-sm text-destructive">{{ replyError }}</p>
            </div>
            <Button class="w-full sm:w-auto" :disabled="replying" @click="sendReply">
              <Loader2 v-if="replying" class="size-4 animate-spin" />Send answer
            </Button>
          </CardContent>
        </Card>

        <Card v-if="app.payment.canUpdateCard" class="border-amber-300 bg-amber-50/40">
          <CardHeader>
            <CardTitle class="flex items-center gap-2 text-amber-900">
              <TriangleAlert class="size-5" />{{
                app.stage === 'Approved' ? 'Enter a card to confirm your spot' : 'Authorization expired'
              }}
            </CardTitle>
            <CardDescription>
              {{
                app.stage === 'Approved'
                  ? `We'll charge ${amount} as soon as you submit.`
                  : `Re-enter payment details to keep your place. We'll authorize ${amount}, not charge it.`
              }}
            </CardDescription>
          </CardHeader>
          <CardContent class="space-y-4 text-sm">
            <CardFields v-model="card" :disabled="updating" />
            <p v-if="cardError" class="text-destructive">{{ cardError }}</p>
            <Button class="w-full sm:w-auto" :disabled="updating || !cardComplete(card)" @click="updateCard">
              <Loader2 v-if="updating" class="size-4 animate-spin" />
              {{ app.stage === 'Approved' ? `Pay ${amount}` : 'Update payment details' }}
            </Button>
          </CardContent>
        </Card>

        <Button variant="link" as-child class="px-0">
          <RouterLink to="/applications">All my applications</RouterLink>
        </Button>
      </div>
    </template>
  </div>
</template>
