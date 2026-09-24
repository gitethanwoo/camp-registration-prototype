<script setup lang="ts">
import { FileUp } from '@lucide/vue'
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import type { Upload } from './types'
import UploadPreview from './UploadPreview.vue'

// The upload side of H2: the latest upload's preview, or how to start one.
defineProps<{ upload: Upload | null; roles: string[] }>()
const emit = defineEmits<{ changed: [upload: Upload]; submitted: [] }>()
</script>

<template>
  <UploadPreview
    v-if="upload"
    :upload="upload"
    :roles="roles"
    @changed="(u) => emit('changed', u)"
    @submitted="emit('submitted')"
  />
  <Card v-else>
    <CardHeader>
      <div class="flex items-start gap-3">
        <FileUp class="mt-0.5 size-6 shrink-0 text-muted-foreground" />
        <div>
          <CardTitle>Upload a volunteer CSV</CardTitle>
          <CardDescription class="mt-1">
            One volunteer per row with first name, last name, email, phone, date of birth (MM/DD/YYYY), and role. You'll
            see every row checked before anything is sent to vetting.
          </CardDescription>
        </div>
      </div>
    </CardHeader>
  </Card>
</template>
