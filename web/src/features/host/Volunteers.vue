<script setup lang="ts">
import { AlertTriangle, Download, Upload as UploadIcon } from '@lucide/vue'
import { useMediaQuery } from '@vueuse/core'
import { onMounted, ref } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { api, ApiError } from '@/lib/api'
import AddVolunteerDialog from './AddVolunteerDialog.vue'
import type { Upload, VolunteerList } from './types'
import UploadPanel from './UploadPanel.vue'
import VolunteerTable from './VolunteerTable.vue'

// H2 · Volunteers: the church's list with vetting status, and CSV batch upload with a row-by-row
// preview (FR-88).
const list = ref<VolunteerList | null>(null)
const upload = ref<Upload | null>(null)
const error = ref<string | null>(null)
const uploading = ref(false)
const fileKey = ref(0)
const wide = useMediaQuery('(min-width: 1024px)')
const tab = ref<'list' | 'upload'>('list')

async function loadList() {
  try {
    list.value = await api.get<VolunteerList>('/host/volunteers')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Couldn't load your volunteers."
  }
}
async function loadUpload() {
  try {
    upload.value = await api.get<Upload | null>('/host/uploads/latest')
    if (upload.value && (upload.value.errors > 0 || upload.value.valid > 0)) tab.value = 'upload'
  } catch {
    upload.value = null
  }
}
onMounted(() => Promise.all([loadList(), loadUpload()]))

async function onFile(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  fileKey.value++
  if (!file) return
  uploading.value = true
  try {
    upload.value = await api.post<Upload>('/host/uploads', {
      fileName: file.name,
      content: await file.text(),
    })
    tab.value = 'upload'
    toast.success(`${file.name}: ${upload.value.total} rows read.`)
  } catch (err) {
    toast.error(err instanceof ApiError ? err.message : "Couldn't read that file.")
  } finally {
    uploading.value = false
  }
}

function onUploadChanged(u: Upload) {
  upload.value = u
}
</script>

<template>
  <div class="mx-auto max-w-7xl space-y-6">
    <div class="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">Volunteers</h1>
        <p class="mt-1 text-muted-foreground">
          Upload a CSV or add volunteers one at a time, then send them to vetting.
        </p>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button as-child :disabled="uploading" :class="uploading ? 'pointer-events-none opacity-60' : 'cursor-pointer'">
          <label>
            <UploadIcon />{{ uploading ? 'Reading…' : 'Upload CSV' }}
            <Input
              :key="fileKey"
              type="file"
              accept=".csv,text/csv"
              class="sr-only h-px w-px"
              data-testid="csv-input"
              @change="onFile"
            />
          </label>
        </Button>
        <Button variant="outline" as-child>
          <a href="/api/host/volunteers/template.csv" download="volunteer-template.csv"
            ><Download />Download template</a
          >
        </Button>
        <AddVolunteerDialog :roles="list?.roles ?? []" @added="loadList" />
      </div>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertTriangle />
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <!-- Desktop: list and preview side by side. Phone: one at a time, behind tabs. -->
    <div
      v-if="wide"
      class="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_26rem] xl:grid-cols-[minmax(0,1fr)_30rem]"
    >
      <VolunteerTable :list="list" />
      <div class="min-w-0">
        <UploadPanel :upload="upload" :roles="list?.roles ?? []" @changed="onUploadChanged" @submitted="loadList" />
      </div>
    </div>
    <Tabs v-else v-model="tab">
      <TabsList class="grid w-full grid-cols-2">
        <TabsTrigger value="list">Volunteer list ({{ list?.counts.total ?? '…' }})</TabsTrigger>
        <TabsTrigger value="upload">Upload preview ({{ upload?.total ?? 0 }})</TabsTrigger>
      </TabsList>
      <TabsContent value="list" class="min-w-0"><VolunteerTable :list="list" /></TabsContent>
      <TabsContent value="upload" class="min-w-0">
        <UploadPanel :upload="upload" :roles="list?.roles ?? []" @changed="onUploadChanged" @submitted="loadList" />
      </TabsContent>
    </Tabs>
  </div>
</template>
