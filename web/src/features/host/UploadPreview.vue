<script setup lang="ts">
import { ChevronDown, CircleCheck, FileText, Info, TriangleAlert, Undo2 } from '@lucide/vue'
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, ApiError } from '@/lib/api'
import { dateTime } from '@/lib/format'
import FixRowDialog from './FixRowDialog.vue'
import type { SubmitResult, Upload, UploadRow } from './types'

// H2 upload preview. Every row of the file is listed under exactly one outcome, so nothing is
// dropped: errors wait for Fix or Skip, valid rows wait for Submit.
const props = defineProps<{ upload: Upload; roles: string[] }>()
const emit = defineEmits<{ changed: [upload: Upload]; submitted: [] }>()

const fixing = ref<UploadRow | null>(null)
const busy = ref<number | 'submit' | null>(null)

const errors = computed(() => props.upload.rows.filter((r) => r.status === 'Error'))
const skipped = computed(() => props.upload.rows.filter((r) => r.status === 'Skipped'))
const valid = computed(() => props.upload.rows.filter((r) => r.status === 'Valid'))
const pct = (n: number) => (props.upload.total ? (n / props.upload.total) * 100 : 0)
const validPct = computed(() => Math.round(pct(props.upload.valid + props.upload.submitted)))
const segments = computed(() => [
  { key: 'submitted', n: props.upload.submitted, class: 'bg-emerald-600' },
  { key: 'valid', n: props.upload.valid, class: 'bg-primary' },
  { key: 'skipped', n: props.upload.skipped, class: 'bg-muted-foreground/40' },
  { key: 'errors', n: props.upload.errors, class: 'bg-destructive' },
])
const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`
const name = (r: UploadRow) => `${r.firstName} ${r.lastName}`.trim() || 'No name'

async function act(row: UploadRow, action: 'skip' | 'restore') {
  busy.value = row.id
  try {
    emit('changed', await api.post<Upload>(`/host/uploads/${props.upload.id}/rows/${row.id}/${action}`))
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : "Couldn't update the row.")
  } finally {
    busy.value = null
  }
}

async function submit() {
  busy.value = 'submit'
  try {
    const r = await api.post<SubmitResult>(`/host/uploads/${props.upload.id}/submit`)
    emit('changed', r.upload)
    emit('submitted')
    toast.success(
      `${plural(r.submitted, 'volunteer')} sent to vetting.${r.heldBack ? ` ${plural(r.heldBack, 'row')} held back.` : ''}`,
    )
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : "Couldn't submit.")
  } finally {
    busy.value = null
  }
}
</script>

<template>
  <Card data-testid="upload-preview">
    <CardHeader>
      <div class="flex items-start gap-3">
        <FileText class="mt-0.5 size-6 shrink-0 text-muted-foreground" />
        <div class="min-w-0">
          <CardTitle>CSV upload preview</CardTitle>
          <CardDescription class="mt-1" data-testid="upload-counts">
            {{ plural(upload.total, 'row') }} · {{ upload.valid }} valid ·
            {{ plural(upload.errors, 'error') }}
            <template v-if="upload.skipped"> · {{ upload.skipped }} skipped</template>
            <template v-if="upload.submitted"> · {{ upload.submitted }} sent to vetting</template>
          </CardDescription>
          <p class="mt-1 truncate text-xs text-muted-foreground">
            {{ upload.fileName }} · uploaded {{ dateTime(upload.createdAt) }}
          </p>
        </div>
      </div>
    </CardHeader>
    <CardContent class="space-y-5">
      <div class="flex items-center gap-3">
        <div
          class="flex h-2 flex-1 overflow-hidden rounded-full bg-muted"
          role="img"
          :aria-label="`${upload.valid + upload.submitted} of ${upload.total} rows valid`"
        >
          <div v-for="s in segments" :key="s.key" :class="s.class" :style="{ width: `${pct(s.n)}%` }" />
        </div>
        <span class="shrink-0 text-sm text-muted-foreground">{{ validPct }}% valid</span>
      </div>

      <Alert v-if="errors.length" class="border-amber-300 bg-amber-50 text-amber-900">
        <TriangleAlert />
        <AlertDescription class="text-amber-900"
          >Nothing is dropped silently; {{ plural(errors.length, 'row needs', 'rows need') }} a
          decision.</AlertDescription
        >
      </Alert>

      <section v-if="errors.length" class="space-y-2">
        <div>
          <h3 class="font-semibold">Errors ({{ errors.length }})</h3>
          <p class="text-sm text-muted-foreground">Fix each row, or skip it to leave it out of this upload.</p>
        </div>
        <div class="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead class="w-10">#</TableHead>
                <TableHead>Row</TableHead>
                <TableHead class="text-right">Action</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="r in errors" :key="r.id" :data-testid="`error-row-${r.rowNumber}`">
                <TableCell class="align-top tabular-nums">{{ r.rowNumber }}</TableCell>
                <TableCell class="align-top whitespace-normal">
                  <p class="font-medium">{{ name(r) }}</p>
                  <p class="text-destructive">{{ r.issue }}</p>
                  <p class="text-xs text-muted-foreground">{{ r.detail }}</p>
                </TableCell>
                <TableCell class="align-top">
                  <div class="flex justify-end gap-2">
                    <Button size="sm" variant="outline" @click="fixing = r">Fix</Button>
                    <Button size="sm" variant="ghost" :disabled="busy === r.id" @click="act(r, 'skip')">Skip</Button>
                  </div>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </section>

      <section v-if="skipped.length" class="space-y-2">
        <h3 class="font-semibold">Skipped ({{ skipped.length }})</h3>
        <ul class="divide-y rounded-lg border text-sm">
          <li
            v-for="r in skipped"
            :key="r.id"
            class="flex items-center gap-3 px-3 py-2"
            :data-testid="`skipped-row-${r.rowNumber}`"
          >
            <span class="w-8 tabular-nums text-muted-foreground">{{ r.rowNumber }}</span>
            <span class="min-w-0 flex-1 truncate"
              >{{ name(r) }}<span v-if="r.issue" class="block text-xs text-muted-foreground">{{ r.issue }}</span></span
            >
            <Button size="sm" variant="ghost" :disabled="busy === r.id" @click="act(r, 'restore')"
              ><Undo2 />Bring back</Button
            >
          </li>
        </ul>
      </section>

      <Collapsible v-if="valid.length">
        <CollapsibleTrigger as-child>
          <Button variant="ghost" class="group -ml-3 font-semibold"
            >Ready to submit ({{ valid.length }})<ChevronDown
              class="transition-transform group-data-[state=open]:rotate-180"
          /></Button>
        </CollapsibleTrigger>
        <CollapsibleContent>
          <ul class="max-h-72 divide-y overflow-y-auto rounded-lg border text-sm">
            <li v-for="r in valid" :key="r.id" class="flex items-center gap-3 px-3 py-2">
              <span class="w-8 tabular-nums text-muted-foreground">{{ r.rowNumber }}</span>
              <span class="min-w-0 flex-1 truncate">{{ name(r) }}</span>
              <span class="hidden truncate text-muted-foreground sm:inline">{{ r.email }}</span>
              <Badge variant="outline" class="shrink-0">{{ r.role || 'Group leader' }}</Badge>
            </li>
          </ul>
        </CollapsibleContent>
      </Collapsible>

      <template v-if="upload.valid > 0">
        <Alert class="bg-muted/40">
          <Info />
          <AlertTitle>Submit {{ upload.valid }} valid to vetting</AlertTitle>
          <AlertDescription>
            <template v-if="upload.errors">{{ plural(upload.errors, 'unresolved row') }} will be held back. </template
            >Valid volunteers join your list as Not started and go to your church's vetting workflow. No WinShape staff
            ticket is created.
          </AlertDescription>
        </Alert>
        <div class="space-y-2">
          <Button class="w-full" size="lg" :disabled="busy === 'submit'" @click="submit">{{
            busy === 'submit' ? 'Submitting…' : `Submit ${upload.valid} valid to vetting`
          }}</Button>
          <p v-if="upload.errors" class="text-center text-xs text-muted-foreground">
            {{ plural(upload.errors, 'row with errors', 'rows with errors') }}
            will be held back until you decide.
          </p>
        </div>
      </template>

      <Alert v-else-if="upload.submitted && !upload.errors" class="border-emerald-200 bg-emerald-50 text-emerald-900">
        <CircleCheck />
        <AlertTitle>Upload complete</AlertTitle>
        <AlertDescription class="text-emerald-900">
          {{ plural(upload.submitted, 'volunteer') }} sent to vetting<template v-if="upload.lastSubmittedAt">
            {{ dateTime(upload.lastSubmittedAt) }}</template
          >.<template v-if="upload.skipped"> {{ plural(upload.skipped, 'row') }} skipped.</template>
          You can upload another file.
        </AlertDescription>
      </Alert>
      <p v-else-if="upload.submitted" class="text-sm text-muted-foreground" data-testid="held-back-note">
        {{ plural(upload.submitted, 'volunteer') }} sent to vetting. {{ plural(upload.errors, 'row') }} still held back:
        fix or skip {{ upload.errors === 1 ? 'it' : 'them' }} to finish this upload.
      </p>
    </CardContent>
  </Card>

  <FixRowDialog
    :upload-id="upload.id"
    :row="fixing"
    :roles="roles"
    @close="fixing = null"
    @changed="(u) => emit('changed', u)"
  />
</template>
