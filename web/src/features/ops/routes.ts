import { BedDouble, ClipboardCheck, ScanLine, UsersRound } from '@lucide/vue'
import type { RouteRecordRaw } from 'vue-router'

// O1, O2, O3, O5 · Camp operations for the selected session.
export const adminRoutes: RouteRecordRaw[] = [
  {
    path: 'ops/readiness',
    component: () => import('./SessionReadiness.vue'),
    meta: {
      title: 'Session readiness',
      nav: { group: 'Operations', label: 'Session readiness', icon: ClipboardCheck, order: 10 },
    },
  },
  {
    path: 'ops/groups',
    component: () => import('./GroupBoard.vue'),
    meta: { title: 'Group assignments', nav: { group: 'Operations', label: 'Groups', icon: UsersRound, order: 20 } },
  },
  {
    path: 'ops/rooming',
    component: () => import('./RoomingBoard.vue'),
    meta: { title: 'Rooming board', nav: { group: 'Operations', label: 'Rooming', icon: BedDouble, order: 30 } },
  },
  {
    path: 'ops/check-in',
    component: () => import('./CheckIn.vue'),
    meta: {
      title: 'Check-in and check-out',
      nav: { group: 'Operations', label: 'Check-in', icon: ScanLine, order: 40 },
    },
  },
]
