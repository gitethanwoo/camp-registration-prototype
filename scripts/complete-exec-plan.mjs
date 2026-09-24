#!/usr/bin/env node
// Move a finished plan from active/ to completed/ and stamp it.
//   node scripts/complete-exec-plan.mjs <slug> [Completed|Abandoned|Shelved]
import { existsSync, readFileSync, renameSync, writeFileSync } from 'node:fs'

const [slug, outcome = 'Completed'] = process.argv.slice(2)
if (!slug) {
  console.error('usage: node scripts/complete-exec-plan.mjs <slug> [Completed|Abandoned|Shelved]')
  process.exit(1)
}
const name = slug.endsWith('.md') ? slug : `${slug}.md`
const from = `docs/exec-plans/active/${name}`
const to = `docs/exec-plans/completed/${name}`
if (!existsSync(from)) {
  console.error(`no active plan at ${from}`)
  process.exit(1)
}
const today = new Date().toISOString().slice(0, 10)
const content = readFileSync(from, 'utf8')
  .replace(/^- Status:.*$/m, `- Status: ${outcome}`)
  .replace(/^- Completed:.*$/m, `- Completed: ${today}`)
writeFileSync(from, content)
renameSync(from, to)
console.log(`${from} -> ${to} (${outcome})`)
