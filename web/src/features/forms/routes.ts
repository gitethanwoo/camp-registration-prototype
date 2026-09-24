import { ListChecks } from '@lucide/vue'
import { adminRoles } from '@/lib/nav'
import type { RouteRecordRaw } from 'vue-router'

// Slice 9 · Registration forms (K6). Admin-only on the server; answers show on C3 and F5.
export const adminRoutes: RouteRecordRaw[] = [
  {
    path: 'setup/forms',
    component: () => import('./FormBuilder.vue'),
    props: (r) => ({ programId: r.query.program ? Number(r.query.program) : null }),
    meta: {
      roles: adminRoles,
      title: 'Registration forms',
      nav: { group: 'Setup', label: 'Registration forms', icon: ListChecks, order: 35 },
    },
  },
]
