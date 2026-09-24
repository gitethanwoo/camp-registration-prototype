import { DollarSign, FilePen, LayoutGrid, ScrollText, TicketPercent } from '@lucide/vue'
import type { RouteRecordRaw } from 'vue-router'

// Slice 5 · Program setup (K2, K3, K4, K5, K7, K12). Setup pages are admin-only on the server;
// the audit log is readable by any staff member.
export const adminRoutes: RouteRecordRaw[] = [
  {
    path: 'setup/programs',
    component: () => import('./ProgramList.vue'),
    meta: { title: 'Programs', nav: { group: 'Setup', label: 'Programs', icon: LayoutGrid, order: 5 } },
  },
  {
    path: 'setup/sessions',
    component: () => import('./SessionSetup.vue'),
    // No sidebar entry: "Sessions & capacity" (/admin/session) links admins here, and so does each program.
    meta: { title: 'Session setup' },
  },
  {
    path: 'setup/sessions/:id(\\d+)',
    component: () => import('./SessionSetup.vue'),
    props: (r) => ({ id: Number(r.params.id) }),
    meta: { title: 'Session setup' },
  },
  {
    path: 'setup/pricing',
    component: () => import('./PricingPolicies.vue'),
    meta: { title: 'Pricing and policies', nav: { group: 'Setup', label: 'Pricing', icon: DollarSign, order: 20 } },
  },
  {
    path: 'setup/discount-rules',
    component: () => import('./DiscountRules.vue'),
    meta: { title: 'Discount rules', nav: { group: 'Setup', label: 'Discount rules', icon: TicketPercent, order: 30 } },
  },
  {
    path: 'setup/waivers',
    component: () => import('./WaiverTemplates.vue'),
    meta: { title: 'Waiver templates', nav: { group: 'Setup', label: 'Waivers', icon: FilePen, order: 40 } },
  },
  {
    path: 'setup/audit',
    component: () => import('./AuditLog.vue'),
    meta: { title: 'Audit log', nav: { group: 'Setup', label: 'Audit log', icon: ScrollText, order: 90 } },
  },
]
