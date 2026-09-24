<script setup lang="ts">
import { CalendarDays, CircleCheck, Info, Loader2, Lock, MapPin, TriangleAlert, UserMinus } from '@lucide/vue'
import { computed, onMounted, reactive, ref } from 'vue'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
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
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, dateTime } from '@/lib/format'
import { signIn } from '@/lib/session'
import type { LinkView } from './types'

const props = defineProps<{ token: string }>()
const view = ref<LinkView | null>(null)
const invalid = ref<string | null>(null)

const form = reactive({ email: '', phone: '', signerName: '', answers: {} as Record<string, string> })
const accepted = reactive<Record<number, boolean>>({})
const errors = ref<Record<string, string[]>>({})
const submitting = ref(false)
const editing = ref(false)

function hydrate(v: LinkView) {
  view.value = v
  form.email = v.attendee.email ?? ''
  form.phone = v.attendee.phone ?? ''
  form.signerName = form.signerName || v.attendee.name
  form.answers = { ...v.attendee.answers }
  for (const w of v.waivers) accepted[w.id] = w.accepted
}

onMounted(async () => {
  try {
    hydrate(await api.get<LinkView>(`/group-links/${encodeURIComponent(props.token)}`))
  } catch (e) {
    invalid.value =
      e instanceof ApiError && e.status === 404 ? e.message : 'We couldn’t open your forms. Check your connection.'
  }
})

const done = computed(() => view.value?.attendee.formStatus === 'Complete' && !editing.value)
const withdrawn = computed(() => view.value && !view.value.attendee.isActive)
const requested = computed(() => view.value?.attendee.withdrawal === 'Requested')
const err = (k: string) => errors.value[k]?.[0]

async function submit() {
  if (!view.value || submitting.value) return
  submitting.value = true
  errors.value = {}
  try {
    const v = await api.post<LinkView>(`/group-links/${encodeURIComponent(props.token)}/forms`, {
      email: form.email,
      phone: form.phone || null,
      signerName: form.signerName,
      answers: form.answers,
      waivers: view.value.waivers.map((w) => ({ waiverId: w.id, accepted: !!accepted[w.id] })),
    })
    hydrate(v)
    editing.value = false
    window.scrollTo({ top: 0 })
  } catch (e) {
    if (e instanceof ApiError && e.status === 400) {
      errors.value = e.errors
      toast.error('Some answers need attention. Check the highlighted fields.')
    } else if (e instanceof ApiError) toast.error(e.message)
    else toast.error('We couldn’t submit your forms. Check your connection and try again.')
  } finally {
    submitting.value = false
  }
}

const withdrawOpen = ref(false)
const reason = ref('')
const withdrawing = ref(false)
async function requestWithdrawal() {
  if (withdrawing.value) return
  withdrawing.value = true
  try {
    hydrate(
      await api.post<LinkView>(`/group-links/${encodeURIComponent(props.token)}/withdrawal`, {
        reason: reason.value || null,
      }),
    )
    withdrawOpen.value = false
    toast.success(`We told ${view.value?.leaderName} you'd like to withdraw.`)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'We couldn’t send your request. Try again.')
  } finally {
    withdrawing.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-3xl px-4 py-6 md:py-10">
    <div v-if="invalid" class="mx-auto max-w-lg py-10">
      <Alert variant="destructive">
        <TriangleAlert class="size-4" />
        <AlertTitle>This link isn’t working</AlertTitle>
        <AlertDescription>{{ invalid }}</AlertDescription>
      </Alert>
    </div>

    <div v-else-if="!view" class="space-y-4">
      <Skeleton class="h-10 w-80" /><Skeleton class="h-40 rounded-xl" /><Skeleton class="h-72 rounded-xl" />
    </div>

    <template v-else>
      <p class="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Lock class="size-3" />Secure link for {{ view.attendee.name }} · You don't need an account
      </p>
      <h1 class="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">
        Completing forms for {{ view.program.name }}
      </h1>
      <p class="mt-1 text-muted-foreground">
        {{ view.leaderName }} registered you for the {{ view.program.name }} with {{ view.groupName }}.
      </p>
      <div class="mt-3 flex flex-wrap gap-x-5 gap-y-1 text-sm text-muted-foreground">
        <span class="flex items-center gap-1.5"
          ><CalendarDays class="size-4" />{{ view.session.name }} ·
          {{ dateRange(view.session.startDate, view.session.endDate) }}</span
        >
        <span class="flex items-center gap-1.5"><MapPin class="size-4" />{{ view.program.location }}</span>
      </div>

      <!-- Withdrawn -->
      <Alert v-if="withdrawn" class="mt-8">
        <UserMinus class="size-4" />
        <AlertTitle>You've been withdrawn from this group</AlertTitle>
        <AlertDescription>
          {{ view.leaderName }} approved your request, so there's nothing left to complete. Questions? Ask your group
          leader.
        </AlertDescription>
      </Alert>

      <template v-else>
        <Alert v-if="requested" class="mt-8 border-amber-300 bg-amber-50/60">
          <UserMinus class="size-4" />
          <AlertTitle>Withdrawal requested</AlertTitle>
          <AlertDescription>
            {{ view.leaderName }} will review your request. Until then you're still on the roster.
          </AlertDescription>
        </Alert>

        <!-- Done -->
        <Card v-if="done" class="mt-8">
          <CardHeader>
            <CardTitle class="flex items-center gap-2 text-xl"
              ><CircleCheck class="size-6 text-emerald-600" />You're all set</CardTitle
            >
            <CardDescription>
              Your forms were submitted
              {{ view.attendee.submittedAt ? dateTime(view.attendee.submittedAt) : '' }}. {{ view.leaderName }} can see
              that you're done, but not your answers.
            </CardDescription>
          </CardHeader>
          <CardContent class="space-y-4">
            <p class="flex items-center gap-2 text-sm">Forms <StatusBadge status="Complete" /></p>
            <div class="rounded-lg border bg-muted/30 p-4 text-sm">
              <p class="font-medium">Want to see your registration later?</p>
              <p class="mt-1 text-muted-foreground">
                Creating an account is optional. Your forms are already saved either way.
              </p>
              <Button variant="outline" size="sm" class="mt-3" @click="signIn('/family')">Create an account</Button>
            </div>
            <Button variant="link" class="h-auto p-0" @click="editing = true">Update my answers</Button>
          </CardContent>
        </Card>

        <!-- Forms -->
        <form v-else class="mt-8 space-y-6" novalidate @submit.prevent="submit">
          <Alert>
            <Info class="size-4" />
            <AlertDescription
              >This takes about five minutes. Only you and WinShape staff can see your answers.</AlertDescription
            >
          </Alert>

          <Card>
            <CardHeader>
              <CardTitle>Your contact details</CardTitle>
              <CardDescription>We'll send updates about the cohort here.</CardDescription>
            </CardHeader>
            <CardContent class="grid gap-4 sm:grid-cols-2">
              <div class="space-y-2">
                <Label for="l-email">Email</Label>
                <Input
                  id="l-email"
                  v-model="form.email"
                  type="email"
                  autocomplete="email"
                  :aria-invalid="!!err('email')"
                />
                <p v-if="err('email')" class="text-sm text-destructive">{{ err('email') }}</p>
              </div>
              <div class="space-y-2">
                <Label for="l-phone">Mobile phone <span class="text-muted-foreground">(optional)</span></Label>
                <Input id="l-phone" v-model="form.phone" type="tel" autocomplete="tel" :aria-invalid="!!err('phone')" />
                <p v-if="err('phone')" class="text-sm text-destructive">{{ err('phone') }}</p>
              </div>
            </CardContent>
          </Card>

          <Card v-if="view.questions.length">
            <CardHeader>
              <CardTitle>A few questions</CardTitle>
            </CardHeader>
            <CardContent class="grid gap-5 sm:grid-cols-2">
              <div v-for="q in view.questions" :key="q.key" class="space-y-2">
                <Label :for="`q-${q.key}`"
                  >{{ q.label }}<span v-if="!q.required" class="text-muted-foreground"> (optional)</span></Label
                >
                <Select v-if="q.type === 'Select'" v-model="form.answers[q.key]">
                  <SelectTrigger :id="`q-${q.key}`" class="w-full" :aria-invalid="!!err(`answers.${q.key}`)"
                    ><SelectValue placeholder="Choose one"
                  /></SelectTrigger>
                  <SelectContent>
                    <SelectItem v-for="o in q.options" :key="o" :value="o">{{ o }}</SelectItem>
                  </SelectContent>
                </Select>
                <RadioGroup
                  v-else-if="q.type === 'YesNo'"
                  :id="`q-${q.key}`"
                  v-model="form.answers[q.key]"
                  class="flex gap-6"
                >
                  <div v-for="o in ['Yes', 'No']" :key="o" class="flex items-center gap-2">
                    <RadioGroupItem :id="`q-${q.key}-${o}`" :value="o" /><Label :for="`q-${q.key}-${o}`">{{ o }}</Label>
                  </div>
                </RadioGroup>
                <Input
                  v-else
                  :id="`q-${q.key}`"
                  v-model="form.answers[q.key]"
                  :aria-invalid="!!err(`answers.${q.key}`)"
                />
                <p v-if="err(`answers.${q.key}`)" class="text-sm text-destructive">{{ err(`answers.${q.key}`) }}</p>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Waivers</CardTitle>
              <CardDescription>Read each one, then check the box to agree.</CardDescription>
            </CardHeader>
            <CardContent class="space-y-6">
              <div v-for="w in view.waivers" :key="w.id" class="space-y-3">
                <div class="flex flex-wrap items-baseline justify-between gap-2">
                  <h3 class="font-medium">{{ w.title }}</h3>
                  <span class="text-xs text-muted-foreground"
                    >Version {{ w.version }} · effective {{ date(w.effectiveDate) }}</span
                  >
                </div>
                <ScrollArea class="h-40 rounded-md border bg-muted/20">
                  <p class="p-4 text-sm leading-relaxed whitespace-pre-line">{{ w.body }}</p>
                </ScrollArea>
                <div class="flex items-start gap-2">
                  <Checkbox
                    :id="`w-${w.id}`"
                    v-model="accepted[w.id]"
                    class="mt-0.5"
                    :aria-invalid="!!err(`waivers.${w.id}`)"
                  />
                  <Label :for="`w-${w.id}`" class="leading-snug font-normal"
                    >I have read and agree to the {{ w.title }}.</Label
                  >
                </div>
                <p v-if="err(`waivers.${w.id}`)" class="text-sm text-destructive">{{ err(`waivers.${w.id}`) }}</p>
              </div>
              <div class="max-w-sm space-y-2">
                <Label for="l-signer">Type your full name to sign</Label>
                <Input
                  id="l-signer"
                  v-model="form.signerName"
                  autocomplete="name"
                  :aria-invalid="!!err('signerName')"
                />
                <p v-if="err('signerName')" class="text-sm text-destructive">{{ err('signerName') }}</p>
              </div>
            </CardContent>
          </Card>

          <div class="flex flex-col-reverse gap-3 sm:flex-row sm:items-center sm:justify-between">
            <Button
              v-if="!requested"
              type="button"
              variant="ghost"
              class="text-muted-foreground"
              @click="withdrawOpen = true"
            >
              Can't attend? Request withdrawal
            </Button>
            <span v-else />
            <Button type="submit" size="lg" :disabled="submitting">
              <Loader2 v-if="submitting" class="size-4 animate-spin" />Submit forms
            </Button>
          </div>
        </form>

        <p v-if="done && !requested" class="mt-6 text-center">
          <Button variant="ghost" class="text-muted-foreground" @click="withdrawOpen = true"
            >Can't attend? Request withdrawal</Button
          >
        </p>
      </template>
    </template>

    <Dialog v-model:open="withdrawOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Request withdrawal</DialogTitle>
          <DialogDescription>
            {{ view?.leaderName }} paid for your spot, so they decide. If they approve, your share is refunded to their
            card.
          </DialogDescription>
        </DialogHeader>
        <div class="space-y-2">
          <Label for="w-reason">Reason <span class="text-muted-foreground">(optional)</span></Label>
          <Textarea id="w-reason" v-model="reason" rows="3" maxlength="1000" />
        </div>
        <DialogFooter>
          <Button variant="outline" @click="withdrawOpen = false">Cancel</Button>
          <Button :disabled="withdrawing" @click="requestWithdrawal">
            <Loader2 v-if="withdrawing" class="size-4 animate-spin" />Send request
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
