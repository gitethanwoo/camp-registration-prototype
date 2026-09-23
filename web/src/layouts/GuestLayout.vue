<script setup lang="ts">
import { ChevronDown, ClipboardCheck, House, LayoutGrid, LifeBuoy } from '@lucide/vue'
import { RouterLink, RouterView } from 'vue-router'
import Logo from '@/components/Logo.vue'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'

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
        <RouterLink to="/programs" class="rounded-md focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none">
          <Logo />
        </RouterLink>
        <nav class="hidden items-center gap-1 md:flex" aria-label="Main">
          <RouterLink
            v-for="item in nav" :key="item.to" :to="item.to"
            class="rounded-md px-3 py-2 text-sm text-muted-foreground transition-colors hover:text-foreground"
            active-class="!text-foreground font-medium"
          >
            {{ item.label }}
          </RouterLink>
        </nav>
        <div class="ml-auto flex items-center gap-3">
          <DropdownMenu>
            <DropdownMenuTrigger class="flex items-center gap-2 rounded-full p-1 pr-2 hover:bg-muted focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none">
              <Avatar class="size-8"><AvatarFallback class="text-xs">MJ</AvatarFallback></Avatar>
              <span class="hidden text-sm sm:inline">Maria Johnson</span>
              <ChevronDown class="size-4 text-muted-foreground" />
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" class="w-56">
              <DropdownMenuLabel>
                <div>Maria Johnson</div>
                <div class="text-xs font-normal text-muted-foreground">Demo guest · Johnson family</div>
              </DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem as-child><RouterLink to="/family">My family</RouterLink></DropdownMenuItem>
              <DropdownMenuItem as-child><RouterLink to="/admin">Switch to staff console</RouterLink></DropdownMenuItem>
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
      <RouterLink to="/programs" class="flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground" active-class="!text-foreground">
        <LayoutGrid class="size-5" />Programs
      </RouterLink>
      <RouterLink to="/family" class="flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground" exact-active-class="!text-foreground">
        <House class="size-5" />My family
      </RouterLink>
      <RouterLink to="/family#checklist" class="flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground">
        <ClipboardCheck class="size-5" />Checklist
      </RouterLink>
      <RouterLink to="/help" class="flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground" active-class="!text-foreground">
        <LifeBuoy class="size-5" />Help
      </RouterLink>
    </nav>
  </div>
</template>
