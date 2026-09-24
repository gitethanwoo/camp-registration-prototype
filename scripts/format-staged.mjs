#!/usr/bin/env node
// Format staged web/script files with oxfmt and re-stage them. C# is checked, not rewritten, by check:api.
import { execFileSync } from 'node:child_process'

const staged = execFileSync('git', ['diff', '--cached', '--name-only', '--diff-filter=ACMR'], { encoding: 'utf8' })
  .split('\n')
  .filter((p) => /\.(vue|ts|js|mjs|json|css|md|ya?ml)$/.test(p))
  .filter((p) => !/package-lock\.json$|^docs\//.test(p))
if (staged.length) {
  execFileSync('npx', ['oxfmt', '--write', ...staged], { stdio: 'inherit' })
  execFileSync('git', ['add', ...staged], { stdio: 'inherit' })
}
