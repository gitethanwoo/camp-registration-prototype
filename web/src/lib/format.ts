const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 2, minimumFractionDigits: 0 })

export function money(cents: number | null | undefined) {
  if (cents == null) return '—'
  return usd.format(cents / 100)
}

/** Parses a DateOnly ("2028-06-12") without timezone drift. */
function parseDate(d: string) {
  if (/^\d{4}-\d{2}-\d{2}$/.test(d)) {
    const [y, m, day] = d.split('-').map(Number)
    return new Date(y, m - 1, day)
  }
  return new Date(d)
}

export function date(d: string | null | undefined, opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', year: 'numeric' }) {
  if (!d) return '—'
  return parseDate(d).toLocaleDateString('en-US', opts)
}

export function dateRange(start: string, end: string) {
  const s = parseDate(start)
  const e = parseDate(end)
  const month = (x: Date) => x.toLocaleDateString('en-US', { month: 'long' })
  if (s.getMonth() === e.getMonth()) return `${month(s)} ${s.getDate()}–${e.getDate()}, ${e.getFullYear()}`
  return `${month(s)} ${s.getDate()} – ${month(e)} ${e.getDate()}, ${e.getFullYear()}`
}

export function dateTime(d: string) {
  return new Date(d.endsWith('Z') || d.includes('+') ? d : `${d}Z`).toLocaleString('en-US', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
}

export function initials(name: string) {
  return name.split(' ').map(p => p[0]).join('').slice(0, 2).toUpperCase()
}

export function spotsLabel(remaining: number, poolName: string) {
  if (remaining <= 0) return `${poolName} is full`
  return `${remaining} ${remaining === 1 ? 'spot' : 'spots'} left`
}
