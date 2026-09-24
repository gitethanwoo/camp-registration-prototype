<script setup lang="ts">
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { VolunteerFields } from './types'

// The six volunteer fields, shared by "Add volunteer" and "Fix row". `invalid` marks the field
// the server's check points at, so the host sees where the fix goes.
const fields = defineModel<VolunteerFields>({ required: true })
defineProps<{
  roles: string[]
  invalid?: keyof VolunteerFields | null
  idPrefix: string
}>()
</script>

<template>
  <div class="grid gap-4 sm:grid-cols-2">
    <div class="space-y-2">
      <Label :for="`${idPrefix}-first`">First name</Label>
      <Input
        :id="`${idPrefix}-first`"
        v-model="fields.firstName"
        autocomplete="off"
        :aria-invalid="invalid === 'firstName' || undefined"
      />
    </div>
    <div class="space-y-2">
      <Label :for="`${idPrefix}-last`">Last name</Label>
      <Input
        :id="`${idPrefix}-last`"
        v-model="fields.lastName"
        autocomplete="off"
        :aria-invalid="invalid === 'lastName' || undefined"
      />
    </div>
    <div class="space-y-2 sm:col-span-2">
      <Label :for="`${idPrefix}-email`">Email</Label>
      <Input
        :id="`${idPrefix}-email`"
        v-model="fields.email"
        type="email"
        autocomplete="off"
        :aria-invalid="invalid === 'email' || undefined"
      />
    </div>
    <div class="space-y-2">
      <Label :for="`${idPrefix}-phone`">Phone <span class="text-muted-foreground">(optional)</span></Label>
      <Input
        :id="`${idPrefix}-phone`"
        v-model="fields.phone"
        type="tel"
        autocomplete="off"
        :aria-invalid="invalid === 'phone' || undefined"
      />
    </div>
    <div class="space-y-2">
      <Label :for="`${idPrefix}-dob`">Date of birth</Label>
      <Input
        :id="`${idPrefix}-dob`"
        v-model="fields.dateOfBirth"
        placeholder="MM/DD/YYYY"
        autocomplete="off"
        :aria-invalid="invalid === 'dateOfBirth' || undefined"
      />
    </div>
    <div class="space-y-2 sm:col-span-2">
      <Label :for="`${idPrefix}-role`">Role</Label>
      <Select v-model="fields.role">
        <SelectTrigger :id="`${idPrefix}-role`" class="w-full" :aria-invalid="invalid === 'role' || undefined">
          <SelectValue placeholder="Group leader" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="r in roles" :key="r" :value="r">{{ r }}</SelectItem>
        </SelectContent>
      </Select>
    </div>
  </div>
</template>
