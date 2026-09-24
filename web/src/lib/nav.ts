import type { Component } from 'vue'

/** Staff sidebar sections, in display order. */
export const navGroups = ['Front desk', 'Setup', 'Finance', 'Operations', 'Hosts'] as const
export type NavGroup = (typeof navGroups)[number]

/** Console roles, matching the API policies in `Auth/Identity.cs` (admin passes every policy). */
export type StaffRole = 'cet' | 'finance' | 'admin'
/** `Policies.Cet` */
export const cetRoles: StaffRole[] = ['cet', 'admin']
/** `Policies.Finance` */
export const financeRoles: StaffRole[] = ['finance', 'admin']
/** `Policies.Admin` */
export const adminRoles: StaffRole[] = ['admin']

declare module 'vue-router' {
  interface RouteMeta {
    title?: string
    /** Who may open this page. Inherited by child routes. */
    auth?: 'family' | 'staff' | 'host'
    /**
     * Staff roles whose API calls this page's data needs. The sidebar hides the entry from other roles;
     * the page itself still explains a 403. Leave unset for pages every console role can read.
     */
    roles?: StaffRole[]
    /** Staff sidebar entry. */
    nav?: { group: NavGroup; label: string; icon: Component; order?: number; exact?: boolean }
  }
}
