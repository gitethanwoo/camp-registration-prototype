<script setup lang="ts">
import { ChevronRight, FileText, TriangleAlert } from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'
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
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { api } from '@/lib/api'
import { cn } from '@/lib/utils'
import { date, dateTime } from '@/lib/format'
import AdminOnly from './AdminOnly.vue'
import PublishBadge from './PublishBadge.vue'
import type { WaiverDetail, WaiverSummary } from './types'
import { saveError, useSetupLoad } from './useSetupLoad'
import WaiverHistory from './WaiverHistory.vue'

// K7 · Waiver versions (FR-73). Live text is never edited; a new version is drafted, sent for approval,
// and approved by a different admin. Families who signed earlier keep the version they signed.
const list = useSetupLoad<WaiverSummary[]>(() => '/admin/setup/waivers')
const selectedId = ref<number | null>(null)
const detail = useSetupLoad<WaiverDetail>(() => (selectedId.value ? `/admin/setup/waivers/${selectedId.value}` : null))
onMounted(async () => {
  await list.load()
  selectedId.value = list.data.value?.find((w) => w.openVersion)?.id ?? list.data.value?.[0]?.id ?? null
})
watch(selectedId, detail.load)
async function reload() {
  await Promise.all([list.load(), detail.load()])
}

const w = computed(() => detail.data.value)
const open = computed(
  () => w.value?.versions.find((v) => v.status === 'Draft' || v.status === 'PendingApproval') ?? null,
)
const live = computed(() => w.value?.versions.find((v) => v.status === 'Published') ?? null)
const body = ref('')
const note = ref('')
watch(open, (v) => {
  body.value = v?.body ?? ''
  note.value = v?.changeNote ?? ''
})
const dirty = computed(
  () => !!open.value && open.value.editable && (body.value !== open.value.body || note.value !== open.value.changeNote),
)

const busy = ref(false)
const message = ref<string | null>(null)
async function act(fn: () => Promise<unknown>, success: string) {
  busy.value = true
  message.value = null
  try {
    await fn()
    toast.success(success)
    await reload()
    return true
  } catch (e) {
    message.value = saveError(e).message
    return false
  } finally {
    busy.value = false
  }
}
async function startDraft() {
  const t = w.value
  if (!t) return
  const next = (t.versions[0]?.version ?? 0) + 1
  await act(
    () => api.post(`/admin/setup/waivers/${t.id}/drafts`),
    `Started v${next}. Live v${t.liveVersion} stays in use until the new one is approved.`,
  )
}
function saveDraft() {
  const v = open.value
  if (!v) return Promise.resolve(false)
  return act(
    () => api.put(`/admin/setup/waiver-versions/${v.id}`, { body: body.value, changeNote: note.value }),
    'Draft saved.',
  )
}
async function submit() {
  const v = open.value
  if (!v) return
  if (dirty.value && !(await saveDraft())) return
  await act(
    () => api.post(`/admin/setup/waiver-versions/${v.id}/submit`),
    `v${v.version} sent for approval. Another admin has to approve it.`,
  )
}
const confirming = ref(false)
async function approve() {
  const v = open.value
  if (!v) return
  if (
    await act(
      () => api.post(`/admin/setup/waiver-versions/${v.id}/approve`),
      `v${v.version} is live. Checkout asks for it from today.`,
    )
  )
    confirming.value = false
}
const returnNote = ref('')
const returning = ref(false)
async function sendBack() {
  const v = open.value
  if (!v) return
  if (
    await act(
      () => api.post(`/admin/setup/waiver-versions/${v.id}/return`, { note: returnNote.value }),
      'Returned to draft.',
    )
  ) {
    returning.value = false
    returnNote.value = ''
  }
}
async function discard() {
  const v = open.value
  if (v) await act(() => api.delete(`/admin/setup/waiver-versions/${v.id}`), 'Draft discarded.')
}
</script>

<template>
  <AdminOnly v-if="list.forbidden.value" page="Waiver templates" />
  <div v-else class="mx-auto max-w-7xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Waiver templates</h1>
      <p class="text-muted-foreground">
        The waivers families sign at checkout. A new version needs a second admin's approval before it goes live.
      </p>
    </div>
    <Alert v-if="list.error.value" variant="destructive"
      ><AlertDescription>{{ list.error.value }}</AlertDescription></Alert
    >

    <div class="grid gap-6 xl:grid-cols-[18rem_1fr_20rem]">
      <Card class="h-fit">
        <CardHeader
          ><CardTitle>Templates</CardTitle
          ><CardDescription>{{ list.data.value?.length ?? '…' }} templates</CardDescription></CardHeader
        >
        <CardContent class="space-y-1 p-2">
          <Skeleton v-if="!list.data.value" class="h-40" />
          <Button
            v-for="t in list.data.value"
            :key="t.id"
            variant="ghost"
            :class="
              cn(
                'h-auto w-full justify-start gap-3 p-3 text-left font-normal whitespace-normal',
                t.id === selectedId && 'bg-muted',
              )
            "
            :aria-current="t.id === selectedId || undefined"
            @click="selectedId = t.id"
          >
            <span class="min-w-0 flex-1">
              <span class="block font-medium">{{ t.title }} v{{ t.liveVersion }}</span>
              <span class="block text-muted-foreground">{{ t.program }}</span>
              <span v-if="t.openVersion" class="mt-1 block text-xs text-amber-700"
                >v{{ t.openVersion.version }} {{ t.openVersion.status.toLowerCase() }}</span
              >
            </span>
            <ChevronRight class="size-4 shrink-0 text-muted-foreground" />
          </Button>
        </CardContent>
      </Card>

      <Skeleton v-if="!w" class="h-96 rounded-xl" />
      <Card v-else class="min-w-0">
        <CardHeader>
          <div class="flex flex-wrap items-center gap-2">
            <CardTitle class="text-xl">{{ w.title }}</CardTitle>
            <PublishBadge
              v-if="open"
              :state="open.status"
              :label="`v${open.version} ${open.statusLabel.toLowerCase()}`"
            />
            <PublishBadge v-else state="Published" :label="`v${w.liveVersion} live`" />
          </div>
          <CardDescription
            >{{ w.program }} · {{ w.signer }} signs · live since {{ date(w.effectiveDate) }}</CardDescription
          >
        </CardHeader>
        <CardContent class="space-y-4">
          <Alert class="border-amber-300 bg-amber-50 text-amber-900">
            <TriangleAlert />
            <AlertTitle class="line-clamp-none">A new version doesn't change signed waivers</AlertTitle>
            <AlertDescription class="text-amber-900"
              >{{ w.signatures }} signatures stay tied to the version each family signed.</AlertDescription
            >
          </Alert>

          <template v-if="open">
            <div class="flex items-center gap-3 rounded-lg border bg-muted/30 p-3 text-sm">
              <FileText class="size-5 shrink-0 text-muted-foreground" />
              <p class="flex-1">
                <template v-if="open.editable"
                  >Editing v{{ open.version }} (draft). Live v{{ w.liveVersion }} can't be edited.</template
                >
                <template v-else
                  >v{{ open.version }} was sent for approval by {{ open.submittedBy
                  }}<template v-if="open.submittedAt">, {{ dateTime(open.submittedAt) }}</template
                  >.</template
                >
              </p>
            </div>
            <div class="space-y-2">
              <Label for="w-note">What changed</Label>
              <Input
                id="w-note"
                v-model="note"
                :disabled="!open.editable"
                maxlength="300"
                placeholder="Adds the lake and waterfront section."
              />
            </div>
            <div class="space-y-2">
              <Label for="w-body">Waiver text</Label>
              <Textarea
                id="w-body"
                v-model="body"
                :disabled="!open.editable"
                rows="16"
                class="font-serif leading-relaxed disabled:text-foreground disabled:opacity-100"
              />
            </div>
            <p v-if="!open.editable && open.approvalBlock" class="text-sm text-muted-foreground">
              {{ open.approvalBlock }}
            </p>
          </template>
          <template v-else-if="live">
            <p class="text-sm text-muted-foreground">
              Live text, v{{ live.version }}. Start a new version to change it.
            </p>
            <div
              class="max-h-[28rem] overflow-y-auto rounded-lg border p-4 font-serif text-sm leading-relaxed whitespace-pre-line"
            >
              {{ live.body }}
            </div>
          </template>

          <p v-if="message" class="text-sm text-destructive" role="alert">{{ message }}</p>
          <div class="flex flex-wrap gap-2 border-t pt-4">
            <Button v-if="!open" :disabled="busy" @click="startDraft"
              >Start v{{ (w.versions[0]?.version ?? 0) + 1 }}</Button
            >
            <template v-else-if="open.editable">
              <Button :disabled="busy" @click="submit">Send v{{ open.version }} for approval</Button>
              <Button variant="outline" :disabled="busy || !dirty" @click="saveDraft">Save draft</Button>
              <Button variant="ghost" :disabled="busy" @click="discard">Discard draft</Button>
            </template>
            <template v-else>
              <Button v-if="open.canApprove" :disabled="busy" @click="confirming = true"
                >Approve v{{ open.version }}</Button
              >
              <Button variant="outline" :disabled="busy" @click="returning = !returning">Return to draft</Button>
            </template>
          </div>
          <div v-if="returning" class="space-y-2">
            <Label for="w-return">What needs to change</Label>
            <Textarea id="w-return" v-model="returnNote" rows="2" maxlength="500" />
            <Button size="sm" :disabled="busy || !returnNote.trim()" @click="sendBack">Return to draft</Button>
          </div>
        </CardContent>
      </Card>

      <WaiverHistory v-if="w" :waiver="w" />
    </div>

    <AlertDialog v-model:open="confirming">
      <AlertDialogContent v-if="open && w">
        <AlertDialogHeader>
          <AlertDialogTitle>Make v{{ open.version }} the live waiver?</AlertDialogTitle>
          <AlertDialogDescription>
            From today, families registering for {{ w.program }} sign v{{ open.version }}. The
            {{ live?.signatures ?? 0 }} families who signed v{{ w.liveVersion }} keep v{{ w.liveVersion }}.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="busy">Cancel</AlertDialogCancel>
          <Button :disabled="busy" @click="approve">Approve and publish</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
