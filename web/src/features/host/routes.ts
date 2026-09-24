import type { RouteRecordRaw } from 'vue-router'

// Slice 8 · Host portal (H1–H3). A top-level shell at /host for staff with the host role.
export const hostRoutes: RouteRecordRaw[] = [
  {
    path: '/host',
    component: () => import('./HostLayout.vue'),
    meta: { auth: 'host' },
    children: [
      {
        path: '',
        component: () => import('./HostHome.vue'),
        meta: { title: 'Home' },
      },
      {
        path: 'volunteers',
        component: () => import('./Volunteers.vue'),
        meta: { title: 'Volunteers' },
      },
      {
        path: 'invoices',
        component: () => import('./Invoices.vue'),
        meta: { title: 'Invoices' },
      },
      {
        path: 'invoices/:id',
        component: () => import('./Invoices.vue'),
        props: (r) => ({ id: Number(r.params.id) }),
        meta: { title: 'Invoices' },
      },
    ],
  },
]
