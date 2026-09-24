import { HeartPulse, UsersRound } from '@lucide/vue'
import { adminRoles } from '@/lib/nav'
import type { RouteRecordRaw } from 'vue-router'

// Slice 10 · Staff access (K11) and health collection settings (K9). Both are admin-only on the server.
export const adminRoutes: RouteRecordRaw[] = [
  {
    path: 'setup/users',
    component: () => import('./StaffUsers.vue'),
    meta: {
      roles: adminRoles,
      title: 'Staff access',
      nav: { group: 'Setup', label: 'Users', icon: UsersRound, order: 60 },
    },
  },
  {
    path: 'setup/health',
    component: () => import('./HealthSettings.vue'),
    meta: {
      roles: adminRoles,
      title: 'Health collection settings',
      nav: { group: 'Setup', label: 'Health settings', icon: HeartPulse, order: 50 },
    },
  },
]
