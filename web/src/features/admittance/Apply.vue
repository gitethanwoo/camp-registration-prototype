<script setup lang="ts">
import { ArrowLeft, Check, CircleAlert, Info, Loader2, ShieldCheck, TriangleAlert } from '@lucide/vue'
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardAction, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, money } from '@/lib/format'
import { cn } from '@/lib/utils'
import { cardComplete, emptyCard, newKey, tokenize } from './card'
import CardFields from './CardFields.vue'
import type { ApplyContext, FormQuestion } from './types'
import { now } from '@/lib/clock'

const props = defineProps<{ sessionId: number }>()
const router = useRouter()

const ctx = ref<ApplyContext | null>(null)
const loadError = ref<string | null>(null)
const appId = ref<number | null>(null)
const step = ref(0)
const reached = ref(0)
const attempted = ref(false)
const form = reactive({
  spousePersonId: null as number | null,
  spouseFirstName: '',
  spouseLastName: '',
  spouseEmail: '',
  answers: {} as Record<string, string>,
})

const steps = computed(() => [
  ...(ctx.value?.sections.map((s) => ({ key: s.key, label: s.title })) ?? []),
  { key: 'review', label: 'Review & submit' },
])
const onReview = computed(() => step.value === steps.value.length - 1)
const section = computed(() => ctx.value?.sections[step.value])

onMounted(async () => {
  try {
    ctx.value = await api.get<ApplyContext>(`/admittance/sessions/${props.sessionId}/apply`)
  } catch (e) {
    loadError.value =
      e instanceof ApiError && e.status === 404 ? "This program doesn't take applications." : (e as Error).message
    return
  }
  const a = ctx.value.application
  if (a && a.stage !== 'Draft') {
    router.replace(`/applications/${a.id}`)
    return
  }
  if (a) {
    appId.value = a.id
    form.spousePersonId = a.spousePersonId
    form.spouseFirstName = a.spouseFirstName
    form.spouseLastName = a.spouseLastName
    form.spouseEmail = a.spouseEmail ?? ''
    form.answers = { ...a.answers }
    step.value = Math.min(a.currentStep, ctx.value.sections.length)
    reached.value = step.value
    savedAt.value = new Date(a.updatedAt.endsWith('Z') ? a.updatedAt : `${a.updatedAt}Z`)
  } else if (ctx.value.otherAdults.length === 1) {
    pickSpouse(String(ctx.value.otherAdults[0]?.id))
  }
  // Start watching only after the saved draft is in place, so loading doesn't count as an edit.
  watch(form, scheduleSave, { deep: true })
})

// ── Spouse ────────────────────────────────────────────────────────────────
const spouseChoice = computed(() => (form.spousePersonId ? String(form.spousePersonId) : 'other'))
function pickSpouse(v: unknown) {
  const adult = ctx.value?.otherAdults.find((p) => String(p.id) === v)
  form.spousePersonId = adult?.id ?? null
  form.spouseFirstName = adult?.firstName ?? ''
  form.spouseLastName = adult?.lastName ?? ''
  form.spouseEmail = adult?.email ?? ''
}

// ── Questions ────────────────────────────────────────────────────────────
const visible = (q: FormQuestion) => !q.showWhenKey || form.answers[q.showWhenKey] === q.showWhenValue
const questionsIn = (key: string) => ctx.value?.questions.filter((q) => q.section === key && visible(q)) ?? []
const missing = (q: FormQuestion) => q.required && visible(q) && !form.answers[q.key]?.trim()
const spouseMissing = computed(() => !form.spouseFirstName.trim() || !form.spouseLastName.trim())

function errorsFor(i: number) {
  const s = ctx.value?.sections[i]
  if (!s) return []
  const errs = questionsIn(s.key)
    .filter(missing)
    .map((q) => q.label)
  if (s.key === 'couple' && spouseMissing.value) errs.unshift("Your spouse's first and last name")
  return errs
}
const stepErrors = computed(() => errorsFor(step.value))
const firstIncomplete = computed(() => ctx.value?.sections.findIndex((_, i) => errorsFor(i).length > 0) ?? -1)

// ── Autosave ─────────────────────────────────────────────────────────────
const saving = ref(false)
const saveFailed = ref(false)
const savedAt = ref<Date | null>(null)
let timer: ReturnType<typeof setTimeout> | undefined
let inFlight: Promise<void> | null = null

function scheduleSave() {
  clearTimeout(timer)
  timer = setTimeout(save, 700)
}
async function save() {
  clearTimeout(timer)
  timer = undefined
  if (inFlight) await inFlight
  inFlight = (async () => {
    saving.value = true
    try {
      const res = await api.put<{ id: number; updatedAt: string }>(`/admittance/sessions/${props.sessionId}/draft`, {
        spousePersonId: form.spousePersonId,
        spouseFirstName: form.spouseFirstName,
        spouseLastName: form.spouseLastName,
        spouseEmail: form.spouseEmail || null,
        answers: form.answers,
        step: step.value,
      })
      appId.value = res.id
      savedAt.value = now()
      saveFailed.value = false
    } catch (e) {
      saveFailed.value = true
      if (e instanceof ApiError && e.status === 409 && appId.value) router.replace(`/applications/${appId.value}`)
    } finally {
      saving.value = false
    }
  })()
  await inFlight
  inFlight = null
}
onBeforeUnmount(() => {
  if (timer) save()
})
const savedLabel = computed(() => {
  if (saving.value) return 'Saving…'
  if (saveFailed.value) return "Couldn't save. Check your connection; we'll try again on your next change."
  if (!savedAt.value) return 'Your answers save as you go.'
  return `Draft saved ${savedAt.value.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })}. You can come back to finish later.`
})

// ── Navigation ───────────────────────────────────────────────────────────
function go(i: number) {
  step.value = i
  reached.value = Math.max(reached.value, i)
  attempted.value = false
  save()
  window.scrollTo({ top: 0 })
}
function next() {
  if (stepErrors.value.length) {
    attempted.value = true
    return
  }
  go(step.value + 1)
}

// ── Waivers (signed here; approval records them on the registration) ────────
const agreed = reactive<Record<number, boolean>>({})
const signer = ref('')
watch(
  () => ctx.value?.applicant,
  (a) => {
    if (a && !signer.value) signer.value = `${a.firstName} ${a.lastName}`
  },
)
const waiversDone = computed(
  () => (ctx.value?.waivers ?? []).every((w) => agreed[w.id]) && (!ctx.value?.waivers.length || !!signer.value.trim()),
)

// ── Submit ───────────────────────────────────────────────────────────────
const card = ref(emptyCard())
let key = newKey()
const submitting = ref(false)
const decline = ref<string | null>(null)
const serverErrors = ref<string[]>([])

async function submit() {
  if (submitting.value || !ctx.value) return
  if (firstIncomplete.value >= 0) {
    go(firstIncomplete.value)
    attempted.value = true
    return
  }
  decline.value = null
  serverErrors.value = []
  submitting.value = true
  try {
    await save()
    if (!appId.value) throw new Error("We couldn't save your application. Check your connection and try again.")
    const token = await tokenize(card.value)
    await api.post(`/admittance/applications/${appId.value}/submit`, {
      cardToken: token,
      idempotencyKey: key,
      acceptedWaiverIds: ctx.value.waivers.filter((w) => agreed[w.id]).map((w) => w.id),
      signerName: signer.value.trim(),
    })
    toast.success('Application submitted. Your card is authorized, not charged.')
    router.replace(`/applications/${appId.value}`)
  } catch (e) {
    if (e instanceof ApiError && e.status === 402) {
      decline.value = e.message
      key = newKey()
    } else if (e instanceof ApiError && e.status === 400 && Object.keys(e.errors).length) {
      const labels: Record<string, string> = Object.fromEntries(
        ctx.value.questions.map((q) => [`answers.${q.key}`, q.label]),
      )
      for (const w of ctx.value.waivers) labels[`waivers.${w.id}`] = `Accept the ${w.title}`
      labels.signerName = 'Type your full name to sign the waiver'
      serverErrors.value = Object.entries(e.errors).map(([k, v]) => labels[k] ?? v[0] ?? k)
    } else if (e instanceof ApiError && e.status === 409) {
      const fresh = await api.get<ApplyContext>(`/admittance/sessions/${props.sessionId}/apply`).catch(() => null)
      if (fresh?.application && fresh.application.stage !== 'Draft') router.replace(`/applications/${appId.value}`)
      else serverErrors.value = [e.message]
    } else {
      serverErrors.value = [e instanceof Error ? e.message : "That didn't go through. Try again."]
    }
  } finally {
    submitting.value = false
  }
}

const price = computed(() => money(ctx.value?.session.priceCents))
const coupleName = computed(() => {
  const me = ctx.value?.applicant
  if (!me) return ''
  return form.spouseLastName === me.lastName
    ? `${me.firstName} and ${form.spouseFirstName} ${me.lastName}`
    : `${me.firstName} ${me.lastName} and ${form.spouseFirstName} ${form.spouseLastName}`
})
const answerFor = (q: FormQuestion) => form.answers[q.key]?.trim() || '—'
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-6 md:py-10">
    <Alert v-if="loadError" variant="destructive">
      <CircleAlert class="size-4" />
      <AlertTitle>We couldn't open this application</AlertTitle>
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>

    <template v-else-if="!ctx">
      <Skeleton class="h-8 w-72" />
      <Skeleton class="mt-6 h-10 w-full" />
      <Skeleton class="mt-8 h-96 w-full rounded-xl" />
    </template>

    <template v-else>
      <Button variant="ghost" size="sm" as-child class="-ml-3 mb-2">
        <RouterLink :to="`/programs/${ctx.session.program.slug}`"
          ><ArrowLeft class="size-4" />Back to program</RouterLink
        >
      </Button>
      <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">Apply for {{ ctx.session.program.name }}</h1>
      <p class="mt-1 text-muted-foreground">
        {{ ctx.session.name }} · {{ dateRange(ctx.session.startDate, ctx.session.endDate) }} · {{ price }} per couple
      </p>

      <Alert v-if="!ctx.applicant" variant="destructive" class="mt-6">
        <CircleAlert class="size-4" />
        <AlertTitle>Add yourself to your family account first</AlertTitle>
        <AlertDescription>We use the adults on your family account to fill in the couple's details.</AlertDescription>
      </Alert>

      <nav aria-label="Application steps" class="mt-6">
        <ol class="flex items-center gap-2">
          <li
            v-for="(s, i) in steps"
            :key="s.key"
            :class="cn('flex items-center gap-2', i < steps.length - 1 && 'flex-1')"
          >
            <Button
              variant="ghost"
              size="sm"
              class="h-auto gap-2 px-1 py-1"
              :disabled="i > reached"
              :aria-current="i === step ? 'step' : undefined"
              @click="go(i)"
            >
              <span
                :class="
                  cn(
                    'flex size-7 items-center justify-center rounded-full border text-xs font-medium',
                    i < step && 'border-primary bg-primary text-primary-foreground',
                    i === step && 'border-primary text-foreground ring-2 ring-primary/20',
                  )
                "
              >
                <Check v-if="i < step" class="size-4" /><template v-else>{{ i + 1 }}</template>
              </span>
              <span :class="cn('hidden md:inline', i === step ? 'font-medium' : 'text-muted-foreground')">{{
                s.label
              }}</span>
            </Button>
            <Separator v-if="i < steps.length - 1" :class="cn('flex-1', i < step && 'bg-primary')" />
          </li>
        </ol>
        <p class="mt-2 text-sm font-medium md:hidden">
          Step {{ step + 1 }} of {{ steps.length }}: {{ steps[step]?.label }}
        </p>
      </nav>

      <div class="mt-6 grid grid-cols-[minmax(0,1fr)] gap-8 lg:grid-cols-[1fr_340px]">
        <section class="min-w-0 space-y-6">
          <!-- Form sections -->
          <Card v-if="section">
            <CardHeader>
              <CardTitle>{{ section.title }}</CardTitle>
              <CardDescription>{{ section.description }}</CardDescription>
            </CardHeader>
            <CardContent class="space-y-6">
              <Alert v-if="attempted && stepErrors.length" variant="destructive">
                <TriangleAlert class="size-4" />
                <AlertTitle>Please answer the following</AlertTitle>
                <AlertDescription>
                  <ul class="list-disc pl-4">
                    <li v-for="e in stepErrors" :key="e">{{ e }}</li>
                  </ul>
                </AlertDescription>
              </Alert>

              <template v-if="section.key === 'couple'">
                <div class="grid gap-4 sm:grid-cols-2">
                  <div class="space-y-1">
                    <p class="text-sm font-medium">You</p>
                    <p class="text-sm">{{ ctx.applicant?.firstName }} {{ ctx.applicant?.lastName }}</p>
                    <p class="text-sm text-muted-foreground">{{ ctx.applicant?.email }}</p>
                  </div>
                  <div v-if="ctx.otherAdults.length" class="space-y-2">
                    <Label for="spouse">Your spouse</Label>
                    <Select :model-value="spouseChoice" @update:model-value="pickSpouse">
                      <SelectTrigger id="spouse" class="w-full"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="p in ctx.otherAdults" :key="p.id" :value="String(p.id)"
                          >{{ p.firstName }} {{ p.lastName }}</SelectItem
                        >
                        <SelectItem value="other">Someone not on our family account</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>
                <div v-if="!form.spousePersonId" class="grid gap-4 sm:grid-cols-2">
                  <div class="space-y-2">
                    <Label for="spouse-first">Spouse's first name<span class="text-destructive"> *</span></Label>
                    <Input
                      id="spouse-first"
                      v-model="form.spouseFirstName"
                      autocomplete="off"
                      :aria-invalid="(attempted && !form.spouseFirstName.trim()) || undefined"
                    />
                  </div>
                  <div class="space-y-2">
                    <Label for="spouse-last">Spouse's last name<span class="text-destructive"> *</span></Label>
                    <Input
                      id="spouse-last"
                      v-model="form.spouseLastName"
                      autocomplete="off"
                      :aria-invalid="(attempted && !form.spouseLastName.trim()) || undefined"
                    />
                  </div>
                  <div class="space-y-2 sm:col-span-2">
                    <Label for="spouse-email">Spouse's email (optional)</Label>
                    <Input id="spouse-email" v-model="form.spouseEmail" type="email" autocomplete="off" />
                  </div>
                </div>
              </template>

              <div v-for="q in questionsIn(section.key)" :key="q.key" class="space-y-2">
                <Label :for="`q-${q.key}`"
                  >{{ q.label }}<span v-if="q.required" class="text-destructive"> *</span></Label
                >
                <p v-if="q.help" :id="`q-${q.key}-help`" class="text-sm text-muted-foreground">{{ q.help }}</p>
                <Select v-if="q.type === 'Select'" v-model="form.answers[q.key]">
                  <SelectTrigger
                    :id="`q-${q.key}`"
                    class="w-full sm:w-80"
                    :aria-invalid="(attempted && missing(q)) || undefined"
                  >
                    <SelectValue placeholder="Choose…" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem v-for="o in q.options" :key="o" :value="o">{{ o }}</SelectItem>
                  </SelectContent>
                </Select>
                <RadioGroup
                  v-else-if="q.type === 'YesNo'"
                  v-model="form.answers[q.key]"
                  :aria-label="q.label"
                  class="flex gap-6"
                  :aria-invalid="(attempted && missing(q)) || undefined"
                >
                  <Label v-for="o in ['Yes', 'No']" :key="o" class="font-normal"
                    ><RadioGroupItem :value="o" :aria-label="o" />{{ o }}</Label
                  >
                </RadioGroup>
                <template v-else-if="q.type === 'LongText'">
                  <Textarea
                    :id="`q-${q.key}`"
                    v-model="form.answers[q.key]"
                    rows="5"
                    :maxlength="q.maxLength"
                    :aria-describedby="q.help ? `q-${q.key}-help` : undefined"
                    :aria-invalid="(attempted && missing(q)) || undefined"
                  />
                  <p class="text-right text-xs text-muted-foreground tabular-nums">
                    {{ form.answers[q.key]?.length ?? 0 }} / {{ q.maxLength }}
                  </p>
                </template>
                <Input
                  v-else
                  :id="`q-${q.key}`"
                  v-model="form.answers[q.key]"
                  :maxlength="q.maxLength"
                  :aria-describedby="q.help ? `q-${q.key}-help` : undefined"
                  :aria-invalid="(attempted && missing(q)) || undefined"
                />
              </div>
            </CardContent>
            <CardFooter class="flex flex-col-reverse gap-3 border-t pt-6 sm:flex-row sm:justify-between">
              <Button variant="outline" class="w-full sm:w-auto" :disabled="step === 0" @click="go(step - 1)"
                >Back</Button
              >
              <Button class="w-full sm:w-auto" @click="next">Save and continue</Button>
            </CardFooter>
          </Card>

          <!-- Review & submit -->
          <template v-if="onReview">
            <Card>
              <CardHeader>
                <CardTitle>Review your application</CardTitle>
                <CardDescription>You can't change answers after you submit, so check them now.</CardDescription>
              </CardHeader>
              <CardContent class="space-y-6">
                <div v-for="(s, i) in ctx.sections" :key="s.key">
                  <div class="flex items-center justify-between gap-2">
                    <h2 class="font-medium">{{ s.title }}</h2>
                    <Button variant="link" class="h-auto p-0" @click="go(i)">Edit</Button>
                  </div>
                  <dl class="mt-2 space-y-3 text-sm">
                    <div v-if="s.key === 'couple'">
                      <dt class="text-muted-foreground">Couple</dt>
                      <dd>{{ coupleName }}</dd>
                    </div>
                    <div v-for="q in questionsIn(s.key)" :key="q.key">
                      <dt class="text-muted-foreground">{{ q.label }}</dt>
                      <dd :class="cn('whitespace-pre-line break-words', missing(q) && 'text-destructive')">
                        {{ missing(q) ? 'Not answered yet' : answerFor(q) }}
                      </dd>
                    </div>
                  </dl>
                  <Separator v-if="i < ctx.sections.length - 1" class="mt-6" />
                </div>
              </CardContent>
            </Card>

            <Card v-for="w in ctx.waivers" :key="w.id">
              <CardHeader>
                <CardTitle>{{ w.title }}</CardTitle>
                <CardDescription>Version {{ w.version }} · effective {{ date(w.effectiveDate) }}</CardDescription>
              </CardHeader>
              <CardContent class="space-y-4">
                <ScrollArea class="h-40 rounded-md border bg-muted/30 p-4">
                  <p class="text-sm leading-relaxed whitespace-pre-line">{{ w.body }}</p>
                </ScrollArea>
                <Label class="flex items-center gap-3 font-normal">
                  <Checkbox v-model="agreed[w.id]" :aria-label="`${w.title}: I agree for us both`" />
                  I agree for us both
                </Label>
              </CardContent>
            </Card>
            <Card v-if="ctx.waivers.length">
              <CardHeader>
                <CardTitle>Sign</CardTitle>
                <CardDescription
                  >Typing your name is your electronic signature. It's recorded on your registration if you're
                  approved.</CardDescription
                >
              </CardHeader>
              <CardContent class="max-w-sm space-y-2">
                <Label for="signer">Full name</Label>
                <Input id="signer" v-model="signer" autocomplete="name" />
              </CardContent>
            </Card>

            <Alert v-if="decline" variant="destructive">
              <CircleAlert class="size-4" />
              <AlertTitle>Card declined</AlertTitle>
              <AlertDescription>{{ decline }} Your application is saved; nothing was authorized.</AlertDescription>
            </Alert>
            <Alert v-if="serverErrors.length" variant="destructive">
              <TriangleAlert class="size-4" />
              <AlertTitle>Please fix the following</AlertTitle>
              <AlertDescription>
                <ul class="list-disc pl-4">
                  <li v-for="e in serverErrors" :key="e">{{ e }}</li>
                </ul>
              </AlertDescription>
            </Alert>

            <Card>
              <CardHeader>
                <CardTitle>Payment</CardTitle>
                <CardDescription>{{ price }} authorized today, charged only if you're approved</CardDescription>
              </CardHeader>
              <CardContent class="space-y-4">
                <Alert class="border-amber-200 bg-amber-50 text-amber-900">
                  <Info class="size-4" />
                  <AlertDescription class="text-amber-900">
                    Your card will be authorized, not charged, until your application is approved. If it isn't approved,
                    the authorization is released and you pay nothing.
                  </AlertDescription>
                </Alert>
                <CardFields v-model="card" :disabled="submitting" />
                <p v-if="!waiversDone" class="text-sm text-muted-foreground">
                  Accept the waiver above and sign with your full name to submit.
                </p>
              </CardContent>
              <CardFooter class="flex flex-col-reverse gap-3 border-t pt-6 sm:flex-row sm:justify-between">
                <Button variant="outline" class="w-full sm:w-auto" :disabled="submitting" @click="go(step - 1)"
                  >Back</Button
                >
                <Button
                  class="w-full sm:w-auto"
                  :disabled="submitting || !cardComplete(card) || !waiversDone"
                  @click="submit"
                >
                  <Loader2 v-if="submitting" class="size-4 animate-spin" />
                  {{ submitting ? 'Authorizing…' : `Authorize ${price} and submit` }}
                </Button>
              </CardFooter>
            </Card>
          </template>

          <p class="text-sm text-muted-foreground" aria-live="polite">{{ savedLabel }}</p>
        </section>

        <aside class="lg:sticky lg:top-24 lg:self-start">
          <Card>
            <CardHeader>
              <CardTitle>{{ ctx.session.program.name }}</CardTitle>
              <CardDescription>{{ ctx.session.program.ministry }}</CardDescription>
              <CardAction><Badge variant="secondary">Draft</Badge></CardAction>
            </CardHeader>
            <CardContent class="space-y-4 text-sm">
              <dl class="space-y-2">
                <div v-if="form.spouseFirstName" class="flex justify-between gap-4">
                  <dt class="text-muted-foreground">Couple</dt>
                  <dd class="text-right">{{ coupleName }}</dd>
                </div>
                <div class="flex justify-between gap-4">
                  <dt class="text-muted-foreground">Dates</dt>
                  <dd class="text-right">{{ dateRange(ctx.session.startDate, ctx.session.endDate) }}</dd>
                </div>
                <div class="flex justify-between gap-4">
                  <dt class="text-muted-foreground">Where</dt>
                  <dd class="text-right">{{ ctx.session.program.location }}</dd>
                </div>
                <div class="flex justify-between gap-4">
                  <dt class="text-muted-foreground">Price</dt>
                  <dd class="text-right tabular-nums">{{ price }} per couple</dd>
                </div>
                <div class="flex justify-between gap-4">
                  <dt class="text-muted-foreground">Availability</dt>
                  <dd :class="cn('text-right', ctx.seatsLeft <= 5 && 'font-medium text-amber-700')">
                    {{ ctx.seatsLeft > 0 ? `${ctx.seatsLeft} ${ctx.seatsLeft === 1 ? 'spot' : 'spots'} left` : 'Full' }}
                  </dd>
                </div>
              </dl>
              <Separator />
              <p class="flex gap-2">
                <ShieldCheck class="mt-0.5 size-4 shrink-0 text-emerald-700" />
                <span
                  >If you're approved, your card is charged {{ price }} and your spot is confirmed. Until then it's only
                  authorized.</span
                >
              </p>
              <p v-if="ctx.seatsLeft === 0" class="text-muted-foreground">
                The retreat is full right now. You can still apply; if no spot opens, you'll be waitlisted and the
                authorization released.
              </p>
            </CardContent>
          </Card>
        </aside>
      </div>
    </template>
  </div>
</template>
