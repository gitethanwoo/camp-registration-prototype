<script setup lang="ts">
import { AlertTriangle, ArrowLeft, Check, CloudUpload } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
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
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import { date } from '@/lib/format'
import type { DuplicateAccount, DuplicateDetail, Holding } from './types'

// C9 · Field-by-field merge with an explicit choice for every conflict. Never one click (FR-7).
const props = defineProps<{ a: number; b: number }>()
const router = useRouter()
const d = ref<DuplicateDetail | null>(null)
const loadError = ref<string | null>(null)
const survivor = ref<'a' | 'b'>('a')
const fields = reactive<Record<string, 'a' | 'b'>>({})
const resolutions = reactive<Record<string, string>>({})
const confirming = ref(false)
const busy = ref(false)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    d.value = await api.get<DuplicateDetail>(`/admin/duplicates/${props.a}/${props.b}`)
    for (const f of d.value.fields) fields[f.key] = 'a'
  } catch (e) {
    if (!(e instanceof ApiError)) loadError.value = "Couldn't load these accounts."
    else loadError.value = e.status === 403 ? 'Only the Customer Experience team merges accounts.' : e.message
  }
})

function account(side: 'a' | 'b'): DuplicateAccount {
  if (!d.value) throw new Error('Accounts not loaded yet.')
  return d.value[side]
}
const unresolved = computed(
  () => d.value?.conflicts.filter((c) => !resolutions[c.key] || resolutions[c.key] === 'pause') ?? [],
)
const blocked = computed(() => d.value?.conflicts.some((c) => !c.options.length) ?? false)

type Row = Holding & { source: 'A' | 'B' }
const history = computed<Row[]>(() =>
  d.value
    ? [
        ...d.value.a.holdings.map((h) => ({ ...h, source: 'A' as const })),
        ...d.value.b.holdings.map((h) => ({ ...h, source: 'B' as const })),
      ]
    : [],
)
const historyColumns: ColumnDef<Row>[] = [
  { accessorKey: 'person', header: 'Person', meta: { cellClass: 'font-medium' } },
  { accessorKey: 'session', header: 'Session' },
  {
    accessorKey: 'detail',
    header: 'Details',
    meta: { class: 'hidden md:table-cell', cellClass: 'text-muted-foreground' },
  },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'source', header: 'Source', cell: ({ row }) => `Account ${row.original.source}` },
]

function review() {
  error.value = null
  if (blocked.value) {
    error.value = 'One conflict has no safe resolution. Cancel one of the registrations first.'
    return
  }
  const open = unresolved.value[0]
  if (open) {
    error.value = `Choose how to resolve ${open.person}'s ${open.session} conflict first.`
    return
  }
  confirming.value = true
}
async function merge() {
  if (!d.value) return
  busy.value = true
  try {
    const res = await api.post<{ survivorHouseholdId: number }>(`/admin/duplicates/${props.a}/${props.b}/merge`, {
      survivor: survivor.value,
      fields,
      resolutions,
    })
    toast.success('Accounts merged. Salesforce will merge the matching records.')
    router.push(`/admin/households/${res.survivorHouseholdId}`)
  } catch (e) {
    confirming.value = false
    error.value = e instanceof ApiError ? e.message : "The merge didn't go through. Nothing was changed."
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-7xl space-y-6">
    <Button variant="ghost" size="sm" as-child class="-ml-2.5 text-muted-foreground">
      <RouterLink to="/admin/duplicates"><ArrowLeft />Duplicate accounts</RouterLink>
    </Button>
    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >
    <Skeleton v-else-if="!d" class="h-96 rounded-xl" />
    <template v-else>
      <div>
        <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">Merge duplicate accounts</h1>
        <p class="text-muted-foreground">
          These two accounts appear to belong to the same family. Pick what to keep, resolve each conflict, then merge.
        </p>
      </div>

      <div class="grid gap-4 md:grid-cols-[1fr_auto_1fr]">
        <Card v-for="side in ['a', 'b'] as const" :key="side" :class="side === 'b' ? 'md:order-3' : ''">
          <CardHeader>
            <CardTitle class="flex flex-wrap items-center gap-2">
              Account {{ side.toUpperCase() }}
              <Badge
                v-if="survivor === side"
                variant="outline"
                class="border-emerald-200 bg-emerald-50 text-emerald-800"
                >Keeps its record</Badge
              >
            </CardTitle>
            <CardDescription>
              <RouterLink :to="`/admin/households/${account(side).id}`" class="hover:underline"
                >Household #{{ account(side).id }}</RouterLink
              >
              · {{ account(side).name }} · Salesforce {{ account(side).salesforceId ?? 'not linked' }}
            </CardDescription>
          </CardHeader>
          <CardContent class="space-y-1 text-sm">
            <div v-for="m in account(side).members" :key="m.id" class="flex justify-between gap-2">
              <span class="font-medium">{{ m.name }}</span>
              <span class="text-muted-foreground">{{
                m.isAdult ? (m.role ?? 'Adult') : `DOB ${date(m.dateOfBirth)}`
              }}</span>
            </div>
          </CardContent>
        </Card>
        <div class="flex flex-col justify-center rounded-lg border bg-muted/30 p-4 text-sm md:order-2 md:w-56">
          <p class="mb-2 font-medium">Match reason</p>
          <ul class="space-y-1">
            <li class="flex items-center gap-2"><Check class="size-4 text-emerald-700" />Same name</li>
            <li class="flex items-center gap-2"><Check class="size-4 text-emerald-700" />Same date of birth</li>
            <li class="flex items-center gap-2"><Check class="size-4 text-emerald-700" />Same phone number</li>
          </ul>
          <p class="mt-2 text-muted-foreground">{{ d.sharedPeople.join(', ') }}</p>
        </div>
      </div>

      <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_24rem]">
        <Card class="min-w-0">
          <CardHeader>
            <CardTitle>Field-by-field comparison</CardTitle>
            <CardDescription
              >Choose which account's value to keep. The other value stays in the merge record.</CardDescription
            >
          </CardHeader>
          <CardContent class="space-y-5">
            <div class="space-y-2">
              <Label>Account that keeps its record (and Salesforce id)</Label>
              <RadioGroup v-model="survivor" class="flex gap-6">
                <div class="flex items-center gap-2">
                  <RadioGroupItem id="survivor-a" value="a" /><Label for="survivor-a" class="font-normal"
                    >Account A</Label
                  >
                </div>
                <div class="flex items-center gap-2">
                  <RadioGroupItem id="survivor-b" value="b" /><Label for="survivor-b" class="font-normal"
                    >Account B</Label
                  >
                </div>
              </RadioGroup>
            </div>
            <div class="divide-y rounded-md border">
              <div class="hidden grid-cols-[8rem_1fr_1fr] gap-3 bg-muted/40 px-3 py-2 text-sm font-medium sm:grid">
                <span>Field</span><span>Account A</span><span>Account B</span>
              </div>
              <RadioGroup
                v-for="f in d.fields"
                :key="f.key"
                v-model="fields[f.key]"
                :aria-label="`Keep ${f.label}`"
                class="grid gap-2 px-3 py-3 sm:grid-cols-[8rem_1fr_1fr] sm:gap-3"
              >
                <span class="text-sm font-medium">{{ f.label }}</span>
                <div class="flex min-w-0 items-center gap-2">
                  <RadioGroupItem :id="`${f.key}-a`" value="a" />
                  <Label :for="`${f.key}-a`" class="min-w-0 font-normal break-all"
                    ><span class="text-muted-foreground sm:hidden">A: </span>{{ f.a || '—' }}</Label
                  >
                </div>
                <div class="flex min-w-0 items-center gap-2">
                  <RadioGroupItem :id="`${f.key}-b`" value="b" />
                  <Label :for="`${f.key}-b`" class="min-w-0 font-normal break-all"
                    ><span class="text-muted-foreground sm:hidden">B: </span>{{ f.b || '—' }}</Label
                  >
                </div>
              </RadioGroup>
            </div>
          </CardContent>
        </Card>

        <Card v-if="d.conflicts.length" class="min-w-0 border-amber-300 bg-amber-50/50">
          <CardHeader>
            <CardTitle class="flex items-center gap-2 text-amber-900"
              ><AlertTriangle class="size-5" />Registration conflict</CardTitle
            >
            <CardDescription class="text-amber-900/80"
              >Both accounts hold a spot for the same camper. Choose what happens before merging.</CardDescription
            >
          </CardHeader>
          <CardContent class="space-y-5">
            <div v-for="c in d.conflicts" :key="c.key" class="space-y-3">
              <div class="text-sm">
                <p class="font-medium">{{ c.person }} · {{ c.session }}</p>
                <p class="text-muted-foreground">{{ c.summary }}</p>
                <p class="mt-1">A: {{ c.a }}</p>
                <p>B: {{ c.b }}</p>
              </div>
              <RadioGroup v-model="resolutions[c.key]" :aria-label="`Resolve ${c.person}'s conflict`" class="gap-2">
                <Label
                  v-for="o in c.options"
                  :key="o.value"
                  :for="`${c.key}-${o.value}`"
                  class="flex cursor-pointer items-start gap-3 rounded-md border bg-background p-3 font-normal leading-snug"
                >
                  <RadioGroupItem :id="`${c.key}-${o.value}`" :value="o.value" class="mt-0.5" />
                  <span>{{ o.label }}</span>
                </Label>
                <Label
                  :for="`${c.key}-pause`"
                  class="flex cursor-pointer items-start gap-3 rounded-md border bg-background p-3 font-normal leading-snug"
                >
                  <RadioGroupItem :id="`${c.key}-pause`" value="pause" class="mt-0.5" />
                  <span>Don't merge yet. I'll resolve this with the family first.</span>
                </Label>
              </RadioGroup>
              <p v-if="!c.options.length" class="text-sm text-destructive">
                Two registrations can't be merged. Cancel one from its registration page first.
              </p>
            </div>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Combined registration history (after merge)</CardTitle>
          <CardDescription
            >Everything moves to the surviving account. Only the waitlist spot you release above is
            removed.</CardDescription
          >
        </CardHeader>
        <CardContent>
          <DataTable
            :columns="historyColumns"
            :data="history"
            :get-row-id="(h) => `${h.kind}-${h.id}`"
            empty-text="Neither account has registrations."
          >
            <template #cell-status="{ row: h }"><StatusBadge :status="h.status" /></template>
          </DataTable>
        </CardContent>
      </Card>

      <Alert>
        <CloudUpload />
        <AlertTitle class="line-clamp-none">Downstream merge to Salesforce</AlertTitle>
        <AlertDescription>
          After you confirm, Salesforce merges {{ d.salesforce.a ?? 'A' }} and {{ d.salesforce.b ?? 'B' }}. The
          surviving record keeps the fields you chose, all registrations and payments.
        </AlertDescription>
      </Alert>

      <p v-if="error" class="text-sm text-destructive" role="alert">{{ error }}</p>
      <div class="flex flex-wrap justify-between gap-3">
        <Button variant="outline" as-child><RouterLink to="/admin/duplicates">Cancel</RouterLink></Button>
        <Button variant="destructive" @click="review">Review and confirm merge</Button>
      </div>

      <AlertDialog v-model:open="confirming">
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Merge these accounts?</AlertDialogTitle>
            <AlertDialogDescription>
              Account {{ survivor === 'a' ? 'B' : 'A' }} (household #{{ survivor === 'a' ? d.b.id : d.a.id }}) is
              archived and everything moves to household #{{ survivor === 'a' ? d.a.id : d.b.id }}. Its sign-in email
              stops working. This can't be undone here.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <ul v-if="d.conflicts.length" class="list-disc space-y-1 pl-5 text-sm">
            <li v-for="c in d.conflicts" :key="c.key">
              {{ c.person }}: {{ c.options.find((o) => o.value === resolutions[c.key])?.label }}
            </li>
          </ul>
          <AlertDialogFooter>
            <AlertDialogCancel :disabled="busy">Cancel</AlertDialogCancel>
            <Button variant="destructive" :disabled="busy" @click="merge">{{
              busy ? 'Merging…' : 'Yes, merge accounts'
            }}</Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </template>
  </div>
</template>
