import { CalendarCog, ClipboardList, Gauge, ListOrdered } from '@lucide/vue'
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import AdminLayout from '@/layouts/AdminLayout.vue'
import GuestLayout from '@/layouts/GuestLayout.vue'
import { loadSession, signIn } from '@/lib/session'

// Feature slices register pages from src/features/<slice>/routes.ts; see src/features/README.md.
const slices = import.meta.glob<{
  routes?: RouteRecordRaw[]
  adminRoutes?: RouteRecordRaw[]
  hostRoutes?: RouteRecordRaw[]
}>('./features/*/routes.ts', { eager: true })
const sliceRoutes = Object.values(slices).flatMap((m) => m.routes ?? [])
const sliceAdminRoutes = Object.values(slices).flatMap((m) => m.adminRoutes ?? [])
// Top-level portals with their own shell (the host portal at /host).
const sliceHostRoutes = Object.values(slices).flatMap((m) => m.hostRoutes ?? [])

export const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: (to, _from, saved) => saved ?? (to.hash ? { el: to.hash, top: 80 } : { top: 0 }),
  routes: [
    {
      path: '/',
      component: GuestLayout,
      children: [
        { path: '', redirect: '/programs' },
        { path: 'programs', component: () => import('@/pages/guest/Programs.vue'), meta: { title: 'Programs' } },
        { path: 'programs/:slug', component: () => import('@/pages/guest/ProgramDetail.vue'), props: true },
        {
          path: 'register/:sessionId',
          component: () => import('@/pages/guest/Register.vue'),
          props: (r) => ({ sessionId: Number(r.params.sessionId) }),
          meta: { auth: 'family' },
        },
        {
          path: 'confirmation/:code',
          component: () => import('@/pages/guest/Confirmation.vue'),
          props: true,
          meta: { auth: 'family' },
        },
        {
          path: 'family',
          component: () => import('@/pages/guest/Family.vue'),
          meta: { title: 'My family', auth: 'family' },
        },
        { path: 'help', component: () => import('@/pages/guest/Help.vue'), meta: { title: 'Help' } },
        { path: 'no-access', component: () => import('@/pages/NoAccess.vue'), meta: { title: 'No access' } },
        ...sliceRoutes,
      ],
    },
    {
      path: '/admin',
      component: AdminLayout,
      meta: { auth: 'staff' },
      children: [
        {
          path: '',
          component: () => import('@/pages/admin/Dashboard.vue'),
          meta: {
            title: 'Session overview',
            nav: { group: 'Front desk', label: 'Session overview', icon: Gauge, order: 10, exact: true },
          },
        },
        {
          path: 'registrations',
          component: () => import('@/pages/admin/Registrations.vue'),
          meta: {
            title: 'Registrations',
            nav: { group: 'Front desk', label: 'Registrations', icon: ClipboardList, order: 20 },
          },
        },
        {
          path: 'registrations/:id',
          component: () => import('@/pages/admin/RegistrationDetail.vue'),
          props: (r) => ({ id: Number(r.params.id) }),
          meta: { title: 'Registration' },
        },
        {
          path: 'waitlist',
          component: () => import('@/pages/admin/Waitlist.vue'),
          meta: { title: 'Waitlists', nav: { group: 'Front desk', label: 'Waitlists', icon: ListOrdered, order: 30 } },
        },
        {
          path: 'session',
          component: () => import('@/pages/admin/SessionEditor.vue'),
          meta: {
            title: 'Sessions & capacity',
            nav: { group: 'Setup', label: 'Sessions & capacity', icon: CalendarCog, order: 10 },
          },
        },
        ...sliceAdminRoutes,
      ],
    },
    ...sliceHostRoutes,
    { path: '/:pathMatch(.*)*', redirect: '/programs' },
  ],
})

router.beforeEach(async (to) => {
  const needs = to.matched.findLast((r) => r.meta.auth)?.meta.auth
  if (!needs) return true
  const session = await loadSession()
  if (!session.signedIn) {
    signIn(to.fullPath)
    return false
  }
  // Host coordinators are staff with the host role; they have their own portal and no console.
  if (needs === 'host') {
    if (session.kind !== 'staff') return { path: '/no-access', query: { need: 'staff' } }
    return session.role === 'host' ? true : { path: '/admin' }
  }
  if (needs === 'staff' && session.role === 'host') return { path: '/host' }
  if (needs !== session.kind) return { path: '/no-access', query: { need: needs } }
  return true
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · WinShape Camps` : 'WinShape Camps'
})
