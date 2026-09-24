<script setup lang="ts">
import { CircleAlert, Loader2, Plus, TriangleAlert } from '@lucide/vue'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { api } from '@/lib/api'
import { date, dateTime } from '@/lib/format'
import { cn } from '@/lib/utils'
import AdminOnly from '@/features/setup/AdminOnly.vue'
import PublishBadge from '@/features/setup/PublishBadge.vue'
import { saveError, useSetupLoad } from '@/features/setup/useSetupLoad'
import PhonePreview from './PhonePreview.vue'
import QuestionEditor from './QuestionEditor.vue'
import { keyFromLabel, typeLabels } from './rules'
import type { FormQuestion, FormQuestionType, ProgramForms, ProgramFormRow } from './types'
import VersionHistory from './VersionHistory.vue'

// K6 · Registration form builder (FR-74). The live version is never edited. An admin starts a draft,
// edits it here (saved as they go), and sends it for approval; a different admin approves it, and
// families registering from then on answer the new version.

const props = defineProps<{ programId: number | null }>()
const router = useRouter()

const programs = useSetupLoad<ProgramFormRow[]>(() => '/admin/forms')
const programId = ref<number | null>(props.programId)
const detail = useSetupLoad<ProgramForms>(() => (programId.value ? `/admin/forms/programs/${programId.value}` : null))
onMounted(async () => {
  await programs.load()
  if (programId.value) return detail.load()
  programId.value = programs.data.value?.find((p) => p.open)?.programId ?? programs.data.value?.[0]?.programId ?? null
})
watch(programId, (id) => {
  viewedId.value = null
  router.replace({ query: id ? { program: String(id) } : {} })
  detail.load()
})

const versions = computed(() => detail.data.value?.versions ?? [])
const live = computed(() => versions.value.find((v) => v.status === 'Published') ?? null)
const open = computed(() => versions.value.find((v) => v.status === 'Draft' || v.status === 'PendingApproval') ?? null)
const viewedId = ref<number | null>(null)
const viewed = computed(
  () => versions.value.find((v) => v.id === viewedId.value) ?? open.value ?? live.value ?? versions.value[0] ?? null,
)
const editable = computed(() => !!viewed.value?.editable)
const liveKeys = computed(() => new Set(live.value?.questions.map((q) => q.key) ?? []))

// ── The version on screen, edited locally and saved as the admin works ──
const questions = ref<FormQuestion[]>([])
const changeNote = ref('')
const selected = ref(0)
const fieldErrors = ref<Record<string, string[]>>({})
const saveState = ref<'saved' | 'pending' | 'saving' | 'error'>('saved')
const saveMessage = ref<string | null>(null)
const lastSaved = ref('')
const snapshot = () => JSON.stringify({ changeNote: changeNote.value, questions: questions.value })

watch(
  () => [viewed.value?.id, viewed.value?.status, viewed.value?.updatedAt],
  () => {
    const v = viewed.value
    questions.value = structuredClone(v?.questions.map((q) => ({ ...q, options: [...q.options] })) ?? [])
    changeNote.value = v?.changeNote ?? ''
    lastSaved.value = snapshot()
    fieldErrors.value = {}
    saveState.value = 'saved'
    if (selected.value >= questions.value.length) selected.value = 0
  },
  { immediate: true },
)

let timer: ReturnType<typeof setTimeout> | undefined
let inflight: Promise<void> | null = null
watch(
  [questions, changeNote],
  () => {
    if (!editable.value || snapshot() === lastSaved.value) return
    saveState.value = 'pending'
    clearTimeout(timer)
    timer = setTimeout(save, 700)
  },
  { deep: true },
)
onBeforeUnmount(() => clearTimeout(timer))

async function save(): Promise<boolean> {
  clearTimeout(timer)
  const v = viewed.value
  if (!v?.editable) return true
  if (inflight) await inflight
  const snap = snapshot()
  if (snap === lastSaved.value && saveState.value !== 'error') {
    saveState.value = 'saved'
    return true
  }
  saveState.value = 'saving'
  inflight = (async () => {
    try {
      await api.put(`/admin/forms/versions/${v.id}`, JSON.parse(snap))
      lastSaved.value = snap
      fieldErrors.value = {}
      saveMessage.value = null
      saveState.value = snapshot() === snap ? 'saved' : 'pending'
    } catch (e) {
      const err = saveError(e)
      fieldErrors.value = err.fields
      saveMessage.value = err.message
      saveState.value = 'error'
    }
  })()
  await inflight
  inflight = null
  // The request above may have changed the state, so read it fresh.
  const after: string = saveState.value
  if (after === 'pending') return save()
  return after === 'saved'
}

// ── Questions ──
const current = computed(() => questions.value[selected.value] ?? null)
const otherKeys = computed(() => new Set(questions.value.filter((_, i) => i !== selected.value).map((q) => q.key)))
const errorsFor = (i: number) => Object.keys(fieldErrors.value).some((k) => k.startsWith(`questions.${i}.`))
const describe = (q: FormQuestion) =>
  [
    typeLabels[q.type as FormQuestionType] ?? q.type,
    q.scope === 'Household' ? 'once per family' : 'each camper',
    q.required ? 'required' : 'optional',
  ].join(' · ')
const conditionText = (q: FormQuestion) => {
  if (!q.showWhenKey) return null
  const from = questions.value.find((x) => x.key === q.showWhenKey)
  return `Shown when “${from?.label ?? q.showWhenKey}” is ${q.showWhenValue ?? '…'}`
}

function addQuestion() {
  const taken = new Set(questions.value.map((q) => q.key))
  questions.value.push({
    key: keyFromLabel('New question', taken),
    label: 'New question',
    helpText: null,
    type: 'ShortText',
    scope: 'Participant',
    required: false,
    options: [],
    showWhenKey: null,
    showWhenValue: null,
  })
  selected.value = questions.value.length - 1
}
function rename(from: string, to: string) {
  for (const q of questions.value) if (q.showWhenKey === from) q.showWhenKey = to
}
function move(by: number) {
  const i = selected.value
  const j = i + by
  if (j < 0 || j >= questions.value.length) return
  const list = [...questions.value]
  const [moving] = list.splice(i, 1)
  if (moving) list.splice(j, 0, moving)
  questions.value = list
  selected.value = j
}
function remove() {
  const gone = current.value
  if (!gone) return
  questions.value = questions.value
    .filter((_, i) => i !== selected.value)
    .map((q) => (q.showWhenKey === gone.key ? { ...q, showWhenKey: null, showWhenValue: null } : q))
  selected.value = Math.max(0, selected.value - 1)
}

// ── Version actions ──
const busy = ref(false)
const actionError = ref<string | null>(null)
async function act(fn: () => Promise<unknown>, success: string, view?: () => number | null | undefined) {
  busy.value = true
  actionError.value = null
  try {
    await fn()
    toast.success(success)
    await Promise.all([programs.load(), detail.load()])
    viewedId.value = view?.() ?? null
    return true
  } catch (e) {
    const err = saveError(e)
    actionError.value = err.message
    fieldErrors.value = err.fields
    return false
  } finally {
    busy.value = false
  }
}
async function startDraft() {
  const next = (versions.value[0]?.version ?? 0) + 1
  const liveVersion = live.value?.version
  await act(
    () => api.post(`/admin/forms/programs/${programId.value}/drafts`),
    liveVersion
      ? `Started v${next}. Families keep answering v${liveVersion} until v${next} is approved.`
      : `Started v${next}.`,
    () => open.value?.id,
  )
  selected.value = 0
}
async function submit() {
  const v = viewed.value
  if (!v || !(await save())) return
  await act(
    () => api.post(`/admin/forms/versions/${v.id}/submit`),
    `v${v.version} sent for approval. A different admin has to approve it.`,
    () => v.id,
  )
}
const approving = ref(false)
async function approve() {
  const v = viewed.value
  if (!v) return
  if (
    await act(
      () => api.post(`/admin/forms/versions/${v.id}/approve`),
      `v${v.version} is live.`,
      () => v.id,
    )
  )
    approving.value = false
}
const returning = ref(false)
const returnNote = ref('')
async function sendBack() {
  const v = viewed.value
  if (!v) return
  if (
    await act(
      () => api.post(`/admin/forms/versions/${v.id}/return`, { note: returnNote.value }),
      `v${v.version} is a draft again.`,
      () => v.id,
    )
  ) {
    returning.value = false
    returnNote.value = ''
  }
}
const discarding = ref(false)
async function discard() {
  const v = viewed.value
  if (!v) return
  clearTimeout(timer)
  if (await act(() => api.delete(`/admin/forms/versions/${v.id}`), `Draft v${v.version} discarded.`))
    discarding.value = false
}

const saveLabel = computed(
  () =>
    ({
      saved: 'All changes saved',
      pending: 'Unsaved changes…',
      saving: 'Saving…',
      error: saveMessage.value ?? "Changes didn't save.",
    })[saveState.value],
)
const programRow = computed(() => programs.data.value?.find((p) => p.programId === programId.value) ?? null)
</script>

<template>
  <AdminOnly v-if="programs.forbidden.value" page="Registration forms" />
  <div v-else class="mx-auto max-w-7xl space-y-6">
    <div class="flex flex-wrap items-end justify-between gap-4">
      <div class="min-w-0">
        <h1 class="text-2xl font-semibold tracking-tight">Registration forms</h1>
        <p class="text-muted-foreground">
          The questions families answer when they register. Changes go live after a second admin approves them.
        </p>
      </div>
      <div class="w-full space-y-1.5 sm:w-72">
        <Label for="form-program">Program</Label>
        <Select v-model="programId">
          <SelectTrigger id="form-program" class="w-full"><SelectValue placeholder="Choose a program" /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="p in programs.data.value" :key="p.programId" :value="p.programId">{{
              p.program
            }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>

    <Alert v-if="programs.error.value || detail.error.value" variant="destructive">
      <CircleAlert /><AlertDescription>{{ programs.error.value ?? detail.error.value }}</AlertDescription>
    </Alert>

    <Skeleton v-if="!detail.data.value" class="h-96 rounded-xl" />
    <template v-else>
      <!-- Status strip: what families answer today, and what's in progress -->
      <div class="grid gap-4 md:grid-cols-3">
        <Card class="gap-2 py-4">
          <CardContent class="space-y-1 px-4 text-sm">
            <p class="flex items-center gap-2">
              <span class="text-lg font-semibold">{{ live ? `v${live.version}` : 'No form' }}</span>
              <PublishBadge v-if="live" state="Published" label="Live" />
            </p>
            <p v-if="live" class="text-muted-foreground">
              Live since {{ date(live.publishedAt) }} · {{ live.questions.length }} questions
            </p>
            <p v-else class="text-muted-foreground">
              Families answer the program's original {{ programRow?.legacyQuestions ?? 0 }} questions.
            </p>
          </CardContent>
        </Card>
        <Card class="gap-2 py-4">
          <CardContent class="space-y-1 px-4 text-sm">
            <template v-if="open">
              <p class="flex items-center gap-2">
                <span class="text-lg font-semibold">v{{ open.version }}</span>
                <PublishBadge :state="open.status" :label="open.statusLabel" />
              </p>
              <p class="text-muted-foreground">
                <template v-if="open.submittedAt"
                  >Sent by {{ open.submittedBy }}, {{ dateTime(open.submittedAt) }}</template
                >
                <template v-else>Last edited {{ dateTime(open.updatedAt) }} · started by {{ open.createdBy }}</template>
              </p>
            </template>
            <template v-else>
              <p class="text-lg font-semibold">No changes in progress</p>
              <p class="text-muted-foreground">Start a new version to change the questions.</p>
            </template>
          </CardContent>
        </Card>
        <Card class="gap-2 border-amber-200 bg-amber-50/60 py-4 dark:bg-amber-950/20">
          <CardContent class="flex h-full flex-col justify-center gap-3 px-4 text-sm">
            <p class="flex items-center gap-2 font-medium">
              <TriangleAlert class="size-4 text-amber-700" />Changes need approval
            </p>
            <p class="text-muted-foreground">A different admin approves each version before families see it.</p>
            <Button v-if="!open" size="sm" class="self-start" :disabled="busy" @click="startDraft"
              ><Plus />Start v{{ (versions[0]?.version ?? 0) + 1 }}</Button
            >
            <Button
              v-else-if="open.id !== viewed?.id"
              size="sm"
              variant="outline"
              class="self-start"
              @click="viewedId = open.id"
              >Open v{{ open.version }}</Button
            >
          </CardContent>
        </Card>
      </div>

      <Alert v-if="actionError" variant="destructive"
        ><CircleAlert /><AlertDescription>{{ actionError }}</AlertDescription></Alert
      >

      <!-- Waiting for approval -->
      <Alert v-if="viewed?.status === 'PendingApproval'" class="border-amber-200 bg-amber-50/60 dark:bg-amber-950/20">
        <TriangleAlert />
        <AlertTitle class="line-clamp-none">v{{ viewed.version }} is waiting for approval</AlertTitle>
        <AlertDescription class="space-y-3">
          <p>“{{ viewed.changeNote }}” Sent by {{ viewed.submittedBy }}, {{ dateTime(viewed.submittedAt!) }}.</p>
          <p v-if="viewed.approvalBlock">{{ viewed.approvalBlock }}</p>
          <div class="flex flex-wrap gap-2">
            <Button v-if="viewed.canApprove" size="sm" :disabled="busy" @click="approving = true"
              >Approve and publish</Button
            >
            <Button size="sm" variant="outline" :disabled="busy" @click="returning = true">Return to draft</Button>
          </div>
        </AlertDescription>
      </Alert>
      <Alert v-else-if="viewed?.status === 'Draft' && viewed.returnNote">
        <CircleAlert />
        <AlertTitle class="line-clamp-none">Returned for changes</AlertTitle>
        <AlertDescription>{{ viewed.returnNote }}</AlertDescription>
      </Alert>

      <Tabs default-value="questions">
        <TabsList>
          <TabsTrigger value="questions">Questions</TabsTrigger>
          <TabsTrigger value="history">Version history</TabsTrigger>
        </TabsList>

        <TabsContent value="questions" class="mt-4 space-y-6">
          <div class="grid items-start gap-6 lg:grid-cols-2 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_21rem]">
            <Card>
              <CardHeader>
                <CardTitle>v{{ viewed?.version }} questions</CardTitle>
                <CardDescription>
                  <template v-if="editable">Draft. Pick a question to change it.</template>
                  <template v-else-if="viewed?.status === 'Published'"
                    >Live. Start a new version to change it.</template
                  >
                  <template v-else>{{ viewed?.statusLabel }}. Read only.</template>
                </CardDescription>
              </CardHeader>
              <CardContent class="space-y-1 p-2">
                <p v-if="!questions.length" class="p-3 text-sm text-muted-foreground">No questions yet.</p>
                <Button
                  v-for="(q, i) in questions"
                  :key="i"
                  variant="ghost"
                  :class="
                    cn(
                      'h-auto w-full justify-start p-3 text-left font-normal whitespace-normal',
                      i === selected && 'bg-muted',
                    )
                  "
                  :aria-current="i === selected || undefined"
                  @click="selected = i"
                >
                  <span class="min-w-0 flex-1">
                    <span class="block font-medium">{{ q.label || 'Untitled question' }}</span>
                    <span class="block text-muted-foreground">{{ describe(q) }}</span>
                    <span v-if="conditionText(q)" class="block text-muted-foreground">{{ conditionText(q) }}</span>
                  </span>
                  <CircleAlert v-if="errorsFor(i)" class="text-destructive" aria-label="Has a problem" />
                </Button>
                <p v-if="fieldErrors.questions" class="px-3 text-sm text-destructive">{{ fieldErrors.questions[0] }}</p>
                <div v-if="editable" class="p-2">
                  <Button variant="outline" class="w-full border-dashed" @click="addQuestion"
                    ><Plus />Add question</Button
                  >
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Question details</CardTitle>
                <CardDescription v-if="current">{{ current.label || 'Untitled question' }}</CardDescription>
              </CardHeader>
              <CardContent>
                <QuestionEditor
                  v-if="current"
                  :key="selected"
                  v-model="questions[selected]!"
                  :earlier="questions.slice(0, selected)"
                  :other-keys="otherKeys"
                  :live-keys="liveKeys"
                  :index="selected"
                  :count="questions.length"
                  :readonly="!editable"
                  :errors="fieldErrors"
                  @rename="rename"
                  @move="move"
                  @remove="remove"
                />
                <p v-else class="text-sm text-muted-foreground">Add a question to see its details.</p>
              </CardContent>
            </Card>

            <Card class="lg:col-span-2 xl:col-span-1">
              <CardHeader>
                <CardTitle>Phone preview</CardTitle>
                <CardDescription>What families see. Answer to try the follow-up questions.</CardDescription>
              </CardHeader>
              <CardContent>
                <PhonePreview
                  :questions="questions"
                  :program="detail.data.value.program.name"
                  :session="detail.data.value.program.session?.name ?? null"
                />
              </CardContent>
            </Card>
          </div>

          <!-- Sending the draft -->
          <Card v-if="editable">
            <CardHeader>
              <CardTitle>Send v{{ viewed?.version }} for approval</CardTitle>
              <CardDescription>Say what changed so the approving admin knows what to check.</CardDescription>
            </CardHeader>
            <CardContent class="space-y-4">
              <div class="space-y-2">
                <Label for="form-change-note">What changed</Label>
                <Textarea
                  id="form-change-note"
                  v-model="changeNote"
                  rows="2"
                  maxlength="300"
                  :aria-invalid="!!fieldErrors.changeNote || undefined"
                />
                <p v-if="fieldErrors.changeNote" class="text-sm text-destructive">{{ fieldErrors.changeNote[0] }}</p>
              </div>
              <div class="flex flex-wrap items-center gap-3">
                <Button :disabled="busy || saveState === 'saving'" @click="submit">Send for approval</Button>
                <Button variant="ghost" class="text-destructive" :disabled="busy" @click="discarding = true"
                  >Discard draft</Button
                >
                <p
                  :class="
                    cn(
                      'flex items-center gap-1.5 text-sm sm:ml-auto',
                      saveState === 'error' ? 'text-destructive' : 'text-muted-foreground',
                    )
                  "
                  role="status"
                >
                  <Loader2 v-if="saveState === 'saving'" class="size-4 animate-spin" />{{ saveLabel }}
                </p>
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="history" class="mt-4">
          <Card>
            <CardHeader>
              <CardTitle>Version history</CardTitle>
              <CardDescription>Registrations keep the version they answered.</CardDescription>
            </CardHeader>
            <CardContent
              ><VersionHistory :versions="versions" :viewed-id="viewed?.id ?? null" @view="viewedId = $event"
            /></CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </template>

    <AlertDialog v-model:open="approving">
      <AlertDialogContent v-if="viewed">
        <AlertDialogHeader>
          <AlertDialogTitle>Make v{{ viewed.version }} the live form?</AlertDialogTitle>
          <AlertDialogDescription>
            Families who register for {{ detail.data.value?.program.name }} from now on answer v{{ viewed.version }}.
            <template v-if="live">Registrations made on v{{ live.version }} keep their answers.</template>
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="busy">Cancel</AlertDialogCancel>
          <Button :disabled="busy" @click="approve">Approve and publish</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog v-model:open="returning">
      <AlertDialogContent v-if="viewed">
        <AlertDialogHeader>
          <AlertDialogTitle>Return v{{ viewed.version }} to draft?</AlertDialogTitle>
          <AlertDialogDescription>The author sees your note and can change the questions.</AlertDialogDescription>
        </AlertDialogHeader>
        <div class="space-y-2">
          <Label for="form-return-note">What needs to change</Label>
          <Textarea id="form-return-note" v-model="returnNote" rows="3" maxlength="500" />
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="busy">Cancel</AlertDialogCancel>
          <Button :disabled="busy || !returnNote.trim()" @click="sendBack">Return to draft</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog v-model:open="discarding">
      <AlertDialogContent v-if="viewed">
        <AlertDialogHeader>
          <AlertDialogTitle>Discard draft v{{ viewed.version }}?</AlertDialogTitle>
          <AlertDialogDescription>
            Its changes are deleted. <template v-if="live">Families keep answering v{{ live.version }}.</template>
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="busy">Keep draft</AlertDialogCancel>
          <Button variant="destructive" :disabled="busy" @click="discard">Discard draft</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
