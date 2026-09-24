<script setup lang="ts">
import { ArrowLeft, CircleAlert, CircleCheck, Info, LoaderCircle, Lock, Users } from '@lucide/vue'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { ApiError, api } from '@/lib/api'
import { dateTime } from '@/lib/format'
import type { Gender, MemberProfile, MemberRequest, Overview } from './types'

const props = defineProps<{ id: number | null; adult: boolean }>()
const router = useRouter()

const profile = ref<MemberProfile | null>(null)
const seasonYear = ref<number | null>(null)
const lastName = ref('')
const loadError = ref(false)
const form = reactive<MemberRequest>({
  firstName: '',
  lastName: '',
  dateOfBirth: null,
  gender: null,
  isAdult: props.adult,
  email: null,
  dietary: null,
  allergies: null,
  adaNeeds: null,
})
const errors = ref<Record<string, string[]>>({})
const saving = ref(false)
const savedAt = ref<string | null>(null)
const saveFailed = ref<string | null>(null)
const isNew = computed(() => props.id == null)
const locked = computed(() => !form.isAdult && (profile.value?.registeredFor.length ?? 0) > 0)
const today = new Date().toISOString().slice(0, 10)

function fill(p: MemberProfile) {
  profile.value = p
  seasonYear.value = p.seasonYear
  Object.assign(form, {
    firstName: p.firstName,
    lastName: p.lastName,
    dateOfBirth: p.dateOfBirth,
    gender: p.isAdult ? null : p.gender,
    isAdult: p.isAdult,
    email: p.email,
    dietary: p.dietary,
    allergies: p.allergies,
    adaNeeds: p.adaNeeds,
  })
}

// What the server last confirmed; autosave only runs when the form differs from it.
let saved = ''
onMounted(async () => {
  try {
    if (props.id != null) {
      fill(await api.get<MemberProfile>(`/family/members/${props.id}`))
    } else {
      const o = await api.get<Overview>('/family/overview')
      seasonYear.value = o.seasonYear
      // Children usually share the family's last name; prefill it from the primary adult.
      lastName.value = o.members.find((m) => m.isAdult)?.lastName ?? ''
      form.lastName = lastName.value
    }
  } catch {
    loadError.value = true
  }
  saved = JSON.stringify(payload())
})

/** Same rule as the API (Eligibility.GradeFor): age on Sept 1 of the season year, minus 5. */
const gradeLabel = computed(() => {
  if (form.isAdult || !form.dateOfBirth || !seasonYear.value) return null
  const [y, m, d] = form.dateOfBirth.split('-').map(Number) as [number, number, number]
  const age = seasonYear.value - y - (m > 9 || (m === 9 && d > 1) ? 1 : 0)
  const grade = age - 5
  if (grade < 0) return 'Pre-K'
  if (grade === 0) return 'Kindergarten'
  return `Grade ${grade}`
})

const payload = (): MemberRequest => ({
  ...form,
  firstName: form.firstName.trim(),
  lastName: form.lastName.trim(),
  dateOfBirth: form.dateOfBirth || null,
})

async function create() {
  errors.value = {}
  saving.value = true
  try {
    const p = await api.post<MemberProfile>('/family/members', payload())
    toast.success(
      p.gradeLabel
        ? `${p.firstName} added · ${p.gradeLabel} in fall ${p.seasonYear}`
        : `${p.firstName} added to your family`,
    )
    await router.push('/family')
  } catch (e) {
    if (e instanceof ApiError) errors.value = e.errors
    toast.error(e instanceof Error ? e.message : 'Couldn’t add this family member.')
  } finally {
    saving.value = false
  }
}

// Edit mode saves as you go (F2 "autosave"), a moment after the last keystroke.
let timer: number | undefined
let queued = false
async function save() {
  if (saving.value) {
    queued = true
    return
  }
  saving.value = true
  saveFailed.value = null
  try {
    const body = payload()
    const p = await api.put<MemberProfile>(`/family/members/${props.id}`, body)
    profile.value = p
    saved = JSON.stringify(body)
    errors.value = {}
    savedAt.value = new Date().toISOString()
  } catch (e) {
    errors.value = e instanceof ApiError ? e.errors : {}
    saveFailed.value = e instanceof Error ? e.message : 'Not saved.'
  } finally {
    saving.value = false
    if (queued) {
      queued = false
      void save()
    }
  }
}
watch(
  form,
  () => {
    if (isNew.value || !saved || JSON.stringify(payload()) === saved) return
    window.clearTimeout(timer)
    timer = window.setTimeout(save, 700)
  },
  { deep: true },
)

const gender = computed({
  get: () => form.gender ?? undefined,
  set: (v: Gender | undefined) => (form.gender = v ?? null),
})
const text = (key: 'dietary' | 'allergies' | 'adaNeeds' | 'email') =>
  computed({
    get: () => form[key] ?? '',
    set: (v: string | number) => (form[key] = String(v)),
  })
const dietary = text('dietary')
const allergies = text('allergies')
const adaNeeds = text('adaNeeds')
const email = text('email')
const dob = computed({
  get: () => form.dateOfBirth ?? '',
  set: (v: string | number) => (form.dateOfBirth = String(v) || null),
})
const title = computed(() => {
  if (!isNew.value) return `${form.firstName} ${form.lastName}`.trim()
  return form.isAdult ? 'Add an adult' : 'Add a child'
})
</script>

<template>
  <div class="mx-auto max-w-3xl px-4 py-8 md:py-12">
    <Button variant="link" as-child class="h-auto p-0 text-muted-foreground">
      <RouterLink to="/family"><ArrowLeft />My family</RouterLink>
    </Button>

    <Alert v-if="loadError" variant="destructive" class="mt-6">
      <CircleAlert />
      <AlertDescription>This family member couldn’t be loaded. They may not be in your household.</AlertDescription>
    </Alert>

    <div v-else-if="!isNew && !profile" class="mt-6 space-y-4">
      <Skeleton class="h-10 w-64" /><Skeleton class="h-64 rounded-xl" />
    </div>

    <form v-else class="mt-4" novalidate @submit.prevent="create">
      <div class="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 class="text-3xl font-semibold tracking-tight md:text-4xl">{{ title }}</h1>
          <p class="mt-1 text-muted-foreground">
            {{
              isNew
                ? 'Add them once and they’re ready for any program.'
                : form.isAdult
                  ? 'Edit this adult’s information below.'
                  : 'Edit your child’s information below.'
            }}
          </p>
        </div>
        <div v-if="!isNew" class="flex items-center gap-2 text-sm" aria-live="polite">
          <template v-if="saving"><LoaderCircle class="size-5 animate-spin text-muted-foreground" />Saving…</template>
          <template v-else-if="saveFailed"
            ><CircleAlert class="size-5 text-destructive" /><span class="text-destructive">Not saved</span></template
          >
          <template v-else-if="savedAt">
            <CircleCheck class="size-5 text-emerald-600" />
            <span
              >Saved just now<span class="block text-xs text-muted-foreground">{{ dateTime(savedAt) }}</span></span
            >
          </template>
          <span v-else class="text-xs text-muted-foreground">Changes save automatically</span>
        </div>
      </div>

      <div class="mt-8 grid gap-5 sm:grid-cols-2">
        <div class="space-y-2">
          <Label for="first">First name</Label>
          <Input
            id="first"
            v-model="form.firstName"
            autocomplete="off"
            :aria-invalid="!!errors.firstName"
            aria-describedby="first-error"
          />
          <p v-if="errors.firstName" id="first-error" class="text-sm text-destructive">{{ errors.firstName[0] }}</p>
        </div>
        <div class="space-y-2">
          <Label for="last">Last name</Label>
          <Input id="last" v-model="form.lastName" autocomplete="off" :aria-invalid="!!errors.lastName" />
          <p v-if="errors.lastName" class="text-sm text-destructive">{{ errors.lastName[0] }}</p>
        </div>

        <div class="space-y-2">
          <Label for="dob"
            >Date of birth<span v-if="form.isAdult" class="font-normal text-muted-foreground"> (optional)</span></Label
          >
          <Input
            id="dob"
            v-model="dob"
            type="date"
            :max="today"
            :disabled="locked"
            :aria-invalid="!!errors.dateOfBirth"
            aria-describedby="dob-help"
          />
          <p v-if="errors.dateOfBirth" id="dob-help" class="text-sm text-destructive">{{ errors.dateOfBirth[0] }}</p>
        </div>

        <div v-if="!form.isAdult" class="space-y-2">
          <Label for="grade">Grade in fall {{ seasonYear }}</Label>
          <Input
            id="grade"
            :model-value="gradeLabel ?? 'Enter a date of birth'"
            readonly
            class="bg-muted/40"
            aria-describedby="grade-help"
          />
          <p id="grade-help" class="text-xs text-muted-foreground">Worked out from date of birth (Sept 1 cutoff).</p>
        </div>
        <div v-else class="space-y-2">
          <Label for="email">Email <span class="font-normal text-muted-foreground">(optional)</span></Label>
          <Input
            id="email"
            v-model="email"
            type="email"
            autocomplete="email"
            :disabled="profile?.role === 'Primary'"
            :aria-invalid="!!errors.email"
          />
          <p v-if="errors.email" class="text-sm text-destructive">{{ errors.email[0] }}</p>
          <p v-else-if="profile?.role === 'Primary'" class="text-xs text-muted-foreground">
            This is the email you sign in with.
          </p>
        </div>

        <div v-if="!form.isAdult" class="space-y-2">
          <Label for="gender">Boy or girl</Label>
          <Select v-model="gender" :disabled="locked">
            <SelectTrigger id="gender" class="w-full" :aria-invalid="!!errors.gender"
              ><SelectValue placeholder="Choose…"
            /></SelectTrigger>
            <SelectContent>
              <SelectItem value="Male">Boy</SelectItem>
              <SelectItem value="Female">Girl</SelectItem>
            </SelectContent>
          </Select>
          <p v-if="errors.gender" class="text-sm text-destructive">{{ errors.gender[0] }}</p>
          <p v-else class="text-xs text-muted-foreground">Cabins and groups are by boys and girls.</p>
        </div>
      </div>

      <p v-if="locked" class="mt-4 flex items-start gap-2 text-sm text-muted-foreground">
        <Lock class="mt-0.5 size-4 shrink-0" />
        {{ form.firstName }} is registered for {{ profile?.registeredFor.join(' and ') }}, so date of birth and gender
        are locked. Contact us if either is wrong.
      </p>

      <Separator class="my-8" />

      <section class="space-y-5" aria-labelledby="health-heading">
        <div>
          <h2 id="health-heading" class="text-lg font-semibold">Basic health (optional)</h2>
          <p class="text-sm text-muted-foreground">
            This helps us prepare for camp. You can leave these blank and add them when you register.
          </p>
        </div>
        <div class="space-y-2">
          <Label for="dietary">Dietary needs <span class="font-normal text-muted-foreground">(optional)</span></Label>
          <Textarea id="dietary" v-model="dietary" rows="2" placeholder="For example: vegetarian, no pork" />
          <p v-if="errors.dietary" class="text-sm text-destructive">{{ errors.dietary[0] }}</p>
        </div>
        <div class="space-y-2">
          <Label for="allergies">Allergies <span class="font-normal text-muted-foreground">(optional)</span></Label>
          <Textarea id="allergies" v-model="allergies" rows="2" placeholder="For example: peanuts (mild)" />
          <p v-if="errors.allergies" class="text-sm text-destructive">{{ errors.allergies[0] }}</p>
        </div>
        <div class="space-y-2">
          <Label for="ada">Accessibility needs <span class="font-normal text-muted-foreground">(optional)</span></Label>
          <Textarea id="ada" v-model="adaNeeds" rows="2" placeholder="Anything that helps them take part fully" />
          <p v-if="errors.adaNeeds" class="text-sm text-destructive">{{ errors.adaNeeds[0] }}</p>
        </div>
      </section>

      <Alert v-if="!form.isAdult && form.firstName" class="mt-8 border-amber-200 bg-amber-50 text-amber-900">
        <Info />
        <AlertDescription class="text-amber-900">
          <template v-if="profile?.canClaimOwnAccount"
            >{{ form.firstName }} is 18, so they can have their own account. Contact us to set it up.</template
          >
          <template v-else>When {{ form.firstName }} turns 18, they can claim their own account.</template>
        </AlertDescription>
      </Alert>

      <p class="mt-6 flex items-center gap-2 text-sm text-muted-foreground">
        <Users class="size-4 shrink-0" />Household contact and payment details are shared; no need to enter them again.
      </p>

      <div v-if="isNew" class="mt-8 flex flex-wrap gap-3">
        <Button type="submit" :disabled="saving">
          <LoaderCircle v-if="saving" class="animate-spin" />{{ form.isAdult ? 'Add adult' : 'Add child' }}
        </Button>
        <Button variant="outline" as-child><RouterLink to="/family">Cancel</RouterLink></Button>
      </div>
      <p v-else-if="saveFailed" class="mt-6 text-sm text-destructive" role="alert">{{ saveFailed }}</p>
    </form>
  </div>
</template>
