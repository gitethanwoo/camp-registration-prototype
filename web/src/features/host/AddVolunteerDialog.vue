<script setup lang="ts">
import { AlertTriangle, Plus } from '@lucide/vue'
import { ref } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { api, ApiError } from '@/lib/api'
import type { VolunteerFields } from './types'
import VolunteerForm from './VolunteerForm.vue'

defineProps<{ roles: string[] }>()
const emit = defineEmits<{ added: [] }>()

const blank = (): VolunteerFields => ({
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  dateOfBirth: '',
  role: 'Group leader',
})
const open = ref(false)
const fields = ref(blank())
const error = ref<string | null>(null)
const invalid = ref<keyof VolunteerFields | null>(null)
const saving = ref(false)

function reset(v: boolean) {
  open.value = v
  if (v) {
    fields.value = blank()
    error.value = null
    invalid.value = null
  }
}

async function save() {
  saving.value = true
  try {
    await api.post('/host/volunteers', fields.value)
    toast.success(`${fields.value.firstName} ${fields.value.lastName} added and sent to vetting.`)
    open.value = false
    emit('added')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Couldn't add the volunteer."
    invalid.value = e instanceof ApiError ? ((Object.keys(e.errors)[0] as keyof VolunteerFields) ?? null) : null
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog :open="open" @update:open="reset">
    <DialogTrigger as-child>
      <Button><Plus />Add volunteer</Button>
    </DialogTrigger>
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>Add a volunteer</DialogTitle>
        <DialogDescription
          >Same checks as the CSV. They go to vetting as Not started as soon as you add them.</DialogDescription
        >
      </DialogHeader>
      <form id="add-volunteer" class="space-y-4" @submit.prevent="save">
        <VolunteerForm v-model="fields" :roles="roles" :invalid="invalid" id-prefix="add" />
        <Alert v-if="error" variant="destructive">
          <AlertTriangle />
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>
      </form>
      <DialogFooter>
        <Button variant="outline" @click="open = false">Cancel</Button>
        <Button type="submit" form="add-volunteer" :disabled="saving">{{
          saving ? 'Adding…' : 'Add volunteer'
        }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
