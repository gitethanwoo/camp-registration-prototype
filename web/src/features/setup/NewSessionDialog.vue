<script setup lang="ts">
import { reactive, ref } from 'vue'
import { toast } from 'vue-sonner'
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
import { api } from '@/lib/api'
import { saveError, toCents } from './useSetupLoad'

// Adds a session with its first capacity pool to a draft program. Pricing details are set in K4.
const open = defineModel<boolean>('open', { required: true })
const props = defineProps<{ programId: number; programName: string }>()
const emit = defineEmits<{ saved: [] }>()

const form = reactive({
  name: '',
  startDate: '',
  endDate: '',
  price: '',
  deposit: '0',
  poolName: 'Everyone',
  gradeMin: '0',
  gradeMax: '12',
  capacity: '',
})
const errors = ref<Record<string, string[]>>({})
const message = ref<string | null>(null)
const busy = ref(false)

async function save() {
  busy.value = true
  errors.value = {}
  message.value = null
  try {
    await api.post(`/admin/setup/programs/${props.programId}/sessions`, {
      name: form.name,
      startDate: form.startDate || null,
      endDate: form.endDate || null,
      priceCents: toCents(form.price) || 0,
      depositCents: toCents(form.deposit) || 0,
      poolName: form.poolName,
      gender: null,
      gradeMin: Number(form.gradeMin),
      gradeMax: Number(form.gradeMax),
      capacity: Number(form.capacity) || 0,
    })
    toast.success(`${form.name} added to ${props.programName}.`)
    open.value = false
    emit('saved')
  } catch (e) {
    const err = saveError(e)
    message.value = err.message
    errors.value = err.fields
  } finally {
    busy.value = false
  }
}
const err = (k: string) => errors.value[k]?.[0]
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>Add a session to {{ programName }}</DialogTitle>
        <DialogDescription
          >Start with one capacity pool. You can add pools and set the payment plan after.</DialogDescription
        >
      </DialogHeader>
      <form id="new-session" class="grid gap-4 sm:grid-cols-2" @submit.prevent="save">
        <div class="space-y-2 sm:col-span-2">
          <Label for="ns-name">Session name</Label>
          <Input id="ns-name" v-model="form.name" placeholder="Summer 2028" />
          <p v-if="err('name')" class="text-sm text-destructive">{{ err('name') }}</p>
        </div>
        <div class="space-y-2">
          <Label for="ns-start">Starts</Label>
          <Input id="ns-start" v-model="form.startDate" type="date" />
        </div>
        <div class="space-y-2">
          <Label for="ns-end">Ends</Label>
          <Input id="ns-end" v-model="form.endDate" type="date" />
          <p v-if="err('endDate')" class="text-sm text-destructive">{{ err('endDate') }}</p>
        </div>
        <div class="space-y-2">
          <Label for="ns-price">Price per camper ($)</Label>
          <Input id="ns-price" v-model="form.price" inputmode="decimal" />
          <p v-if="err('priceCents')" class="text-sm text-destructive">{{ err('priceCents') }}</p>
        </div>
        <div class="space-y-2">
          <Label for="ns-deposit">Deposit ($)</Label>
          <Input id="ns-deposit" v-model="form.deposit" inputmode="decimal" />
          <p v-if="err('depositCents')" class="text-sm text-destructive">{{ err('depositCents') }}</p>
        </div>
        <div class="space-y-2 sm:col-span-2">
          <Label for="ns-pool">First pool</Label>
          <Input id="ns-pool" v-model="form.poolName" />
        </div>
        <div class="grid grid-cols-3 gap-3 sm:col-span-2">
          <div class="space-y-2">
            <Label for="ns-gmin">From grade</Label>
            <Input id="ns-gmin" v-model="form.gradeMin" type="number" min="0" max="12" />
          </div>
          <div class="space-y-2">
            <Label for="ns-gmax">To grade</Label>
            <Input id="ns-gmax" v-model="form.gradeMax" type="number" min="0" max="12" />
          </div>
          <div class="space-y-2">
            <Label for="ns-cap">Capacity</Label>
            <Input id="ns-cap" v-model="form.capacity" type="number" min="1" />
          </div>
        </div>
        <p class="text-xs text-muted-foreground sm:col-span-2">Grade 0 is kindergarten.</p>
      </form>
      <p v-if="message" class="text-sm text-destructive" role="alert">{{ message }}</p>
      <DialogFooter>
        <Button variant="outline" :disabled="busy" @click="open = false">Cancel</Button>
        <Button
          type="submit"
          form="new-session"
          :disabled="busy || !form.name.trim() || !form.startDate || !form.endDate"
          >Add session</Button
        >
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
