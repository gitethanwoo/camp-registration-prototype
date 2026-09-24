import { HeartHandshake } from '@lucide/vue'
import type { RouteRecordRaw } from 'vue-router'

// Admittance programs (WSM retreats): couples apply, staff review, approval charges the held card.
export const routes: RouteRecordRaw[] = [
  {
    path: 'apply/:sessionId',
    component: () => import('./Apply.vue'),
    props: (r) => ({ sessionId: Number(r.params.sessionId) }),
    meta: { title: 'Apply', auth: 'family' },
  },
  {
    path: 'applications',
    component: () => import('./MyApplications.vue'),
    meta: { title: 'My applications', auth: 'family' },
  },
  {
    path: 'applications/:id',
    component: () => import('./ApplicationStatus.vue'),
    props: (r) => ({ id: Number(r.params.id) }),
    meta: { title: 'Application status', auth: 'family' },
  },
]

export const adminRoutes: RouteRecordRaw[] = [
  {
    path: 'applications',
    component: () => import('./ReviewQueue.vue'),
    meta: {
      title: 'Applications',
      nav: { group: 'Front desk', label: 'Applications', icon: HeartHandshake, order: 25 },
    },
  },
]
