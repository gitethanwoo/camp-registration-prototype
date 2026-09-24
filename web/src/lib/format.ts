const wholeDollars = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })
const withCents = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2 })

/** Cents from the server as dollars: "$475" when whole, otherwise always two decimals ("$427.50"). */
export function money(cents: number | null | undefined) {
  if (cents == null) return '—'
  return (cents % 100 === 0 ? wholeDollars : withCents).format(cents / 100)
}

/** Parses a DateOnly ("2028-06-12") without timezone drift. */
function parseDate(d: string) {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(d)
  if (m) return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  return new Date(d)
}

export function date(
  d: string | null | undefined,
  opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', year: 'numeric' },
) {
  if (!d) return '—'
  return parseDate(d).toLocaleDateString('en-US', opts)
}

const month = (x: Date) => x.toLocaleDateString('en-US', { month: 'long' })

export function dateRange(start: string, end: string) {
  const s = parseDate(start)
  const e = parseDate(end)
  if (s.getMonth() === e.getMonth()) return `${month(s)} ${s.getDate()}–${e.getDate()}, ${e.getFullYear()}`
  return `${month(s)} ${s.getDate()} – ${month(e)} ${e.getDate()}, ${e.getFullYear()}`
}

export function dateTime(d: string) {
  return new Date(d.endsWith('Z') || d.includes('+') ? d : `${d}Z`).toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

export function initials(name: string) {
  return name
    .split(' ')
    .map((p) => p[0])
    .join('')
    .slice(0, 2)
    .toUpperCase()
}

export function spotsLabel(remaining: number, poolName: string) {
  if (remaining <= 0) return `${poolName} is full`
  return `${remaining} ${remaining === 1 ? 'spot' : 'spots'} left`
}
