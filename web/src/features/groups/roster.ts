import type { RosterDraftRow } from './types'

const EMAIL = /[^\s<>,;"']+@[^\s<>,;"']+\.[^\s<>,;"']+/

let nextKey = 1
export function row(name = '', email = ''): RosterDraftRow {
  return { key: nextKey++, name, email }
}

/**
 * Reads a pasted list or a CSV file into rows. Accepts one attendee per line as
 * "Name, email", "Name <email>", "Name<TAB>email", just a name, or just an email.
 * A header line that mentions "name" or "email" and has no address is skipped.
 */
export function parseRoster(text: string): RosterDraftRow[] {
  const rows: RosterDraftRow[] = []
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim()
    if (!line) continue
    const email = EMAIL.exec(line)?.[0] ?? ''
    if (!email && /^(full\s*)?name\b|email/i.test(line)) continue
    const name = line
      .replace(email, '')
      .replace(/[<>"]/g, ' ')
      .split(/[,;\t]/)
      .map((p) => p.trim())
      .filter(Boolean)
      .join(' ')
      .replace(/\s+/g, ' ')
    rows.push(row(name, email))
  }
  return rows
}

/** Adds pasted rows, skipping anyone whose email is already on the roster. */
export function mergeRows(existing: RosterDraftRow[], incoming: RosterDraftRow[]) {
  const kept = existing.filter((r) => r.name.trim() || r.email.trim())
  const emails = new Set(kept.map((r) => r.email.trim().toLowerCase()).filter(Boolean))
  let skipped = 0
  for (const r of incoming) {
    const e = r.email.toLowerCase()
    if (e && emails.has(e)) {
      skipped++
      continue
    }
    if (e) emails.add(e)
    kept.push(r)
  }
  return { rows: kept, added: incoming.length - skipped, skipped }
}
