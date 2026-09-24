<script setup lang="ts">
import { CircleAlert, Loader2 } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Separator } from '@/components/ui/separator'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { api } from '@/lib/api'
import { date, dateTime, money } from '@/lib/format'
import { useSession } from '@/lib/session'
import PaymentBadge from './PaymentBadge.vue'
import { isPending, stageLabels, type StaffDetail } from './types'

const props = defineProps<{ id: number | null }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ changed: [] }>()

const { session } = useSession()
// Reading is open to console staff; decisions are CET work (the API enforces it too).
const canDecide = computed(() => ['cet', 'admin'].includes(session.value?.role ?? ''))

const detail = ref<StaffDetail | null>(null)
const loadError = ref<string | null>(null)
async function load() {
  if (!props.id) return
  loadError.value = null
  try {
    detail.value = await api.get<StaffDetail>(`/admin/admittance/applications/${props.id}`)
  } catch (e) {
    loadError.value = (e as Error).message
  }
}
watch(
  () => props.id,
  () => {
    detail.value = null
    load()
  },
  { immediate: true },
)

const d = computed(() => detail.value)
const amount = computed(() => money(d.value?.payment.amountCents))
const holdGood = computed(() => ['Authorized', 'Expiring'].includes(d.value?.payment.state ?? ''))
const pending = computed(() => !!d.value && isPending(d.value.stage))
const full = computed(() => (d.value?.sessionRemaining ?? 1) <= 0)
const approvable = computed(() => pending.value || d.value?.stage === 'Waitlisted')
const sections = computed(() => {
  const out: { title: string; items: StaffDetail['answers'] }[] = []
  for (const a of d.value?.answers ?? []) {
    let s = out.find((x) => x.title === a.section)
    if (!s) out.push((s = { title: a.section, items: [] }))
    s.items.push(a)
  }
  return out
})

function expiryText(expiresAt: string | null) {
  if (!expiresAt) return ''
  const days = Math.ceil((new Date(expiresAt).getTime() - Date.now()) / 86_400_000)
  if (days <= 0) return 'Expired'
  return days === 1 ? 'Expires tomorrow' : `Expires in ${days} days (${date(expiresAt)})`
}

// ── Actions ──
const busy = ref(false)
const error = ref<string | null>(null)
const approveOpen = ref(false)
const waitlistOpen = ref(false)
const messageMode = ref<'info' | 'decline' | null>(null)
const message = ref('')

async function act(path: string, body: unknown, done: string) {
  if (!props.id) return false
  busy.value = true
  error.value = null
  try {
    const res = await api.post<{ captured?: boolean } | null>(
      `/admin/admittance/applications/${props.id}/${path}`,
      body,
    )
    toast.success(res?.captured === false ? `${done} We asked them to re-enter a card.` : done)
    await load()
    emit('changed')
    return true
  } catch (e) {
    error.value = (e as Error).message
    return false
  } finally {
    busy.value = false
  }
}

const couple = computed(() => d.value?.couple ?? 'the couple')
async function approve() {
  approveOpen.value = false
  await act(
    'approve',
    {},
    holdGood.value ? `Approved ${couple.value}. ${amount.value} charged; spot confirmed.` : `Approved ${couple.value}.`,
  )
}
async function waitlist() {
  waitlistOpen.value = false
  await act('waitlist', {}, `${couple.value} added to the waitlist. Their card hold was released.`)
}
function openMessage(mode: 'info' | 'decline') {
  message.value = ''
  error.value = null
  messageMode.value = mode
}
const messageError = ref<string | null>(null)
async function sendMessage() {
  if (!message.value.trim()) {
    messageError.value = messageMode.value === 'info' ? 'Write the question.' : 'Write a short message to the couple.'
    return
  }
  messageError.value = null
  const mode = messageMode.value
  const ok =
    mode === 'info'
      ? await act('request-info', { message: message.value }, `Question sent to ${couple.value}.`)
      : await act('decline', { message: message.value }, `Declined ${couple.value}. The card hold was released.`)
  if (ok) messageMode.value = null
}
</script>

<template>
  <Sheet v-model:open="open">
    <SheetContent class="w-full gap-0 p-0 sm:max-w-xl">
      <SheetHeader class="border-b p-4 pr-12">
        <SheetTitle>{{ d?.couple ?? 'Application' }}</SheetTitle>
        <SheetDescription v-if="d">
          Applied {{ d.submittedAt ? dateTime(d.submittedAt) : '—' }} · last activity {{ dateTime(d.lastActivity) }}
        </SheetDescription>
      </SheetHeader>

      <ScrollArea class="min-h-0 flex-1">
        <div class="space-y-6 p-4">
          <Alert v-if="loadError" variant="destructive">
            <CircleAlert class="size-4" />
            <AlertTitle>Couldn't load this application</AlertTitle>
            <AlertDescription>{{ loadError }}</AlertDescription>
          </Alert>
          <template v-else-if="!d">
            <Skeleton class="h-6 w-40" />
            <Skeleton class="h-40 w-full" />
            <Skeleton class="h-24 w-full" />
          </template>
          <template v-else>
            <section class="flex flex-wrap items-center gap-2">
              <Badge variant="secondary">{{ stageLabels[d.stage] }}</Badge>
              <PaymentBadge :state="d.payment.state" />
              <span v-if="d.reviewedBy" class="text-sm text-muted-foreground">by {{ d.reviewedBy }}</span>
            </section>

            <Alert v-if="error" variant="destructive">
              <CircleAlert class="size-4" />
              <AlertTitle>That didn't go through</AlertTitle>
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>

            <section>
              <h3 class="text-sm font-medium">Applicants</h3>
              <dl class="mt-2 grid gap-3 text-sm sm:grid-cols-2">
                <div>
                  <dt class="text-muted-foreground">Applicant</dt>
                  <dd>{{ d.applicant.firstName }} {{ d.applicant.lastName }}</dd>
                  <dd class="break-all text-muted-foreground">{{ d.applicant.email }}</dd>
                </div>
                <div>
                  <dt class="text-muted-foreground">Spouse</dt>
                  <dd>{{ d.spouse.firstName }} {{ d.spouse.lastName }}</dd>
                  <dd v-if="d.spouse.email" class="break-all text-muted-foreground">{{ d.spouse.email }}</dd>
                </div>
                <div v-if="d.household.phone || d.household.city">
                  <dt class="text-muted-foreground">Household</dt>
                  <dd>{{ [d.household.phone, d.household.city].filter(Boolean).join(' · ') }}</dd>
                </div>
              </dl>
            </section>

            <section v-if="d.infoRequest" class="rounded-md border p-3 text-sm">
              <p class="font-medium">Question to the couple</p>
              <p class="whitespace-pre-line">{{ d.infoRequest }}</p>
              <p class="mt-1 text-xs text-muted-foreground">
                Asked {{ d.infoRequestedAt ? dateTime(d.infoRequestedAt) : '' }}
              </p>
              <Separator class="my-3" />
              <template v-if="d.infoResponse">
                <p class="font-medium">Their answer</p>
                <p class="whitespace-pre-line">{{ d.infoResponse }}</p>
              </template>
              <p v-else class="text-muted-foreground">Waiting on their answer.</p>
            </section>

            <section v-for="s in sections" :key="s.title">
              <h3 class="text-sm font-medium">{{ s.title }}</h3>
              <dl class="mt-2 space-y-3 text-sm">
                <div v-for="a in s.items" :key="a.key">
                  <dt class="text-muted-foreground">{{ a.label }}</dt>
                  <dd class="whitespace-pre-line break-words">{{ a.answer }}</dd>
                </div>
              </dl>
            </section>

            <section class="rounded-md border p-3 text-sm">
              <h3 class="font-medium">Payment</h3>
              <dl class="mt-2 space-y-2">
                <div class="flex justify-between gap-4">
                  <dt class="text-muted-foreground">
                    {{ d.payment.state === 'Paid' ? 'Amount charged' : 'Amount authorized' }}
                  </dt>
                  <dd class="tabular-nums">{{ amount }}</dd>
                </div>
                <div v-if="d.payment.cardLast4" class="flex justify-between gap-4">
                  <dt class="text-muted-foreground">Card</dt>
                  <dd>Ending {{ d.payment.cardLast4 }}</dd>
                </div>
                <div v-if="d.payment.expiresAt" class="flex justify-between gap-4">
                  <dt class="text-muted-foreground">Authorization</dt>
                  <dd :class="d.payment.state !== 'Authorized' ? 'font-medium text-amber-700' : ''">
                    {{ expiryText(d.payment.expiresAt) }}
                  </dd>
                </div>
              </dl>
              <p class="mt-2 text-muted-foreground">
                <template v-if="holdGood">No charge is captured until the application is approved.</template>
                <template v-else-if="d.payment.state === 'Expired'"
                  >If you approve now, we hold their spot and ask them to re-enter a card. Nothing is charged until they
                  do.</template
                >
                <template v-else-if="d.payment.state === 'CardNeeded'"
                  >Approved with the spot held. Waiting on the couple to enter a new card.</template
                >
                <template v-else-if="d.payment.state === 'Voided'"
                  >The hold was released. Nothing was charged.</template
                >
              </p>
              <Button v-if="d.registrationId" variant="link" as-child class="mt-1 h-auto p-0">
                <RouterLink :to="`/admin/registrations/${d.registrationId}`">Open registration</RouterLink>
              </Button>
            </section>

            <section v-if="d.history.length">
              <h3 class="text-sm font-medium">History</h3>
              <ol class="mt-2 space-y-2 text-sm">
                <li v-for="(h, i) in d.history" :key="i">
                  <p>{{ h.detail }}</p>
                  <p class="text-xs text-muted-foreground">{{ h.actor }} · {{ dateTime(h.createdAt) }}</p>
                </li>
              </ol>
            </section>
          </template>
        </div>
      </ScrollArea>

      <SheetFooter v-if="d && canDecide && (approvable || d.payment.state === 'CardNeeded')" class="border-t p-4">
        <p v-if="approvable && full" class="text-sm text-muted-foreground">
          {{ d.session.name }} is full. Waitlist this couple, or raise capacity to approve.
        </p>
        <div class="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:justify-end">
          <Button
            v-if="d.stage === 'Submitted'"
            variant="outline"
            :disabled="busy"
            @click="act('start-review', {}, `Review started for ${couple}.`)"
            >Start review</Button
          >
          <Button
            v-if="d.stage === 'Submitted' || d.stage === 'UnderReview'"
            variant="outline"
            :disabled="busy"
            @click="openMessage('info')"
            >Request info</Button
          >
          <Button variant="outline" class="text-destructive" :disabled="busy" @click="openMessage('decline')"
            >Decline</Button
          >
          <Button v-if="pending && full" variant="secondary" :disabled="busy" @click="waitlistOpen = true"
            >Waitlist</Button
          >
          <Button v-if="approvable" :disabled="busy || full" @click="approveOpen = true">
            <Loader2 v-if="busy" class="size-4 animate-spin" />Approve application
          </Button>
        </div>
      </SheetFooter>
    </SheetContent>
  </Sheet>

  <AlertDialog v-model:open="approveOpen">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>Approve {{ couple }}?</AlertDialogTitle>
        <AlertDialogDescription>
          <template v-if="holdGood"
            >This charges {{ amount }} to the card ending {{ d?.payment.cardLast4 }} and confirms their spot.</template
          >
          <template v-else
            >Their card authorization has lapsed. Approving holds their spot and asks them to re-enter a card. Nothing
            is charged until they do.</template
          >
          {{ (d?.sessionRemaining ?? 1) - 1 }} of {{ d?.session.name }}'s spots will be left.
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>Cancel</AlertDialogCancel>
        <AlertDialogAction @click="approve">{{
          holdGood ? `Approve and charge ${amount}` : 'Approve'
        }}</AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>

  <AlertDialog v-model:open="waitlistOpen">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>Waitlist {{ couple }}?</AlertDialogTitle>
        <AlertDialogDescription>
          The session is full. Their {{ amount }} authorization is released now; if a spot opens, they'll re-enter a
          card to confirm.
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>Cancel</AlertDialogCancel>
        <AlertDialogAction @click="waitlist">Waitlist</AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>

  <Dialog :open="messageMode !== null" @update:open="(v) => !v && (messageMode = null)">
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{{ messageMode === 'info' ? 'Ask the couple a question' : `Decline ${couple}` }}</DialogTitle>
        <DialogDescription>
          {{
            messageMode === 'info'
              ? "We'll email your question. The application waits in review until they answer."
              : `We'll email this message and release the ${amount} authorization. They won't be charged.`
          }}
        </DialogDescription>
      </DialogHeader>
      <div class="space-y-2">
        <Label for="reader-message">{{ messageMode === 'info' ? 'Question' : 'Message to the couple' }}</Label>
        <Textarea
          id="reader-message"
          v-model="message"
          rows="4"
          maxlength="2000"
          :aria-invalid="!!messageError || undefined"
        />
        <p v-if="messageError" class="text-sm text-destructive">{{ messageError }}</p>
        <p v-if="error" class="text-sm text-destructive">{{ error }}</p>
      </div>
      <DialogFooter>
        <Button variant="outline" @click="messageMode = null">Cancel</Button>
        <Button :variant="messageMode === 'decline' ? 'destructive' : 'default'" :disabled="busy" @click="sendMessage">
          <Loader2 v-if="busy" class="size-4 animate-spin" />{{ messageMode === 'info' ? 'Send question' : 'Decline' }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
