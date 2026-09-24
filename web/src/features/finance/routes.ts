import { BookOpenCheck, CalendarX2, ChartColumn, HandHeart, Scale } from '@lucide/vue'
import type { RouteRecordRaw } from 'vue-router'

// O6 · A family's scholarship applications and the application form.
export const routes: RouteRecordRaw[] = [
  {
    path: 'family/scholarships',
    component: () => import('./Scholarships.vue'),
    meta: { title: 'Scholarships', auth: 'family' },
  },
  {
    path: 'family/scholarships/apply/:code',
    component: () => import('./ScholarshipApply.vue'),
    props: true,
    meta: { title: 'Apply for financial assistance', auth: 'family' },
  },
]

// FN1–FN4 (Finance) and O7 (scholarship review).
export const adminRoutes: RouteRecordRaw[] = [
  {
    path: 'finance/reports',
    component: () => import('./Reports.vue'),
    meta: { title: 'Reports', nav: { group: 'Finance', label: 'Reports', icon: ChartColumn, order: 10 } },
  },
  {
    path: 'finance/reconciliation',
    component: () => import('./Reconciliation.vue'),
    meta: {
      title: 'Payment reconciliation',
      nav: { group: 'Finance', label: 'Reconciliation', icon: Scale, order: 20 },
    },
  },
  {
    path: 'finance/plan-exceptions',
    component: () => import('./PlanExceptions.vue'),
    meta: {
      title: 'Payment plan exceptions',
      nav: { group: 'Finance', label: 'Plan exceptions', icon: CalendarX2, order: 30 },
    },
  },
  {
    path: 'finance/journals',
    component: () => import('./JournalExports.vue'),
    meta: {
      title: 'Oracle Fusion journal exports',
      nav: { group: 'Finance', label: 'Oracle exports', icon: BookOpenCheck, order: 40 },
    },
  },
  {
    path: 'scholarships',
    component: () => import('./ScholarshipReview.vue'),
    meta: {
      title: 'Scholarship reviews',
      nav: { group: 'Operations', label: 'Scholarships', icon: HandHeart, order: 60 },
    },
  },
]
