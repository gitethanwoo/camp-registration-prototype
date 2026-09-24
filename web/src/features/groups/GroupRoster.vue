<script setup lang="ts">
import {
  ArrowLeft,
  Check,
  ClipboardPaste,
  Info,
  Loader2,
  MailWarning,
  Plus,
  ShieldCheck,
  TriangleAlert,
  Upload,
  X,
} from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
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
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { dateRange, money } from '@/lib/format'
import { cn } from '@/lib/utils'
import CardFields, { type CardInput } from './CardFields.vue'
import { mergeRows, parseRoster, row } from './roster'
import type { GroupDetail, RosterDraftRow, StartContext } from './types'

const props = defineProps<{ sessionId?: number; groupId?: number }>()
const router = useRouter()

const ctx = ref<StartContext | null>(null)
const loadError = ref<string | null>(null)
const groupId = ref<number | null>(props.groupId ?? null)
const groupName = ref('')
const rows = ref<RosterDraftRow[]>([row()])
const dirty = ref(false)
const step = ref<'attendees' | 'pay'>('attendees')
const saved = ref<GroupDetail | null>(null)
const saving = ref(false)
const fieldErrors = ref<Record<string, string[]>>({})
const formError = ref<string | null>(null)

onMounted(async () => {
  try {
    let sessionId = props.sessionId
    if (props.groupId) {
      const g = await api.get<GroupDetail>(`/groups/${props.groupId}`)
      if (g.status === 'Confirmed') {
        router.replace(`/groups/${g.id}`)
        return
      }
      groupName.value = g.name
      rows.value = g.attendees.map((a) => row(a.name, a.email ?? ''))
      sessionId = g.session.id
    }
    ctx.value = await api.get<StartContext>(`/groups/start/${sessionId}`)
    dirty.value = false
  } catch (e) {
    loadError.value =
      e instanceof ApiError && e.status === 404
        ? 'We couldn’t find that cohort session or group. It may have been removed.'
        : 'We couldn’t load this page. Check your connection and refresh.'
  }
})
watch([rows, groupName], () => (dirty.value = true), { deep: true })

const filled = computed(() => rows.value.filter((r) => r.name.trim() || r.email.trim()))
const missingEmail = computed(() => filled.value.filter((r) => !r.email.trim()).length)
const overCapacity = computed(() => !!ctx.value && filled.value.length > ctx.value.remaining)
const otherGroups = computed(() => ctx.value?.existingGroups.filter((g) => g.id !== groupId.value) ?? [])

function addRow() {
  rows.value.push(row())
}
function removeRow(key: number) {
  rows.value = rows.value.filter((r) => r.key !== key)
  if (!rows.value.length) rows.value.push(row())
}
function errorFor(i: number, field: 'name' | 'email') {
  return fieldErrors.value[`attendees[${i}].${field}`]?.[0]
}

// ── Paste list / upload CSV ──────────────────────────────────────────────
const pasteOpen = ref(false)
const pasteText = ref('')
const fileInput = ref<{ $el: HTMLInputElement } | null>(null)

function importText(text: string, source: string) {
  const parsed = parseRoster(text)
  if (!parsed.length) {
    toast.error(`No attendees found in the ${source}. Put one person per line: name, then email.`)
    return false
  }
  const { rows: merged, added, skipped } = mergeRows(rows.value, parsed)
  rows.value = merged.length ? merged : [row()]
  toast.success(
    `Added ${added} ${added === 1 ? 'attendee' : 'attendees'}` +
      (skipped ? `; skipped ${skipped} already on the roster.` : '.'),
  )
  return true
}
function applyPaste() {
  if (importText(pasteText.value, 'pasted list')) {
    pasteText.value = ''
    pasteOpen.value = false
  }
}
async function onFile(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (file) importText(await file.text(), 'file')
  input.value = ''
}

// ── Save and continue ────────────────────────────────────────────────────
async function save(): Promise<boolean> {
  if (saving.value || !ctx.value) return false
  saving.value = true
  fieldErrors.value = {}
  formError.value = null
  const body = { name: groupName.value, attendees: rows.value.map((r) => ({ name: r.name, email: r.email })) }
  try {
    if (groupId.value) await api.put(`/groups/${groupId.value}/roster`, body)
    else {
      const res = await api.post<{ id: number }>('/groups', { ...body, sessionId: ctx.value.session.id })
      groupId.value = res.id
      router.replace(`/groups/${res.id}/roster`)
    }
    saved.value = await api.get<GroupDetail>(`/groups/${groupId.value}`)
    // Keep what the server kept: blank rows are dropped.
    rows.value = saved.value.attendees.map((a) => row(a.name, a.email ?? ''))
    groupName.value = saved.value.name
    await Promise.resolve()
    dirty.value = false
    return true
  } catch (e) {
    if (e instanceof ApiError && e.status === 400) {
      fieldErrors.value = e.errors
      formError.value = e.errors.attendees?.[0] ?? 'Fix the highlighted rows, then try again.'
    } else if (e instanceof ApiError) formError.value = e.message
    else formError.value = 'We couldn’t save the roster. Check your connection and try again.'
    return false
  } finally {
    saving.value = false
  }
}
async function saveDraft() {
  if (await save()) toast.success('Draft saved. You can come back to it from Your groups.')
}
async function toPayment() {
  if (await save()) {
    step.value = 'pay'
    window.scrollTo({ top: 0 })
  }
}

// ── Pay ──────────────────────────────────────────────────────────────────
const card = ref<CardInput>({ number: '', expiry: '', cvc: '', zip: '' })
const processing = ref(false)
const decline = ref<string | null>(null)
const payErrors = ref<string[]>([])
let idempotencyKey = crypto.randomUUID()

const cardComplete = computed(
  () =>
    card.value.number.replace(/\D/g, '').length >= 15 &&
    /^\d{2}\s*\/\s*\d{2}$/.test(card.value.expiry) &&
    card.value.cvc.length >= 3 &&
    card.value.zip.length >= 5,
)

async function pay() {
  if (processing.value || !saved.value) return
  processing.value = true
  decline.value = null
  payErrors.value = []
  try {
    const { token } = await api.post<{ token: string }>('/fiserv-sandbox/tokenize', { cardNumber: card.value.number })
    await api.post(`/groups/${saved.value.id}/checkout`, { idempotencyKey, cardToken: token })
    const links = saved.value.attendees.filter((a) => a.email).length
    toast.success(`Paid. We emailed secure links to ${links} ${links === 1 ? 'attendee' : 'attendees'}.`)
    router.replace(`/groups/${saved.value.id}`)
  } catch (e) {
    if (e instanceof ApiError && e.status === 402) {
      decline.value = (e.body as { message?: string } | null)?.message ?? 'Your card was declined.'
      idempotencyKey = crypto.randomUUID() // the declined attempt is closed; the next one is new
    } else if (e instanceof ApiError && e.status === 400) {
      payErrors.value = Object.values(e.errors).flat()
      if (!payErrors.value.length) payErrors.value = [e.message]
    } else {
      toast.error('We couldn’t reach the payment service. If you try again you won’t be charged twice.')
    }
  } finally {
    processing.value = false
  }
}

const steps = ['Program', 'Attendees', 'Review & pay'] as const
const current = computed(() => (step.value === 'attendees' ? 1 : 2))
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-6 md:py-10">
    <div v-if="loadError" class="max-w-xl">
      <Alert variant="destructive">
        <TriangleAlert class="size-4" />
        <AlertTitle>Can’t open this group</AlertTitle>
        <AlertDescription>{{ loadError }}</AlertDescription>
      </Alert>
      <Button as-child variant="outline" class="mt-4"><RouterLink to="/groups">Your groups</RouterLink></Button>
    </div>

    <div v-else-if="!ctx" class="grid gap-8 lg:grid-cols-[1fr_360px]">
      <Skeleton class="h-[480px] rounded-xl" /><Skeleton class="h-72 rounded-xl" />
    </div>

    <template v-else>
      <Button v-if="step === 'attendees'" variant="ghost" size="sm" as-child class="-ml-2.5 mb-4 text-muted-foreground">
        <RouterLink :to="`/programs/${ctx.program.slug}`"><ArrowLeft />{{ ctx.program.name }}</RouterLink>
      </Button>
      <Button v-else variant="ghost" size="sm" class="-ml-2.5 mb-4 text-muted-foreground" @click="step = 'attendees'">
        <ArrowLeft />Back to roster
      </Button>

      <nav aria-label="Registration progress">
        <p class="text-sm font-medium md:hidden">Step {{ current + 1 }} of 3 · {{ steps[current] }}</p>
        <ol class="mt-2 flex gap-1 md:hidden" aria-hidden="true">
          <li
            v-for="(s, i) in steps"
            :key="s"
            :class="cn('h-1.5 flex-1 rounded-full', i <= current ? 'bg-primary' : 'bg-muted')"
          />
        </ol>
        <ol class="hidden max-w-xl items-center gap-2 md:flex">
          <li v-for="(s, i) in steps" :key="s" class="flex flex-1 items-center gap-2 last:flex-none">
            <span
              :aria-current="i === current ? 'step' : undefined"
              :class="
                cn(
                  'flex size-7 shrink-0 items-center justify-center rounded-full border text-xs font-medium',
                  i < current && 'border-primary bg-primary text-primary-foreground',
                  i === current && 'border-primary ring-2 ring-primary/20',
                )
              "
            >
              <Check v-if="i < current" class="size-4" /><template v-else>{{ i + 1 }}</template>
            </span>
            <span :class="cn('text-sm whitespace-nowrap', i === current ? 'font-medium' : 'text-muted-foreground')">{{
              s
            }}</span>
            <Separator v-if="i < steps.length - 1" :class="cn('flex-1', i < current && 'bg-primary')" />
          </li>
        </ol>
      </nav>

      <div class="mt-8 grid grid-cols-[minmax(0,1fr)] gap-8 lg:grid-cols-[1fr_360px]">
        <section class="min-w-0 space-y-6">
          <div>
            <p class="text-xs font-medium tracking-wide text-muted-foreground uppercase">
              {{ ctx.program.ministry }} {{ ctx.program.name }} · {{ ctx.session.name }}
            </p>
            <h1 class="mt-1 text-2xl font-semibold tracking-tight md:text-3xl">
              {{ step === 'attendees' ? 'Attendee roster' : 'Review and pay' }}
            </h1>
            <p class="mt-1 max-w-prose text-muted-foreground">
              <template v-if="step === 'attendees'"
                >Add the people from your group who will attend. You only need a name and email for each person; you can
                save a row without an email and add it later.</template
              >
              <template v-else
                >You pay for the whole group now. Each attendee then gets a secure link to complete their own
                forms.</template
              >
            </p>
          </div>

          <template v-if="step === 'attendees'">
            <Alert>
              <Info class="size-4" />
              <AlertTitle>Each attendee completes their own forms by link</AlertTitle>
              <AlertDescription>
                After you pay, we email every attendee a secure link to their waivers and questions. No account needed.
                You'll see who's done on your tracker, but you can't fill in forms for them.
              </AlertDescription>
            </Alert>

            <Alert v-if="otherGroups.length">
              <Info class="size-4" />
              <AlertTitle>You already have a group in this session</AlertTitle>
              <AlertDescription>
                <span>
                  <template v-for="(g, i) in otherGroups" :key="g.id"
                    >{{ i ? ', ' : ''
                    }}<RouterLink
                      class="font-medium text-foreground underline underline-offset-4"
                      :to="g.status === 'Draft' ? `/groups/${g.id}/roster` : `/groups/${g.id}`"
                      >{{ g.name }}</RouterLink
                    >
                    ({{ g.attendees }})</template
                  >. Start a new roster below only for a separate group.
                </span>
              </AlertDescription>
            </Alert>

            <div class="max-w-md space-y-2">
              <Label for="group-name">Group name</Label>
              <Input id="group-name" v-model="groupName" maxlength="120" :placeholder="`${ctx.leaderName}'s group`" />
            </div>

            <div class="flex flex-wrap items-center gap-2">
              <Button @click="addRow"><Plus />Add attendee</Button>
              <Button variant="outline" @click="pasteOpen = true"><ClipboardPaste />Paste list</Button>
              <Button variant="outline" @click="fileInput?.$el.click()"><Upload />Upload CSV</Button>
              <Input
                ref="fileInput"
                type="file"
                accept=".csv,.txt,text/csv,text/plain"
                class="sr-only"
                tabindex="-1"
                aria-hidden="true"
                @change="onFile"
              />
              <p class="ml-auto text-sm text-muted-foreground" aria-live="polite">
                {{ filled.length }} {{ filled.length === 1 ? 'attendee' : 'attendees'
                }}<template v-if="missingEmail"> · {{ missingEmail }} without an email</template>
              </p>
            </div>

            <div class="rounded-lg border">
              <div
                class="hidden grid-cols-[2rem_minmax(0,1fr)_minmax(0,1fr)_2.5rem] gap-3 border-b bg-muted/40 px-3 py-2 text-xs font-medium text-muted-foreground md:grid"
              >
                <span>#</span><span>Name (required)</span><span>Email</span><span class="sr-only">Remove</span>
              </div>
              <ol class="divide-y">
                <li
                  v-for="(r, i) in rows"
                  :key="r.key"
                  class="grid grid-cols-[minmax(0,1fr)_2.5rem] gap-x-3 gap-y-2 p-3 md:grid-cols-[2rem_minmax(0,1fr)_minmax(0,1fr)_2.5rem] md:items-start"
                >
                  <span class="hidden pt-2 text-sm text-muted-foreground tabular-nums md:block">{{ i + 1 }}</span>
                  <div class="space-y-1">
                    <Input
                      v-model="r.name"
                      :aria-label="`Attendee ${i + 1} name`"
                      :aria-invalid="!!errorFor(i, 'name')"
                      placeholder="Full name"
                      autocomplete="off"
                    />
                    <p v-if="errorFor(i, 'name')" class="text-xs text-destructive">{{ errorFor(i, 'name') }}</p>
                  </div>
                  <div class="col-start-1 row-start-2 space-y-1 md:col-start-auto md:row-start-auto">
                    <Input
                      v-model="r.email"
                      type="email"
                      :aria-label="`Attendee ${i + 1} email`"
                      :aria-invalid="!!errorFor(i, 'email')"
                      placeholder="name@example.com"
                      autocomplete="off"
                    />
                    <p v-if="errorFor(i, 'email')" class="text-xs text-destructive">{{ errorFor(i, 'email') }}</p>
                    <p v-else-if="r.name.trim() && !r.email.trim()" class="text-xs text-muted-foreground">
                      No email yet: they won't get a link until you add one.
                    </p>
                  </div>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="row-span-2 text-muted-foreground md:row-span-1"
                    :aria-label="`Remove ${r.name || `attendee ${i + 1}`}`"
                    @click="removeRow(r.key)"
                  >
                    <X />
                  </Button>
                </li>
              </ol>
            </div>
          </template>

          <template v-else-if="saved">
            <Card>
              <CardHeader>
                <CardTitle>{{ saved.name }}</CardTitle>
                <CardDescription
                  >{{ saved.counts.attendees }} attendees ·
                  {{ dateRange(saved.session.startDate, saved.session.endDate) }}</CardDescription
                >
              </CardHeader>
              <CardContent>
                <ul class="divide-y text-sm">
                  <li v-for="a in saved.attendees" :key="a.id" class="flex flex-wrap justify-between gap-x-4 py-2">
                    <span class="font-medium">{{ a.name }}</span>
                    <span v-if="a.email" class="text-muted-foreground">{{ a.email }}</span>
                    <span v-else class="flex items-center gap-1 text-amber-700"
                      ><MailWarning class="size-3.5" />No email yet</span
                    >
                  </li>
                </ul>
              </CardContent>
            </Card>

            <Alert v-if="saved.counts.noEmail">
              <MailWarning class="size-4" />
              <AlertTitle
                >{{ saved.counts.noEmail }} {{ saved.counts.noEmail === 1 ? 'attendee has' : 'attendees have' }} no
                email</AlertTitle
              >
              <AlertDescription
                >Their seats are included, but they won't get a link until you add an email from your
                tracker.</AlertDescription
              >
            </Alert>

            <Alert v-if="decline" variant="destructive" aria-live="polite">
              <TriangleAlert class="size-4" />
              <AlertTitle>Payment declined</AlertTitle>
              <AlertDescription
                >{{ decline }} The seats were released and you weren't charged. Your roster is saved.</AlertDescription
              >
            </Alert>
            <Alert v-if="payErrors.length" variant="destructive" aria-live="polite">
              <TriangleAlert class="size-4" />
              <AlertTitle>We couldn’t take this payment</AlertTitle>
              <AlertDescription>
                <ul class="list-disc pl-4">
                  <li v-for="e in payErrors" :key="e">{{ e }}</li>
                </ul>
              </AlertDescription>
            </Alert>

            <Card>
              <CardHeader>
                <CardTitle>Payment</CardTitle>
                <CardDescription>{{ money(saved.totalCents) }} charged today, paid in full</CardDescription>
              </CardHeader>
              <CardContent><CardFields v-model="card" :disabled="processing" /></CardContent>
            </Card>
          </template>

          <Alert v-if="formError" variant="destructive" aria-live="polite">
            <TriangleAlert class="size-4" />
            <AlertTitle>Check the roster</AlertTitle>
            <AlertDescription>{{ formError }}</AlertDescription>
          </Alert>
        </section>

        <aside class="lg:sticky lg:top-24 lg:self-start">
          <Card>
            <CardHeader>
              <CardDescription class="text-xs font-medium tracking-wide uppercase">{{
                ctx.program.name
              }}</CardDescription>
              <CardTitle class="text-xl">Registration summary</CardTitle>
              <CardDescription>{{ dateRange(ctx.session.startDate, ctx.session.endDate) }}</CardDescription>
            </CardHeader>
            <CardContent class="space-y-3 text-sm">
              <div class="flex justify-between">
                <span>Attendees</span
                ><span class="tabular-nums">{{
                  step === 'pay' && saved ? saved.counts.attendees : filled.length
                }}</span>
              </div>
              <div class="flex justify-between">
                <span>Price per attendee</span><span class="tabular-nums">{{ money(ctx.session.priceCents) }}</span>
              </div>
              <Separator />
              <div class="flex items-baseline justify-between">
                <span class="font-medium">Total</span>
                <span class="text-2xl font-semibold tabular-nums">{{
                  money(step === 'pay' && saved ? saved.totalCents : filled.length * ctx.session.priceCents)
                }}</span>
              </div>
              <p :class="cn('text-xs', overCapacity ? 'font-medium text-destructive' : 'text-muted-foreground')">
                <template v-if="overCapacity"
                  >Only {{ ctx.remaining }} spots are left in {{ ctx.session.name }}. Remove
                  {{ filled.length - ctx.remaining }} to continue.</template
                >
                <template v-else>{{ ctx.remaining }} spots left in {{ ctx.session.name }}.</template>
              </p>

              <div class="flex flex-col gap-2 pt-2 sm:flex-row lg:flex-col">
                <template v-if="step === 'attendees'">
                  <Button class="flex-1" size="lg" :disabled="saving || overCapacity" @click="toPayment">
                    <Loader2 v-if="saving" class="size-4 animate-spin" />Continue to payment
                  </Button>
                  <Button class="flex-1" variant="outline" size="lg" :disabled="saving" @click="saveDraft"
                    >Save draft</Button
                  >
                  <p v-if="dirty && groupId" class="text-center text-xs text-amber-700">Unsaved changes</p>
                </template>
                <Button v-else size="lg" class="w-full" :disabled="processing || !cardComplete || !saved" @click="pay">
                  <Loader2 v-if="processing" class="size-4 animate-spin" />
                  <ShieldCheck v-else class="size-4" />
                  {{ processing ? 'Processing…' : `Pay ${money(saved?.totalCents)}` }}
                </Button>
              </div>
            </CardContent>
          </Card>
        </aside>
      </div>
    </template>

    <Dialog v-model:open="pasteOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Paste a list of attendees</DialogTitle>
          <DialogDescription>
            One person per line: name, then email. Copying two columns from a spreadsheet works too.
          </DialogDescription>
        </DialogHeader>
        <Textarea
          v-model="pasteText"
          aria-label="Attendee list"
          rows="8"
          placeholder="Jasmine Lee, jasmine.lee@example.com&#10;Kevin Morales, kevin.morales@example.com&#10;Noah Bennett"
        />
        <DialogFooter>
          <Button variant="outline" @click="pasteOpen = false">Cancel</Button>
          <Button :disabled="!pasteText.trim()" @click="applyPaste">Add to roster</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
