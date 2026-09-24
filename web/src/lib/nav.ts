import type { Component } from 'vue'

/** Staff sidebar sections, in display order. */
export const navGroups = ['Front desk', 'Setup', 'Finance', 'Operations', 'Hosts'] as const
export type NavGroup = (typeof navGroups)[number]

declare module 'vue-router' {
  interface RouteMeta {
    title?: string
    /** Who may open this page. Inherited by child routes. */
    auth?: 'family' | 'staff' | 'host'
    /** Staff sidebar entry. */
    nav?: { group: NavGroup; label: string; icon: Component; order?: number; exact?: boolean }
  }
}
