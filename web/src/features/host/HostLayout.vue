<script setup lang="ts">
import { ChevronsUpDown, FileText, House, LogOut, Users } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import Logo from '@/components/Logo.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Separator } from '@/components/ui/separator'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
} from '@/components/ui/sidebar'
import { useSession } from '@/lib/session'
import { useHost } from './useHost'

const route = useRoute()
const { session, initials, signOut } = useSession()
const { me, notLinked } = useHost()

const items = [
  { to: '/host', label: 'Home', icon: House, exact: true },
  { to: '/host/volunteers', label: 'Volunteers', icon: Users },
  { to: '/host/invoices', label: 'Invoices', icon: FileText },
]
const isActive = (to: string, exact?: boolean) => (exact ? route.path === to : route.path.startsWith(to))
const pageTitle = computed(() => (route.meta.title as string) ?? '')
</script>

<template>
  <SidebarProvider>
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <div class="flex min-h-10 items-center gap-2 px-2 group-data-[collapsible=icon]:px-0.5">
          <Logo compact />
          <div class="grid min-w-0 leading-tight group-data-[collapsible=icon]:hidden">
            <span class="font-semibold tracking-tight">WinShape</span>
            <span class="truncate text-xs text-muted-foreground">{{ me?.organization ?? 'Host' }} · Host</span>
          </div>
        </div>
      </SidebarHeader>

      <SidebarContent>
        <SidebarGroup>
          <SidebarMenu>
            <SidebarMenuItem v-for="item in items" :key="item.to">
              <SidebarMenuButton as-child :is-active="isActive(item.to, item.exact)" :tooltip="item.label">
                <RouterLink :to="item.to"
                  ><component :is="item.icon" /><span>{{ item.label }}</span></RouterLink
                >
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>

      <SidebarFooter>
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <SidebarMenuButton size="lg" :tooltip="session?.name">
              <Avatar class="size-8 rounded-lg"
                ><AvatarFallback class="rounded-lg text-xs">{{ initials }}</AvatarFallback></Avatar
              >
              <div class="grid flex-1 text-left leading-tight">
                <span class="truncate text-sm font-medium">{{ session?.name }}</span>
                <span class="truncate text-xs text-muted-foreground">{{ me?.title ?? 'Host coordinator' }}</span>
              </div>
              <ChevronsUpDown class="ml-auto size-4" />
            </SidebarMenuButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent side="top" align="start" class="w-60">
            <DropdownMenuLabel class="text-xs font-normal text-muted-foreground">{{
              session?.email
            }}</DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem @select="signOut"><LogOut /> Sign out</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>

    <SidebarInset class="min-w-0">
      <header class="flex h-14 shrink-0 items-center gap-2 border-b px-4">
        <SidebarTrigger class="-ml-1" />
        <Separator orientation="vertical" class="mr-2 !h-4" />
        <Breadcrumb class="min-w-0">
          <BreadcrumbList class="flex-nowrap">
            <BreadcrumbItem class="hidden sm:inline-flex"
              ><BreadcrumbLink as-child><RouterLink to="/host">Host</RouterLink></BreadcrumbLink></BreadcrumbItem
            >
            <BreadcrumbSeparator class="hidden sm:block" />
            <BreadcrumbItem
              ><BreadcrumbPage class="truncate">{{ pageTitle }}</BreadcrumbPage></BreadcrumbItem
            >
          </BreadcrumbList>
        </Breadcrumb>
        <span class="ml-auto min-w-0 truncate pl-3 text-right text-sm text-muted-foreground md:hidden"
          >{{ me?.organization }} · Host</span
        >
      </header>
      <div class="min-w-0 flex-1 p-4 md:p-6">
        <div v-if="notLinked" class="mx-auto max-w-lg py-10">
          <Alert>
            <AlertTitle>No host church on this account</AlertTitle>
            <AlertDescription>{{ notLinked }}</AlertDescription>
          </Alert>
          <Button variant="outline" class="mt-4" @click="signOut">Sign out</Button>
        </div>
        <RouterView v-else />
      </div>
    </SidebarInset>
  </SidebarProvider>
</template>
