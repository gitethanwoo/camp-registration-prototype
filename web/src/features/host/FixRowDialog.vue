<script setup lang="ts">
import { AlertTriangle } from '@lucide/vue'
import { ref, watch } from 'vue'
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
import { api, ApiError } from '@/lib/api'
import type { Upload, UploadRow, VolunteerFields } from './types'
import VolunteerForm from './VolunteerForm.vue'

// Edit one held-back row. The server rechecks the whole file, so a fix can also clear (or cause)
// an in-file duplicate elsewhere; the dialog stays open while this row still has a problem.
const props = defineProps<{
  uploadId: number
  row: UploadRow | null
  roles: string[]
}>()
const emit = defineEmits<{ close: []; changed: [upload: Upload] }>()

const fields = ref<VolunteerFields>({
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  dateOfBirth: '',
  role: '',
})
const problem = ref<{
  issue: string
  detail: string | null
  field: keyof VolunteerFields | null
} | null>(null)
const saving = ref(false)

watch(
  () => props.row,
  (r) => {
    if (!r) return
    fields.value = {
      firstName: r.firstName,
      lastName: r.lastName,
      email: r.email,
      phone: r.phone,
      dateOfBirth: r.dateOfBirth,
      role: r.role || 'Group leader',
    }
    problem.value = r.issue ? { issue: r.issue, detail: r.detail, field: r.field } : null
  },
  { immediate: true },
)

async function save() {
  if (!props.row) return
  saving.value = true
  try {
    const upload = await api.put<Upload>(`/host/uploads/${props.uploadId}/rows/${props.row.id}`, fields.value)
    emit('changed', upload)
    const now = upload.rows.find((r) => r.id === props.row?.id)
    if (now?.status === 'Error') {
      problem.value = {
        issue: now.issue ?? 'Still has a problem',
        detail: now.detail,
        field: now.field,
      }
    } else {
      toast.success(`Row ${now?.rowNumber} is valid and ready to submit.`)
      emit('close')
    }
  } catch (e) {
    problem.value = {
      issue: "Couldn't save",
      detail: e instanceof ApiError ? e.message : null,
      field: null,
    }
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog :open="!!row" @update:open="(v) => !v && emit('close')">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>Fix row {{ row?.rowNumber }}</DialogTitle>
        <DialogDescription>Correct the row here; your file isn't changed. Saving checks it again.</DialogDescription>
      </DialogHeader>
      <form id="fix-row" class="space-y-4" @submit.prevent="save">
        <Alert v-if="problem" variant="destructive">
          <AlertTriangle />
          <AlertTitle>{{ problem.issue }}</AlertTitle>
          <AlertDescription v-if="problem.detail">{{ problem.detail }}</AlertDescription>
        </Alert>
        <VolunteerForm v-model="fields" :roles="roles" :invalid="problem?.field" id-prefix="fix" />
      </form>
      <DialogFooter>
        <Button variant="outline" @click="emit('close')">Cancel</Button>
        <Button type="submit" form="fix-row" :disabled="saving">{{ saving ? 'Checking…' : 'Save row' }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
