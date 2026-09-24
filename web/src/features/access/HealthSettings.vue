<script setup lang="ts">
import { ExternalLink, Info, ShieldCheck, TriangleAlert } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Separator } from '@/components/ui/separator'
import { api } from '@/lib/api'
import { dateTime } from '@/lib/format'
import AdminOnly from '@/features/setup/AdminOnly.vue'
import { saveError, useSetupLoad } from '@/features/setup/useSetupLoad'
import { mechanismLabel, mechanisms, type HealthProgram, type HealthSettings, type Mechanism } from './types'

// K9 · Health collection settings (FR-22, FR-112). Per program: how health forms are collected, and which
// console roles may read the details. Staff also need health-data access on Setup › Users.
const { data, loading, forbidden, error, load } = useSetupLoad<HealthSettings>(() => '/access/health')
const selectedId = ref<number | null>(null)
const selected = computed(() => data.value?.programs.find((p) => p.id === selectedId.value) ?? null)

onMounted(async () => {
  await load()
  selectedId.value ??= data.value?.programs[0]?.id ?? null
})

const form = reactive({ mechanism: 'Embedded' as Mechanism, roles: [] as string[], url: '', confirm: false })
const message = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})
const busy = ref(false)

function reset(p: HealthProgram | null) {
  if (!p) return
  Object.assign(form, {
    mechanism: p.mechanism,
    roles: [...p.viewerRoles],
    url: p.thirdPartyFormUrl ?? '',
    confirm: false,
  })
  message.value = null
  fieldErrors.value = {}
}
watch(selected, reset, { immediate: true })

const dirty = computed(() => {
  const p = selected.value
  if (!p) return false
  return (
    form.mechanism !== p.mechanism ||
    form.roles.toSorted().join() !== p.viewerRoles.toSorted().join() ||
    (form.mechanism === 'ThirdParty' && form.url !== (p.thirdPartyFormUrl ?? ''))
  )
})
// CampDoc belongs to Overnight Camp (FR-22). Any other pairing needs a deliberate confirm.
const mismatch = computed(() => {
  const p = selected.value
  if (!p || form.mechanism === p.mechanism) return null
  if (form.mechanism === 'CampDoc' && !p.isOvernight)
    return {
      title: 'CampDoc is used by Overnight Camp only.',
      body: `${p.name} families would get a CampDoc link instead of a form here, and staff would see completion status only.`,
    }
  if (form.mechanism !== 'CampDoc' && p.isOvernight)
    return {
      title: `${p.name} keeps health profiles in CampDoc.`,
      body: 'Collecting them here instead means camp nurses would need to look in two places.',
    }
  return null
})
watch(
  () => form.mechanism,
  () => (form.confirm = false),
)

function toggleRole(slug: string, on: boolean | 'indeterminate') {
  form.roles = on === true ? [...new Set([...form.roles, slug])] : form.roles.filter((r) => r !== slug)
}

async function save() {
  const p = selected.value
  if (!p) return
  busy.value = true
  message.value = null
  fieldErrors.value = {}
  try {
    await api.put(`/access/health/${p.id}`, {
      mechanism: form.mechanism,
      viewerRoles: form.mechanism === 'CampDoc' ? [] : form.roles,
      thirdPartyFormUrl: form.url || null,
      confirmMismatch: form.confirm,
    })
    toast.success(`Health settings for ${p.name} saved.`)
    await load()
    reset(selected.value)
  } catch (e) {
    const err = saveError(e)
    message.value = err.message
    fieldErrors.value = err.fields
  } finally {
    busy.value = false
  }
}
const err = (k: string) => fieldErrors.value[k]?.[0]

const editor = ref<HTMLElement | null>(null)
async function pick(p: HealthProgram) {
  selectedId.value = p.id
  await nextTick()
  if (window.matchMedia('(max-width: 1023px)').matches) editor.value?.scrollIntoView({ block: 'start' })
}

// The selected program's row stays highlighted while its settings are open.
const picked = (p: HealthProgram) => (p.id === selectedId.value ? 'bg-muted' : '')
const columns: ColumnDef<HealthProgram>[] = [
  { accessorKey: 'name', header: 'Program', meta: { class: 'whitespace-normal', cellClass: picked } },
  { accessorKey: 'location', header: 'Location', meta: { class: 'hidden 2xl:table-cell', cellClass: picked } },
  { id: 'mechanism', header: 'Collection method', meta: { class: 'hidden sm:table-cell', cellClass: picked } },
  { id: 'viewers', header: 'Can view details', meta: { class: 'whitespace-normal', cellClass: picked } },
]
const roleLabel = (slug: string) => data.value?.roles.find((r) => r.slug === slug)?.label ?? slug
</script>

<template>
  <AdminOnly v-if="forbidden" page="Health collection settings" />
  <div v-else class="mx-auto max-w-7xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Health collection settings</h1>
      <p class="text-muted-foreground">
        Choose how each program collects health forms, and which staff roles can read what families wrote.
      </p>
    </div>

    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >

    <div v-else class="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,28rem)] lg:items-start">
      <Card class="min-w-0">
        <CardHeader>
          <CardTitle>Programs</CardTitle>
          <CardDescription
            >Select a program to change its health settings. They apply to every session.</CardDescription
          >
        </CardHeader>
        <CardContent>
          <DataTable
            :columns="columns"
            :data="data?.programs ?? null"
            :loading="loading && !data"
            :get-row-id="(p) => String(p.id)"
            :on-row-click="pick"
            empty-text="No programs yet. Add one under Setup › Programs."
          >
            <template #cell-name="{ row: p }">
              <div class="font-medium">{{ p.name }}</div>
              <div class="text-sm text-muted-foreground">
                {{ p.ministry.name }} · {{ p.sessions.length }} {{ p.sessions.length === 1 ? 'session' : 'sessions' }}
              </div>
              <div class="text-sm text-muted-foreground sm:hidden">{{ mechanismLabel(p.mechanism) }}</div>
            </template>
            <template #cell-mechanism="{ row: p }">{{ mechanismLabel(p.mechanism) }}</template>
            <template #cell-viewers="{ row: p }">
              <span v-if="p.mechanism === 'CampDoc'" class="text-muted-foreground">In CampDoc</span>
              <span v-else-if="!p.viewerRoles.length" class="text-muted-foreground">No one</span>
              <span v-else>{{ p.viewerRoles.map(roleLabel).join(', ') }}</span>
            </template>
          </DataTable>
        </CardContent>
      </Card>

      <div ref="editor" class="min-w-0 scroll-mt-20">
        <Card v-if="selected">
          <CardHeader>
            <CardTitle class="text-lg">{{ selected.name }}</CardTitle>
            <CardDescription>
              {{ selected.ministry.name }}<template v-if="selected.location"> · {{ selected.location }}</template> ·
              <template v-if="selected.mechanism === 'Embedded'"
                >{{ selected.formsOnFile }} of {{ selected.registered }} health forms on file</template
              >
              <template v-else
                >{{ selected.healthComplete }} of {{ selected.registered }} complete in
                {{ selected.mechanism === 'CampDoc' ? 'CampDoc' : 'the third-party form' }}</template
              >
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form id="health-form" class="space-y-6" @submit.prevent="save">
              <fieldset class="space-y-3">
                <legend class="mb-3 text-sm font-medium">Health collection method</legend>
                <RadioGroup v-model="form.mechanism" class="gap-3">
                  <Label
                    v-for="m in mechanisms"
                    :key="m.value"
                    class="flex items-start gap-3 rounded-md border p-3 font-normal has-data-[state=checked]:border-primary"
                  >
                    <RadioGroupItem :value="m.value" class="mt-0.5" />
                    <span>
                      <span class="block font-medium">{{ m.label }}</span>
                      <span class="block text-sm text-muted-foreground">{{ m.help }}</span>
                    </span>
                  </Label>
                </RadioGroup>
                <p v-if="err('mechanism') && !mismatch" class="text-sm text-destructive">{{ err('mechanism') }}</p>
              </fieldset>

              <Alert v-if="mismatch" class="border-amber-300 bg-amber-50 text-amber-900">
                <TriangleAlert />
                <AlertTitle class="line-clamp-none">{{ mismatch.title }}</AlertTitle>
                <AlertDescription class="text-amber-900">
                  <p>{{ mismatch.body }}</p>
                  <Label class="mt-2 flex items-center gap-2 font-normal">
                    <Checkbox v-model="form.confirm" />Use {{ mechanismLabel(form.mechanism) }} for {{ selected.name }}
                    anyway
                  </Label>
                </AlertDescription>
              </Alert>

              <div v-if="form.mechanism === 'ThirdParty'" class="space-y-2">
                <Label for="h-url">Form link</Label>
                <Input
                  id="h-url"
                  v-model="form.url"
                  type="url"
                  inputmode="url"
                  placeholder="https://"
                  :aria-invalid="!!err('thirdPartyFormUrl') || undefined"
                />
                <p v-if="err('thirdPartyFormUrl')" class="text-sm text-destructive">{{ err('thirdPartyFormUrl') }}</p>
                <p v-else class="text-sm text-muted-foreground">
                  Staff see this link on each registration's health form. Send it to families yourself; the family
                  checklist doesn't show it yet.
                </p>
              </div>

              <Separator />

              <template v-if="form.mechanism === 'CampDoc'">
                <Alert>
                  <Info />
                  <AlertTitle class="line-clamp-none">No health details are stored in this platform</AlertTitle>
                  <AlertDescription>
                    <p>
                      Staff see whether each camper's CampDoc profile is complete. Camp nurses open details in CampDoc.
                    </p>
                    <Button
                      as="a"
                      href="https://app.campdoc.com/"
                      target="_blank"
                      rel="noopener"
                      variant="outline"
                      size="sm"
                      class="mt-2"
                    >
                      <ExternalLink />Open CampDoc
                    </Button>
                  </AlertDescription>
                </Alert>
              </template>
              <fieldset v-else class="space-y-3">
                <legend class="mb-1 text-sm font-medium">Roles that can view health details</legend>
                <p class="text-sm text-muted-foreground">
                  Others see completion status only. Host coordinators never see health details.
                </p>
                <Label v-for="r in data?.roles ?? []" :key="r.slug" class="flex items-center gap-3 font-normal">
                  <Checkbox
                    :model-value="form.roles.includes(r.slug)"
                    @update:model-value="(v) => toggleRole(r.slug, v)"
                  />{{ r.label }}
                </Label>
                <p v-if="err('viewerRoles')" class="text-sm text-destructive">{{ err('viewerRoles') }}</p>
              </fieldset>

              <section v-if="form.mechanism !== 'CampDoc' && !dirty" class="space-y-3 text-sm">
                <div>
                  <h3 class="flex items-center gap-2 font-medium">
                    <ShieldCheck class="size-4" />Can view details now
                  </h3>
                  <ul v-if="selected.viewers.length" class="mt-2 space-y-1">
                    <li v-for="v in selected.viewers" :key="v.id">
                      {{ v.name }} <span class="text-muted-foreground">· {{ v.role }}</span>
                    </li>
                  </ul>
                  <p v-else class="mt-1 text-muted-foreground">No one yet.</p>
                </div>
                <div v-if="selected.blocked.length">
                  <h3 class="font-medium">In an allowed role, but can't view</h3>
                  <ul class="mt-2 space-y-1">
                    <li v-for="b in selected.blocked" :key="b.id">
                      {{ b.name }} <span class="text-muted-foreground">· {{ b.reason }}</span>
                    </li>
                  </ul>
                  <p class="mt-1 text-muted-foreground">Turn on health-data access under Setup › Users.</p>
                </div>
              </section>
              <p v-else-if="dirty && form.mechanism !== 'CampDoc'" class="text-sm text-muted-foreground">
                Save to see who can view details.
              </p>

              <Alert v-if="message && !mismatch" variant="destructive"
                ><AlertDescription>{{ message }}</AlertDescription></Alert
              >
            </form>
          </CardContent>
          <CardFooter class="flex flex-wrap items-center justify-end gap-2">
            <span v-if="selected.updatedAt && !dirty" class="mr-auto text-sm text-muted-foreground">
              Last changed {{ dateTime(selected.updatedAt) }} by {{ selected.updatedBy }}
            </span>
            <Badge v-if="dirty" variant="outline" class="mr-auto">Unsaved changes</Badge>
            <Button v-if="dirty" variant="outline" @click="reset(selected)">Discard</Button>
            <Button type="submit" form="health-form" :disabled="!dirty || busy || (!!mismatch && !form.confirm)">
              {{ busy ? 'Saving…' : 'Save settings' }}
            </Button>
          </CardFooter>
        </Card>
      </div>
    </div>
  </div>
</template>
