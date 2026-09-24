/**
 * The app's clock. Use now() instead of Date.now() or new Date() so every "today" matches the API's
 * demo clock (GET /api/clock: the demo starts March 2, 2028 and moves forward in real time).
 */
let offsetMs = 0

/** Loads the server's demo-clock offset once, before the app mounts. Falls back to the real time. */
export async function loadClock(): Promise<void> {
  try {
    const res = await fetch('/api/clock')
    if (!res.ok) return
    const body = (await res.json()) as { offsetMs?: number }
    if (typeof body.offsetMs === 'number' && Number.isFinite(body.offsetMs)) offsetMs = body.offsetMs
  } catch {
    // Offline or the API isn't up yet: show the real date rather than blocking the app.
  }
}

export function now(): Date {
  return new Date(Date.now() + offsetMs)
}

/** Milliseconds since the epoch on the app clock, for the places that did Date.now(). */
export function nowMs(): number {
  return Date.now() + offsetMs
}
