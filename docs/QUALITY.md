# Quality gates

Every commit runs these through husky. Never bypass them with `--no-verify`. When a gate is wrong, fix the gate in the same PR and explain why.

| Gate | Command | Runs on |
| --- | --- | --- |
| Format (oxfmt, staged files) | `npm run format:staged` | pre-commit |
| Secrets (gitleaks, staged) | `npm run check:secrets:staged` | pre-commit, including docs-only commits |
| Plans and progress | `npm run check:governance:cached` | pre-commit |
| Lint: oxlint plus the anti-slop script | `npm run lint` | pre-commit |
| Typecheck: vue-tsc strict | `npm run typecheck` | pre-commit |
| C#: build with warnings as errors, then `dotnet format` verify | `npm run check:api` | pre-commit |
| API integration tests (needs SQL: `docker compose up -d db`) | `npm run test:api` | pre-push |
| Web production build | `npm --prefix web run build` | pre-push |
| Playwright e2e (needs the full stack) | `npm run test:e2e` | before merging a slice |

## TypeScript

- `.oxlintrc.json`: `correctness` and `suspicious` are errors.
- No `any`, no `@ts-ignore`, no non-null `!`.
- No `console` in app code, and no TODO/FIXME/HACK comments. Open an issue or write it in the plan.
- No nested ternaries, empty catch blocks, or param reassignment.
- Files cap at 600 lines; nesting caps at 4 levels.
- `tsconfig.app.json` adds `strict` and `noUncheckedIndexedAccess`.

## Anti-slop (`scripts/check-slop.mjs`)

- Pages compose the primitives in `web/src/components/ui`: no raw `<button>`, `<table>`, `<select>`, `<input>`, `<textarea>`, or `<dialog>`.
- No `alert`, `confirm`, or `prompt`. Use `AlertDialog`, `Dialog`, or a toast.
- No filler copy: "seamless", "effortless", "leverage", "unlock", "elevate", "robust", "Oops!", "Something went wrong.", or emoji in UI strings. Error messages say what happened and what to do next.
- e2e tests wait on behavior, never on the clock.

## C#

`Directory.Build.props` and `.editorconfig` set these:

- `TreatWarningsAsErrors`, `Nullable`, `AnalysisMode=Recommended`, and `EnforceCodeStyleInBuild`.
- Unused usings, variables, and parameters are errors.
- A small set of analyzers is off, each with a reason in `.editorconfig`: EF query translation, logging ceremony, and minimal-API internals.
- Culture-sensitive formatting uses `CultureInfo.InvariantCulture`.
