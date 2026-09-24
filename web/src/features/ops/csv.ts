function cell(v: string | number) {
  const s = String(v)
  return /[",\n]/.test(s) ? `"${s.replaceAll('"', '""')}"` : s
}

/** Builds a CSV file from rows and hands it to the browser as a download. */
export function downloadCsv(filename: string, header: string[], rows: (string | number)[][]) {
  const body = [header, ...rows].map((r) => r.map(cell).join(',')).join('\n')
  const url = URL.createObjectURL(new Blob([body], { type: 'text/csv' }))
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}
