#!/usr/bin/env node
// Change governance, ported from servant-io/faithbase.
//   node scripts/check-governance.mjs            diff vs origin/main (pre-push, CI)
//   node scripts/check-governance.mjs --cached   staged diff (pre-commit)
// Rules live in docs/exec-plans/index.md.
import { execSync } from 'node:child_process'
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { extname, join } from 'node:path'

const MICROPLAN_THRESHOLD = 50
const EXECPLAN_THRESHOLD = 250
const MAX_FILE_LINES = 1200
const ACTIVE = ['in progress', 'in review']
const COMPLETED = ['completed', 'done', 'abandoned', 'shelved']
const CODE_EXT = new Set([
  '.cs',
  '.ts',
  '.vue',
  '.js',
  '.mjs',
  '.css',
  '.sql',
  '.yml',
  '.yaml',
  '.json',
  '.csproj',
  '.props',
])
// Money, capacity, auth and schema: any change here needs an ExecPlan.
const CRITICAL = [
  /^api\/Camp\.Api\/Data\/(Entities|CampDbContext)\.cs$/,
  /^api\/Camp\.Api\/Features\/(Checkout|Pricing|PaymentGateway)\.cs$/,
  /^api\/Camp\.Api\/Auth\//,
  /^api\/Camp\.Api\/Program\.cs$/,
]
const cached = process.argv.includes('--cached')
const failures = []

function run(cmd) {
  try {
    return execSync(cmd, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim()
  } catch {
    return null
  }
}

function range() {
  if (cached) return ''
  const base = process.env.GOVERNANCE_BASE_REF ?? 'origin/main'
  return run(`git merge-base HEAD ${base}`) ?? run('git rev-parse --verify HEAD~1') ?? ''
}

function isCode(path) {
  if (path.startsWith('docs/') || /(^|\/)Migrations\//.test(path)) return false
  if (path.endsWith('package-lock.json') || path.endsWith('.Designer.cs')) return false
  return CODE_EXT.has(extname(path))
}

function isTest(path) {
  return /(^|\/)(Camp\.Api\.Tests|e2e)\//.test(path) || path.endsWith('.test.ts')
}

function status(content) {
  const m = /^- Status:\s*(.+)$/im.exec(content)
  if (!m?.[1]) return null
  const primary = m[1].split('(')[0].trim().toLowerCase()
  if (COMPLETED.some((k) => primary.includes(k))) return 'completed'
  if (ACTIVE.some((k) => primary.includes(k))) return 'active'
  return 'unknown'
}

// Lifecycle: every plan's status must match its directory, whatever the diff.
for (const dir of ['active', 'completed']) {
  const root = join('docs/exec-plans', dir)
  if (!existsSync(root)) continue
  for (const name of readdirSync(root)) {
    if (!name.endsWith('.md') || name.startsWith('_')) continue
    const path = join(root, name)
    const content = readFileSync(path, 'utf8')
    const bucket = status(content)
    if (bucket !== dir) failures.push(`${path}: Status "${bucket ?? 'missing'}" does not belong in ${dir}/.`)
    if (dir === 'active' && !/## Progress[\s\S]*?\d{4}-\d{2}-\d{2}/.test(content))
      failures.push(`${path}: active plans need a dated entry under ## Progress.`)
    if (dir === 'completed' && /^- \[ \]/m.test(content))
      failures.push(`${path}: completed plan has unchecked validation items.`)
  }
}

const r = range()
const diffFlag = cached ? '--cached' : ''
const numstat = run(`git diff ${diffFlag} --numstat --diff-filter=ACMR ${r}`) ?? ''
const files = numstat
  .split('\n')
  .filter(Boolean)
  .map((line) => {
    const [added, deleted, path] = line.split('\t')
    return { path: path ?? '', lines: (Number(added) || 0) + (Number(deleted) || 0) }
  })

const code = files.filter((f) => isCode(f.path))
const changed = code.reduce((sum, f) => sum + f.lines, 0)
const critical = code.filter((f) => CRITICAL.some((re) => re.test(f.path))).map((f) => f.path)

for (const f of code) {
  if (isTest(f.path) || !existsSync(f.path)) continue
  const n = readFileSync(f.path, 'utf8').split('\n').length
  if (n > MAX_FILE_LINES) failures.push(`${f.path}: ${n} lines (cap ${MAX_FILE_LINES}). Split it.`)
}

const plans = files
  .map((f) => f.path)
  .filter((p) => /^docs\/exec-plans\/(active|completed)\/[^_].*\.md$/.test(p) && existsSync(p))
  .map((p) => readFileSync(p, 'utf8'))
const hasExec = plans.some((c) => /Plan Type:\s*ExecPlan/i.test(c))
const hasMicro = hasExec || plans.some((c) => /Plan Type:\s*MicroPlan/i.test(c))

if ((changed > EXECPLAN_THRESHOLD || critical.length) && !hasExec) {
  const why = critical.length ? `critical files touched (${critical.join(', ')})` : `${changed} changed code lines`
  failures.push(`ExecPlan required: ${why}. Add or update a plan in docs/exec-plans with "Plan Type: ExecPlan".`)
} else if (changed > MICROPLAN_THRESHOLD && !hasMicro) {
  failures.push(`MicroPlan required: ${changed} changed code lines. Add or update a plan in docs/exec-plans.`)
}

if (failures.length) {
  for (const f of failures) console.error(`[governance] FAIL: ${f}`)
  process.exit(1)
}
console.log(`[governance] ok (${changed} code lines, ${plans.length} plan file(s) in diff)`)
