<script setup lang="ts">
import { ArrowLeft, Check, CircleCheck, Clock, EllipsisVertical, Plus, Trash2, TriangleAlert, X } from '@lucide/vue'
import { onMounted, reactive, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, api } from '@/lib/api'
import { date, initials } from '@/lib/format'
import type { HouseholdAccess } from './types'

type Adult = HouseholdAccess['adults'][number]

const data = ref<HouseholdAccess | null>(null)
async function load() {
  data.value = await api.get<HouseholdAccess>('/family/access')
}
onMounted(load)

// Invite
const inviteOpen = ref(false)
const invite = reactive({ firstName: '', lastName: '', email: '' })
const inviteErrors = ref<Record<string, string[]>>({})
const inviting = ref(false)
function openInvite() {
  Object.assign(invite, { firstName: '', lastName: '', email: '' })
  inviteErrors.value = {}
  inviteOpen.value = true
}
async function sendInvite() {
  inviting.value = true
  inviteErrors.value = {}
  try {
    await api.post('/family/invitations', invite)
    inviteOpen.value = false
    toast.success(`Invitation sent to ${invite.email.trim().toLowerCase()}`)
    await load()
  } catch (e) {
    if (e instanceof ApiError && Object.keys(e.errors).length) inviteErrors.value = e.errors
    else toast.error(e instanceof Error ? e.message : 'The invitation wasn’t sent.')
  } finally {
    inviting.value = false
  }
}
async function cancelInvite(id: number, email: string) {
  try {
    await api.delete(`/family/invitations/${id}`)
    toast.success(`Invitation to ${email} cancelled`)
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'The invitation wasn’t cancelled.')
  }
}

// Revoke
const revoking = ref<Adult | null>(null)
const revokeOpen = ref(false)
const busy = ref(false)
function askRevoke(a: Adult) {
  revoking.value = a
  revokeOpen.value = true
}
async function revoke() {
  const a = revoking.value
  if (!a) return
  busy.value = true
  try {
    await api.post(`/family/members/${a.id}/revoke-access`)
    toast.success(`${a.firstName} no longer has account access. They’re still a guardian in your household.`)
    revokeOpen.value = false
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Access wasn’t revoked.')
  } finally {
    busy.value = false
  }
}

const tones = ['bg-emerald-100 text-emerald-800', 'bg-violet-100 text-violet-800', 'bg-sky-100 text-sky-800']
</script>

<template>
  <div class="mx-auto max-w-4xl px-4 py-8 md:py-12">
    <Button variant="link" as-child class="h-auto p-0 text-muted-foreground">
      <RouterLink to="/family"><ArrowLeft />My family</RouterLink>
    </Button>
    <h1 class="mt-4 text-3xl font-semibold tracking-tight md:text-4xl">Household access</h1>
    <p class="mt-1 max-w-xl text-muted-foreground">
      Who can sign in to this household, what they can do, and the children linked to it.
    </p>

    <div v-if="!data" class="mt-8 space-y-4">
      <Skeleton class="h-28 rounded-xl" /><Skeleton class="h-28 rounded-xl" />
    </div>

    <template v-else>
      <section class="mt-8" aria-labelledby="adults-heading">
        <div class="flex items-center justify-between gap-4">
          <h2 id="adults-heading" class="text-xl font-semibold">Adults</h2>
          <Button v-if="data.canManage" @click="openInvite"><Plus />Invite adult</Button>
        </div>
        <p v-if="!data.canManage" class="mt-2 text-sm text-muted-foreground">
          Only the primary owner can invite adults or change access.
        </p>

        <ul class="mt-4 space-y-3">
          <li v-for="a in data.adults" :key="a.id">
            <Card class="py-4">
              <CardContent class="space-y-3 px-4 sm:px-6">
                <div class="flex items-center gap-4">
                  <Avatar class="size-12"
                    ><AvatarFallback class="bg-muted font-semibold">{{
                      initials(`${a.firstName} ${a.lastName}`)
                    }}</AvatarFallback></Avatar
                  >
                  <div class="min-w-0 flex-1">
                    <p class="font-semibold">
                      {{ a.firstName }} {{ a.lastName }}
                      <span v-if="a.isYou" class="font-normal text-muted-foreground">(you)</span>
                    </p>
                    <p class="truncate text-sm text-muted-foreground">
                      {{ a.access }}<template v-if="a.email"> · {{ a.email }}</template>
                    </p>
                  </div>
                  <Badge v-if="a.role" variant="outline" class="border-emerald-200 bg-emerald-50 text-emerald-800"
                    >Active</Badge
                  >
                  <Badge v-else variant="outline" class="bg-muted text-muted-foreground">Guardian only</Badge>
                  <DropdownMenu v-if="data.canManage && a.role && a.role !== 'Primary'">
                    <DropdownMenuTrigger as-child>
                      <Button variant="outline" size="icon-sm" :aria-label="`Actions for ${a.firstName}`"
                        ><EllipsisVertical
                      /></Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem class="text-destructive focus:text-destructive" @select="askRevoke(a)"
                        ><Trash2 class="text-destructive" />Revoke access</DropdownMenuItem
                      >
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
                <ul v-if="a.permissions.length" class="flex flex-wrap gap-x-6 gap-y-1 text-sm text-muted-foreground">
                  <li v-for="p in a.permissions" :key="p" class="flex items-center gap-1.5">
                    <Check class="size-4 text-emerald-600" />{{ p }}
                  </li>
                </ul>
                <p v-else class="text-sm text-muted-foreground">
                  Still a guardian of the children below; can’t sign in to this household.
                </p>
              </CardContent>
            </Card>
          </li>
          <li v-for="i in data.invitations" :key="`invite-${i.id}`">
            <Card class="border-dashed py-4">
              <CardContent class="flex flex-wrap items-center gap-4 px-4 sm:px-6">
                <Avatar class="size-12"
                  ><AvatarFallback class="bg-muted"><Clock class="size-5 text-muted-foreground" /></AvatarFallback
                ></Avatar>
                <div class="min-w-0 flex-1">
                  <p class="font-semibold">{{ i.firstName }} {{ i.lastName }}</p>
                  <p class="truncate text-sm text-muted-foreground">
                    Invited as co-owner · {{ i.email }} ·
                    {{ i.expired ? `expired ${date(i.expiresAt)}` : `expires ${date(i.expiresAt)}` }}
                  </p>
                </div>
                <Badge variant="outline" class="border-amber-200 bg-amber-50 text-amber-800">{{
                  i.expired ? 'Expired' : 'Invited'
                }}</Badge>
                <Button
                  v-if="data.canManage"
                  variant="ghost"
                  size="sm"
                  :aria-label="`Cancel invitation to ${i.email}`"
                  @click="cancelInvite(i.id, i.email)"
                  ><X />Cancel</Button
                >
              </CardContent>
            </Card>
          </li>
        </ul>
      </section>

      <section class="mt-10" aria-labelledby="dependents-heading">
        <h2 id="dependents-heading" class="text-xl font-semibold">Dependents</h2>
        <p v-if="!data.dependents.length" class="mt-3 text-sm text-muted-foreground">
          No children yet.
          <Button variant="link" as-child class="h-auto p-0"
            ><RouterLink to="/family/members/new">Add a child</RouterLink></Button
          >
        </p>
        <ul class="mt-4 space-y-3">
          <li v-for="(d, i) in data.dependents" :key="d.id">
            <Card class="py-4">
              <CardContent class="flex items-center gap-4 px-4 sm:px-6">
                <Avatar class="size-12"
                  ><AvatarFallback :class="['font-semibold', tones[i % tones.length]]">{{
                    initials(`${d.firstName} ${d.lastName}`)
                  }}</AvatarFallback></Avatar
                >
                <div class="min-w-0 flex-1">
                  <RouterLink :to="`/family/members/${d.id}`" class="font-semibold hover:underline"
                    >{{ d.firstName }} {{ d.lastName }}</RouterLink
                  >
                  <p class="text-sm text-muted-foreground">{{ d.gradeLabel }} in fall {{ data.seasonYear }}</p>
                </div>
                <span class="hidden items-center gap-1.5 text-sm text-muted-foreground sm:flex"
                  ><CircleCheck class="size-4 text-emerald-600" />Included in this household</span
                >
              </CardContent>
            </Card>
          </li>
        </ul>
      </section>
    </template>

    <Dialog v-model:open="inviteOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Invite an adult</DialogTitle>
          <DialogDescription>
            They’ll get an email to join as a co-owner: they can register household members and see history and
            payments.
          </DialogDescription>
        </DialogHeader>
        <form id="invite-form" class="space-y-4" novalidate @submit.prevent="sendInvite">
          <div class="grid grid-cols-2 gap-3">
            <div class="space-y-2">
              <Label for="inv-first">First name</Label>
              <Input id="inv-first" v-model="invite.firstName" :aria-invalid="!!inviteErrors.firstName" />
              <p v-if="inviteErrors.firstName" class="text-sm text-destructive">{{ inviteErrors.firstName[0] }}</p>
            </div>
            <div class="space-y-2">
              <Label for="inv-last">Last name</Label>
              <Input id="inv-last" v-model="invite.lastName" :aria-invalid="!!inviteErrors.lastName" />
              <p v-if="inviteErrors.lastName" class="text-sm text-destructive">{{ inviteErrors.lastName[0] }}</p>
            </div>
          </div>
          <div class="space-y-2">
            <Label for="inv-email">Email</Label>
            <Input
              id="inv-email"
              v-model="invite.email"
              type="email"
              autocomplete="off"
              :aria-invalid="!!inviteErrors.email"
            />
            <p v-if="inviteErrors.email" class="text-sm text-destructive">{{ inviteErrors.email[0] }}</p>
          </div>
        </form>
        <DialogFooter>
          <Button variant="outline" @click="inviteOpen = false">Cancel</Button>
          <Button type="submit" form="invite-form" :disabled="inviting">Send invitation</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog v-model:open="revokeOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Revoke {{ revoking?.firstName }}’s access?</AlertDialogTitle>
          <AlertDialogDescription>
            {{ revoking?.firstName }} won’t be able to sign in to this household, register anyone, or see payments.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <Alert class="border-amber-200 bg-amber-50 text-amber-900">
          <TriangleAlert />
          <AlertDescription class="text-amber-900">
            Revoking account access does not remove guardian links. {{ revoking?.firstName }} stays listed as a guardian
            of your children.
          </AlertDescription>
        </Alert>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="busy">Cancel</AlertDialogCancel>
          <AlertDialogAction :disabled="busy" @click.prevent="revoke">Revoke</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
