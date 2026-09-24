import { CalendarRange, Tent } from '@lucide/vue'
import { adminRoles } from '@/lib/nav'
import type { RouteRecordRaw } from 'vue-router'

// P3 activity detail (public) and choosing activities after registering (F1 checklist).
export const routes: RouteRecordRaw[] = [
  {
    path: 'activities/:id(\\d+)',
    component: () => import('./ActivityPage.vue'),
    props: (r) => ({ id: Number(r.params.id), sessionId: r.query.sessionId ? Number(r.query.sessionId) : null }),
    meta: { title: 'Activity' },
  },
  {
    path: 'family/activities/:registrationId(\\d+)',
    component: () => import('./FamilyActivities.vue'),
    props: (r) => ({ registrationId: Number(r.params.registrationId) }),
    meta: { title: 'Choose activities', auth: 'family' },
  },
]

// K8 activity catalog (admins) and O4 activity schedule (staff read, CET and admins change).
export const adminRoutes: RouteRecordRaw[] = [
  {
    path: 'setup/activities',
    component: () => import('./ActivityCatalog.vue'),
    meta: {
      roles: adminRoles,
      title: 'Activity catalog',
      nav: { group: 'Setup', label: 'Activities', icon: Tent, order: 45 },
    },
  },
  {
    path: 'ops/activities',
    component: () => import('./ActivitySchedule.vue'),
    meta: {
      title: 'Activity schedule',
      nav: { group: 'Operations', label: 'Activities', icon: CalendarRange, order: 35 },
    },
  },
]
