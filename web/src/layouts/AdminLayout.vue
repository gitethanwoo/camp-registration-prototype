<script setup lang="ts">
import { CalendarCog, ChevronsUpDown, ClipboardList, Gauge, ListOrdered, LogOut, Search } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import Logo from '@/components/Logo.vue'
import { useAdminScope } from '@/composables/useAdminScope'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Breadcrumb, BreadcrumbItem, BreadcrumbLink, BreadcrumbList, BreadcrumbPage, BreadcrumbSeparator } from '@/components/ui/breadcrumb'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Separator } from '@/components/ui/separator'
import { Button } from '@/components/ui/button'
import {
  Sidebar, SidebarContent, SidebarFooter, SidebarGroup, SidebarGroupLabel, SidebarHeader, SidebarInset,
  SidebarMenu, SidebarMenuButton, SidebarMenuItem, SidebarProvider, SidebarRail, SidebarTrigger,
} from '@/components/ui/sidebar'
import { date } from '@/lib/format'

const route = useRoute()
const router = useRouter()
const { ministries, current, select } = useAdminScope()

const groups = [
  {
    label: 'Front desk',
    items: [
      { to: '/admin', label: 'Session overview', icon: Gauge, exact: true },
      { to: '/admin/registrations', label: 'Registrations', icon: ClipboardList },
      { to: '/admin/waitlist', label: 'Waitlists', icon: ListOrdered },
    ],
  },
  {
    label: 'Setup',
    items: [{ to: '/admin/session', label: 'Sessions & capacity', icon: CalendarCog }],
  },
]

const isActive = (to: string, exact?: boolean) => exact ? route.path === to : route.path.startsWith(to)
const pageTitle = computed(() => (route.meta.title as string) ?? '')

function choose(id: number) {
  select(id)
  if (route.params.id) router.push('/admin/registrations')
}
</script>

<template>
  <SidebarProvider>
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <div class="flex h-10 items-center px-2 group-data-[collapsible=icon]:px-0.5">
          <Logo class="group-data-[collapsible=icon]:hidden" />
          <Logo compact class="hidden group-data-[collapsible=icon]:flex" />
        </div>
        <!-- Scope switcher: Ministry ▸ Program ▸ Session -->
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <SidebarMenuButton size="lg" class="border bg-background data-[state=open]:bg-sidebar-accent" tooltip="Scope">
              <div class="grid flex-1 text-left leading-tight">
                <span class="truncate text-xs text-muted-foreground">{{ current?.ministry.name ?? 'Loading…' }} ▸ {{ current?.program.name }}</span>
                <span class="truncate text-sm font-medium">{{ current?.session.name }}</span>
              </div>
              <ChevronsUpDown class="ml-auto size-4" />
            </SidebarMenuButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent class="w-72" align="start">
            <template v-for="m in ministries" :key="m.id">
              <DropdownMenuLabel class="text-xs text-muted-foreground">{{ m.name }}</DropdownMenuLabel>
              <template v-for="p in m.programs" :key="p.id">
                <DropdownMenuItem v-for="s in p.sessions" :key="s.id" class="flex-col items-start gap-0 pl-4" @select="choose(s.id)">
                  <span>{{ p.name }} · {{ s.name }}</span>
                  <span class="text-xs text-muted-foreground">{{ date(s.startDate) }}{{ p.type !== 'Standard' ? ` · ${p.type}` : '' }}</span>
                </DropdownMenuItem>
              </template>
              <DropdownMenuSeparator />
            </template>
          </DropdownMenuContent>
        </DropdownMenu>
      </SidebarHeader>

      <SidebarContent>
        <SidebarGroup v-for="g in groups" :key="g.label">
          <SidebarGroupLabel>{{ g.label }}</SidebarGroupLabel>
          <SidebarMenu>
            <SidebarMenuItem v-for="item in g.items" :key="item.to">
              <SidebarMenuButton as-child :is-active="isActive(item.to, item.exact)" :tooltip="item.label">
                <RouterLink :to="item.to"><component :is="item.icon" /><span>{{ item.label }}</span></RouterLink>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>

      <SidebarFooter>
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <SidebarMenuButton size="lg" tooltip="Diane Carter">
              <Avatar class="size-8 rounded-lg"><AvatarFallback class="rounded-lg text-xs">DC</AvatarFallback></Avatar>
              <div class="grid flex-1 text-left leading-tight">
                <span class="truncate text-sm font-medium">Diane Carter</span>
                <span class="truncate text-xs text-muted-foreground">CET · all ministries</span>
              </div>
              <ChevronsUpDown class="ml-auto size-4" />
            </SidebarMenuButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent side="top" align="start" class="w-60">
            <DropdownMenuLabel class="text-xs font-normal text-muted-foreground">Signed in with Entra SSO (demo)</DropdownMenuLabel>
            <DropdownMenuItem as-child><RouterLink to="/programs"><LogOut /> Back to guest site</RouterLink></DropdownMenuItem>
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
            <BreadcrumbItem class="hidden md:block"><BreadcrumbLink as-child><RouterLink to="/admin">{{ current?.ministry.name }}</RouterLink></BreadcrumbLink></BreadcrumbItem>
            <BreadcrumbSeparator class="hidden md:block" />
            <BreadcrumbItem class="hidden sm:block"><span class="truncate">{{ current?.program.name }} · {{ current?.session.name }}</span></BreadcrumbItem>
            <BreadcrumbSeparator class="hidden sm:block" />
            <BreadcrumbItem><BreadcrumbPage class="truncate">{{ pageTitle }}</BreadcrumbPage></BreadcrumbItem>
          </BreadcrumbList>
        </Breadcrumb>
        <Button variant="outline" size="sm" as-child class="ml-auto text-muted-foreground" title="Search every ministry by name, email, or phone">
          <RouterLink to="/admin/registrations?all=1"><Search /><span class="hidden sm:inline">Search families</span></RouterLink>
        </Button>
      </header>
      <div class="min-w-0 flex-1 p-4 md:p-6">
        <RouterView :key="current?.session.id" />
      </div>
    </SidebarInset>
  </SidebarProvider>
</template>
