<script setup lang="ts">
import { CircleCheck, CircleDashed, Info, Plus, TriangleAlert } from '@lucide/vue'
import { computed, reactive, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { api } from '@/lib/api'
import { dateRange, dateTime, money } from '@/lib/format'
import NewSessionDialog from './NewSessionDialog.vue'
import PublishBadge from './PublishBadge.vue'
import type { ProgramRow } from './types'
import { saveError } from './useSetupLoad'

const props = defineProps<{ open: boolean; program: ProgramRow | null; ministries: { id: number; name: string }[] }>()
const emit = defineEmits<{ close: []; saved: [id?: number] }>()

const types = [
  { value: 'Standard', label: 'Single session', help: 'Families pick a session and pay at checkout.' },
  { value: 'Admittance', label: 'Admittance', help: 'Families apply first; staff approve before payment.' },
  { value: 'Cohort', label: 'Cohort', help: 'A group enrolls together for a fixed program.' },
]
const health = [
  { value: 'Embedded', label: 'Built-in health form' },
  { value: 'ThirdParty', label: 'Third-party form link' },
  { value: 'CampDoc', label: 'CampDoc status only' },
]

const isNew = computed(() => props.open && !props.program)
const editable = computed(() => isNew.value || props.program?.state === 'Draft')
const form = reactive({
  ministryId: '',
  name: '',
  type: 'Standard',
  healthMechanism: 'Embedded',
  location: '',
  tagline: '',
  description: '',
})
const fieldErrors = ref<Record<string, string[]>>({})
const message = ref<string | null>(null)
const busy = ref(false)
const tab = ref('details')

watch(
  () => [props.open, props.program?.id] as const,
  () => {
    const p = props.program
    Object.assign(form, {
      ministryId: String(p?.ministry.id ?? props.ministries[0]?.id ?? ''),
      name: p?.name ?? '',
      type: p?.type ?? 'Standard',
      healthMechanism: p?.healthMechanism ?? 'Embedded',
      location: p?.location ?? '',
      tagline: p?.tagline ?? '',
      description: p?.description ?? '',
    })
    fieldErrors.value = {}
    message.value = null
    if (!p) tab.value = 'details'
  },
  { immediate: true },
)
// The sheet can open before the ministries load; default a new program's ministry once they arrive.
watch(
  () => props.ministries,
  (list) => {
    if (!form.ministryId && list[0]) form.ministryId = String(list[0].id)
  },
)
const dirty = computed(() => {
  const p = props.program
  if (!p) return !!form.name
  return (
    form.name !== p.name ||
    form.type !== p.type ||
    form.healthMechanism !== p.healthMechanism ||
    form.location !== p.location ||
    form.tagline !== p.tagline ||
    form.description !== p.description ||
    form.ministryId !== String(p.ministry.id)
  )
})

async function run(action: () => Promise<unknown>, success: string, id?: number) {
  busy.value = true
  message.value = null
  fieldErrors.value = {}
  try {
    const res = (await action()) as { id?: number } | null
    toast.success(success)
    emit('saved', res?.id ?? id)
    return true
  } catch (e) {
    const err = saveError(e)
    message.value = err.message
    fieldErrors.value = err.fields
    return false
  } finally {
    busy.value = false
  }
}

function save() {
  const body = { ...form, ministryId: Number(form.ministryId) }
  const p = props.program
  return p
    ? run(() => api.put(`/admin/setup/programs/${p.id}`, body), `${form.name} saved.`, p.id)
    : run(() => api.post('/admin/setup/programs', body), `${form.name} created as a draft. Add a session next.`)
}
function submit() {
  const p = props.program
  if (!p) return
  return run(
    () => api.post(`/admin/setup/programs/${p.id}/submit`),
    `${p.name} sent to the ${p.steps[0]?.role.toLowerCase()} for approval.`,
    p.id,
  )
}
function approve() {
  const p = props.program
  if (!p) return
  const last = p.steps.filter((s) => !s.approvedAt).length === 1
  return run(
    () => api.post(`/admin/setup/programs/${p.id}/approve`),
    last ? `${p.name} is published. Families can register now.` : `Approved as ${p.nextStep}.`,
    p.id,
  )
}

const returning = ref(false)
const returnNote = ref('')
async function sendBack() {
  const p = props.program
  if (!p) return
  if (
    await run(
      () => api.post(`/admin/setup/programs/${p.id}/return`, { note: returnNote.value }),
      `${p.name} is back in draft.`,
      p.id,
    )
  ) {
    returning.value = false
    returnNote.value = ''
  }
}
// Polish: a program can't be submitted or published without a waiver, so a draft can take the standard release.
function addWaiver() {
  const p = props.program
  if (!p) return
  return run(() => api.post(`/admin/setup/programs/${p.id}/waivers`), `Standard release added to ${p.name}.`, p.id)
}
const addingSession = ref(false)
const err = (k: string) => fieldErrors.value[k]?.[0]
</script>

<template>
  <Sheet :open="open" @update:open="(v) => !v && emit('close')">
    <SheetContent class="w-full overflow-y-auto sm:max-w-xl">
      <SheetHeader>
        <SheetTitle class="text-xl">{{ program?.name ?? 'New program' }}</SheetTitle>
        <SheetDescription class="flex flex-wrap items-center gap-2">
          <template v-if="program">
            <PublishBadge :state="program.state" :label="program.stateLabel" />
            <span>{{ program.ministry.name }}</span>
          </template>
          <template v-else>New programs start as drafts. Families can't see a draft.</template>
        </SheetDescription>
      </SheetHeader>

      <Tabs v-model="tab" class="px-4">
        <TabsList v-if="program" class="w-full">
          <TabsTrigger value="details">Details</TabsTrigger>
          <TabsTrigger value="approval">Approval</TabsTrigger>
          <TabsTrigger value="sessions">Sessions</TabsTrigger>
        </TabsList>

        <TabsContent value="details" class="space-y-4 pt-2">
          <Alert v-if="program && !editable">
            <Info />
            <AlertDescription
              >{{ program.stateLabel }} programs can't be edited, so approvers approve exactly what they read. Return it
              to draft to make changes.</AlertDescription
            >
          </Alert>
          <form id="program-form" class="grid gap-4 sm:grid-cols-2" @submit.prevent="save">
            <div class="space-y-2 sm:col-span-2">
              <Label for="p-name">Program name</Label>
              <Input id="p-name" v-model="form.name" :disabled="!editable" :aria-invalid="!!err('name') || undefined" />
              <p v-if="err('name')" class="text-sm text-destructive">{{ err('name') }}</p>
            </div>
            <div class="space-y-2">
              <Label for="p-ministry">Ministry</Label>
              <Select v-model="form.ministryId" :disabled="!editable">
                <SelectTrigger id="p-ministry" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="m in ministries" :key="m.id" :value="String(m.id)">{{ m.name }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="space-y-2">
              <Label for="p-type">Program type</Label>
              <Select v-model="form.type" :disabled="!editable">
                <SelectTrigger id="p-type" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="t in types" :key="t.value" :value="t.value">{{ t.label }}</SelectItem>
                </SelectContent>
              </Select>
              <p class="text-xs text-muted-foreground">{{ types.find((t) => t.value === form.type)?.help }}</p>
              <p v-if="err('type')" class="text-sm text-destructive">{{ err('type') }}</p>
            </div>
            <div class="space-y-2">
              <Label for="p-health">Health information</Label>
              <Select v-model="form.healthMechanism" :disabled="!editable">
                <SelectTrigger id="p-health" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="h in health" :key="h.value" :value="h.value">{{ h.label }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="space-y-2">
              <Label for="p-location">Location</Label>
              <Input
                id="p-location"
                v-model="form.location"
                :disabled="!editable"
                :aria-invalid="!!err('location') || undefined"
              />
              <p v-if="err('location')" class="text-sm text-destructive">{{ err('location') }}</p>
            </div>
            <div class="space-y-2 sm:col-span-2">
              <Label for="p-tagline">Tagline <span class="font-normal text-muted-foreground">(optional)</span></Label>
              <Input id="p-tagline" v-model="form.tagline" :disabled="!editable" maxlength="160" />
            </div>
            <div class="space-y-2 sm:col-span-2">
              <Label for="p-description"
                >Description <span class="font-normal text-muted-foreground">(optional)</span></Label
              >
              <Textarea id="p-description" v-model="form.description" rows="4" :disabled="!editable" maxlength="2000" />
            </div>
          </form>
          <div v-if="program" class="rounded-md border p-3 text-sm">
            <p class="font-medium">Forms and waivers</p>
            <p class="text-muted-foreground">
              {{ program.questions }} registration questions ·
              {{
                program.waivers.length
                  ? program.waivers.map((w) => `${w.title} v${w.version}`).join(', ')
                  : 'no waivers'
              }}
            </p>
            <p v-if="!program.waivers.length" class="mt-1 text-amber-800">
              Families sign a waiver at checkout, so a program can't be published without one.
            </p>
            <p v-if="err('waivers')" class="mt-1 text-destructive">{{ err('waivers') }}</p>
            <div class="mt-2 flex flex-wrap items-center gap-3">
              <Button
                v-if="editable && !program.waivers.length"
                size="sm"
                variant="outline"
                :disabled="busy"
                @click="addWaiver"
                ><Plus />Add the standard release</Button
              >
              <RouterLink to="/admin/setup/waivers" class="text-primary underline-offset-4 hover:underline"
                >Manage waiver versions</RouterLink
              >
            </div>
          </div>
        </TabsContent>

        <TabsContent v-if="program" value="approval" class="space-y-4 pt-2">
          <p class="text-sm text-muted-foreground">Every step has to approve before the program is published.</p>
          <ol class="space-y-3" aria-label="Approval chain">
            <li v-for="s in program.steps" :key="s.sequence" class="flex gap-3">
              <CircleCheck v-if="s.approvedAt" class="mt-0.5 size-5 shrink-0 text-emerald-600" aria-hidden="true" />
              <CircleDashed v-else class="mt-0.5 size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
              <div class="text-sm">
                <p class="font-medium">
                  {{ s.sequence }}. {{ s.role }} <span v-if="s.next" class="font-normal text-amber-700">· waiting</span>
                </p>
                <p class="text-muted-foreground">{{ s.description }}</p>
                <p v-if="s.approvedAt" class="text-muted-foreground">
                  Approved by {{ s.approvedBy }}, {{ dateTime(s.approvedAt) }}
                </p>
              </div>
            </li>
          </ol>
          <p v-if="program.submittedBy" class="text-sm text-muted-foreground">
            Submitted by {{ program.submittedBy
            }}<template v-if="program.submittedAt">, {{ dateTime(program.submittedAt) }}</template>
          </p>
          <Alert v-if="program.state === 'Draft' && program.returnNote">
            <Info />
            <AlertTitle class="line-clamp-none">Returned to draft</AlertTitle>
            <AlertDescription>{{ program.returnNote }}</AlertDescription>
          </Alert>
          <Alert v-if="program.state !== 'Published'" class="border-amber-300 bg-amber-50 text-amber-900">
            <TriangleAlert />
            <AlertTitle class="line-clamp-none">Not visible to families yet</AlertTitle>
            <AlertDescription class="text-amber-900">
              {{
                program.state === 'Draft'
                  ? 'Submit it for approval when the details and sessions are ready.'
                  : `Waiting on the ${program.nextStep?.toLowerCase()}. It publishes when the last step approves.`
              }}
            </AlertDescription>
          </Alert>
          <Alert v-else>
            <CircleCheck />
            <AlertTitle class="line-clamp-none">Published</AlertTitle>
            <AlertDescription>Families can see {{ program.name }} and register for its open sessions.</AlertDescription>
          </Alert>
          <p v-if="program.approvalBlock" class="text-sm text-muted-foreground">{{ program.approvalBlock }}</p>
        </TabsContent>

        <TabsContent v-if="program" value="sessions" class="space-y-3 pt-2">
          <p v-if="!program.sessions.length" class="text-sm text-muted-foreground">
            No sessions yet. A program needs one before it can be submitted.
          </p>
          <RouterLink
            v-for="s in program.sessions"
            :key="s.id"
            :to="`/admin/setup/sessions/${s.id}`"
            class="block rounded-md border p-3 text-sm hover:bg-muted/50"
          >
            <span class="font-medium">{{ s.name }}</span>
            <span class="block text-muted-foreground">
              {{ dateRange(s.startDate, s.endDate) }} · {{ money(s.priceCents) }} · {{ s.registered }} of
              {{ s.capacity }} registered
            </span>
          </RouterLink>
          <Button v-if="editable" variant="outline" @click="addingSession = true"><Plus />Add session</Button>
        </TabsContent>
      </Tabs>

      <p v-if="message" class="px-4 text-sm text-destructive" role="alert">{{ message }}</p>
      <SheetFooter class="flex-row flex-wrap gap-2">
        <template v-if="editable">
          <Button type="submit" form="program-form" :disabled="busy || !dirty">{{
            program ? 'Save changes' : 'Create draft'
          }}</Button>
          <Button v-if="program" variant="outline" :disabled="busy || dirty" @click="submit"
            >Submit for approval</Button
          >
        </template>
        <template v-else-if="program">
          <Button v-if="program.canApprove" :disabled="busy" @click="approve"
            >Approve as {{ program.nextStep?.toLowerCase() }}</Button
          >
          <Button variant="outline" :disabled="busy" @click="returning = true">
            {{ program.state === 'Published' ? 'Unpublish and edit' : 'Return to draft' }}
          </Button>
        </template>
      </SheetFooter>
    </SheetContent>
  </Sheet>

  <Dialog v-model:open="returning">
    <DialogContent v-if="program" class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>{{
          program.state === 'Published' ? `Unpublish ${program.name}?` : `Return ${program.name} to draft?`
        }}</DialogTitle>
        <DialogDescription>
          {{
            program.state === 'Published'
              ? 'Families stop seeing it right away. Existing registrations are kept. It needs the full approval chain again to publish.'
              : 'Approvals so far are cleared. The note goes to whoever submitted it.'
          }}
        </DialogDescription>
      </DialogHeader>
      <div class="space-y-2">
        <Label for="return-note">What needs to change</Label>
        <Textarea id="return-note" v-model="returnNote" rows="3" maxlength="500" />
      </div>
      <p v-if="message" class="text-sm text-destructive" role="alert">{{ message }}</p>
      <DialogFooter>
        <Button variant="outline" :disabled="busy" @click="returning = false">Cancel</Button>
        <Button :disabled="busy || !returnNote.trim()" @click="sendBack">Return to draft</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>

  <NewSessionDialog
    v-if="program"
    v-model:open="addingSession"
    :program-id="program.id"
    :program-name="program.name"
    @saved="emit('saved', program.id)"
  />
</template>
