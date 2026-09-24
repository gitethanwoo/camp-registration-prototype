import { now } from '@/lib/clock'
import { dateTime } from '@/lib/format'

/** "just now", "12 minutes ago", "3 hours ago", then the date and time. */
export function ago(iso: string) {
  const mins = Math.round((now().getTime() - new Date(iso).getTime()) / 60000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins} minute${mins === 1 ? '' : 's'} ago`
  const hours = Math.round(mins / 60)
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`
  return dateTime(iso)
}
