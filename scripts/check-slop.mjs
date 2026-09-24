#!/usr/bin/env node
// Anti-slop checks that oxlint can't express. Scans web/src (Vue/TS) and e2e.
//   - Pages use the shadcn primitives in components/ui, never raw form/table elements.
//   - No native browser dialogs (alert/confirm/prompt); use Dialog/AlertDialog/toast.
//   - No filler UI copy.
//   - No sleeps in e2e; wait on behavior.
//   - One clock: "now" comes from TimeProvider (api) and now() in web/src/lib/clock.ts (web), never
//     DateTime.UtcNow/Now/Today, DateTimeOffset.UtcNow/Now, new Date() or Date.now().
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'

const RAW_ELEMENTS = ['button', 'table', 'select', 'input', 'textarea', 'dialog']
const SLOP_COPY = [
  /\bseamless(ly)?\b/i,
  /\beffortless(ly)?\b/i,
  /\bleverag(e|ing)\b/i,
  /\bunlock\b/i,
  /\belevate\b/i,
  /\brobust\b/i,
  /\bdelve\b/i,
  /\bin today's\b/i,
  /\bLorem ipsum\b/i,
  /Oops!/,
  /Something went wrong/i,
  /🚀|✨|🎉/u,
]
const failures = []

function walk(dir) {
  let out = []
  for (const name of readdirSync(dir)) {
    const p = join(dir, name)
    if (name === 'node_modules' || name === 'dist') continue
    if (statSync(p).isDirectory()) out = out.concat(walk(p))
    else out.push(p)
  }
  return out
}

function lineOf(text, index) {
  return text.slice(0, index).split('\n').length
}

const webFiles = walk('web/src').filter((p) => /\.(vue|ts)$/.test(p))
for (const path of webFiles) {
  const rel = relative('.', path)
  if (rel.startsWith('web/src/components/ui/')) continue
  const text = readFileSync(path, 'utf8')

  if (path.endsWith('.vue')) {
    const template = /<template>([\s\S]*)<\/template>/.exec(text)?.[1] ?? ''
    const offset = text.indexOf(template)
    for (const el of RAW_ELEMENTS) {
      const re = new RegExp(`<${el}[\\s>/]`, 'g')
      for (const m of template.matchAll(re))
        failures.push(`${rel}:${lineOf(text, offset + m.index)} raw <${el}>; use the components/ui primitive.`)
    }
  }
  for (const m of text.matchAll(/\b(window\.)?(alert|confirm|prompt)\(/g))
    failures.push(`${rel}:${lineOf(text, m.index)} native ${m[2]}(); use Dialog/AlertDialog or a toast.`)
  for (const re of SLOP_COPY) {
    const m = re.exec(text)
    if (m) failures.push(`${rel}:${lineOf(text, m.index)} filler copy "${m[0]}"; say the concrete thing.`)
  }
}

let e2eFiles = []
try {
  e2eFiles = walk('e2e').filter((p) => p.endsWith('.ts'))
} catch {
  // no e2e suite yet
}
for (const path of e2eFiles) {
  const text = readFileSync(path, 'utf8')
  for (const m of text.matchAll(/waitForTimeout\(/g)) {
    const line = text.split('\n')[lineOf(text, m.index) - 1] ?? ''
    if (!line.includes('governance: allow-waitForTimeout'))
      failures.push(`${path}:${lineOf(text, m.index)} waitForTimeout; wait on a locator or response instead.`)
  }
}

// One clock. The demo runs in March 2028; a raw "now" read would stamp live actions with the real date.
const CLOCK_FILES = new Set([
  'web/src/lib/clock.ts',
  'api/Camp.Api/Features/Polish/DemoClock.cs',
  'api/Camp.Api/Features/Polish/ClockEndpoints.cs',
])
const WEB_NOW = [/\bnew Date\(\s*\)/g, /\bDate\.now\(\)/g]
const API_NOW = [/\bDateTime\.(UtcNow|Now|Today)\b/g, /\bDateTimeOffset\.(UtcNow|Now)\b/g, /\bTimeProvider\.System\b/g]
function scanNow(files, patterns, hint) {
  for (const path of files) {
    const rel = relative('.', path)
    if (CLOCK_FILES.has(rel)) continue
    const text = readFileSync(path, 'utf8')
    for (const re of patterns)
      for (const m of text.matchAll(re)) {
        const line = text.split('\n')[lineOf(text, m.index) - 1] ?? ''
        if (/^\s*(\/\/|\*|\/\*|\/\/\/)/.test(line) || line.includes('clock: allow-real-time')) continue
        failures.push(`${rel}:${lineOf(text, m.index)} ${m[0]} reads the real time; ${hint}.`)
      }
  }
}
scanNow(webFiles, WEB_NOW, 'use now() or nowMs() from @/lib/clock')
const apiFiles = walk('api').filter(
  (p) => p.endsWith('.cs') && !/\/(bin|obj|Migrations)\//.test(p) && !p.includes('Camp.Api.Tests'),
)
scanNow(apiFiles, API_NOW, 'inject TimeProvider and use clock.UtcNow() or clock.Today()')

if (failures.length) {
  for (const f of failures) console.error(`[slop] ${f}`)
  process.exit(1)
}
console.log(`[slop] ok (${webFiles.length + e2eFiles.length + apiFiles.length} files)`)
