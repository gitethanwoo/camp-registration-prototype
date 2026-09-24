<script setup lang="ts">
import { ChevronDown, ClipboardCheck, House, LayoutGrid, LifeBuoy } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { RouterLink, RouterView } from 'vue-router'
import Logo from '@/components/Logo.vue'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { loadSession, useSession } from '@/lib/session'

const { session, initials, signIn, signOut } = useSession()

// Retreat applications and church groups belong to other slices; the menu shows them only to a
// family that has one, so most families see the same short menu.
const hasApplications = ref(false)
const hasGroups = ref(false)
const any = (url: string) =>
  api
    .get<unknown[]>(url)
    .then((rows) => rows.length > 0)
    .catch(() => false)

onMounted(async () => {
  const s = await loadSession()
  if (s.kind !== 'family') return
  const [apps, groups] = await Promise.all([any('/admittance/applications'), any('/groups')])
  hasApplications.value = apps
  hasGroups.value = groups
})

const nav = [
  { to: '/programs', label: 'Programs' },
  { to: '/family', label: 'My family' },
  { to: '/help', label: 'Help' },
]
</script>

<template>
  <div class="min-h-svh bg-background pb-20 md:pb-0">
    <header class="sticky top-0 z-30 border-b bg-background/95 backdrop-blur">
      <div class="mx-auto flex h-16 max-w-6xl items-center gap-8 px-4">
        <RouterLink
          to="/programs"
          class="rounded-md focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          <Logo />
        </RouterLink>
        <nav class="hidden items-center gap-1 md:flex" aria-label="Main">
          <Button v-for="item in nav" :key="item.to" variant="ghost" size="sm" as-child class="text-muted-foreground">
            <RouterLink :to="item.to" active-class="!text-foreground font-medium">{{ item.label }}</RouterLink>
          </Button>
        </nav>
        <div class="ml-auto flex items-center gap-3">
          <Button v-if="session && !session.signedIn" size="sm" @click="signIn()">Sign in</Button>
          <DropdownMenu v-else-if="session?.signedIn">
            <DropdownMenuTrigger
              class="flex items-center gap-2 rounded-full p-1 pr-2 hover:bg-muted focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
            >
              <Avatar class="size-8"
                ><AvatarFallback class="text-xs">{{ initials }}</AvatarFallback></Avatar
              >
              <span class="hidden text-sm sm:inline">{{ session.name }}</span>
              <ChevronDown class="size-4 text-muted-foreground" />
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" class="w-56">
              <DropdownMenuLabel>
                <div>{{ session.name }}</div>
                <div class="text-xs font-normal text-muted-foreground">{{ session.email }}</div>
              </DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem v-if="session.kind === 'family'" as-child
                ><RouterLink to="/family">My family</RouterLink></DropdownMenuItem
              >
              <DropdownMenuItem v-if="session.kind === 'family'" as-child
                ><RouterLink to="/family/registrations">My registrations</RouterLink></DropdownMenuItem
              >
              <DropdownMenuItem v-if="session.kind === 'family' && hasApplications" as-child
                ><RouterLink to="/applications">Applications</RouterLink></DropdownMenuItem
              >
              <DropdownMenuItem v-if="session.kind === 'family' && hasGroups" as-child
                ><RouterLink to="/groups">My groups</RouterLink></DropdownMenuItem
              >
              <DropdownMenuItem v-if="session.kind === 'family'" as-child
                ><RouterLink to="/family/access">Household access</RouterLink></DropdownMenuItem
              >
              <DropdownMenuItem v-if="session.kind === 'family'" as-child
                ><RouterLink to="/family/scholarships">Scholarships</RouterLink></DropdownMenuItem
              >
              <DropdownMenuItem v-if="session.kind === 'staff'" as-child
                ><RouterLink to="/admin">Staff console</RouterLink></DropdownMenuItem
              >
              <DropdownMenuItem @select="signOut">Sign out</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>
    </header>

    <main>
      <RouterView />
    </main>

    <!-- Phone: fixed bottom tab bar (plain Tailwind), primary nav within thumb reach. -->
    <nav class="fixed inset-x-0 bottom-0 z-30 grid grid-cols-4 border-t bg-background md:hidden" aria-label="Main">
      <RouterLink
        to="/programs"
        class="flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground"
        active-class="!text-foreground"
      >
        <LayoutGrid class="size-5" />Programs
      </RouterLink>
      <RouterLink
        to="/family"
        class="flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground"
        exact-active-class="!text-foreground"
      >
        <House class="size-5" />My family
      </RouterLink>
      <RouterLink
        to="/family#checklist"
        class="flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground"
      >
        <ClipboardCheck class="size-5" />Checklist
      </RouterLink>
      <RouterLink
        to="/help"
        class="flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground"
        active-class="!text-foreground"
      >
        <LifeBuoy class="size-5" />Help
      </RouterLink>
    </nav>
  </div>
</template>
