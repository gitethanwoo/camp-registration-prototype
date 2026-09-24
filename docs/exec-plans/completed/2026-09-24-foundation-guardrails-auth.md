# Foundation: quality gates, WorkOS sign-in, and slice seams

- Plan Type: ExecPlan
- Status: Completed
- Owner: Claude (orchestrator)
- Started: 2026-09-24
- Completed: 2026-09-24

> Maintain this file in accordance with `docs/PLANS.md`.

## Purpose / Big Picture

Before several agents build features in parallel, the repository has to protect itself and give each agent a place to plug in. After this change, every commit runs formatting, lint, anti-slop checks, strict typechecking, a warnings-as-errors C# build, and plan governance. Nobody can land a large change without a written plan. Sign-in is real: guests and staff sign in through WorkOS AuthKit, served locally by the official WorkOS emulator, instead of being hard-coded as Maria Johnson and Diane Carter. A new feature slice adds API endpoints, EF mappings, seed data, pages, and sidebar entries without editing `Program.cs`, `router.ts`, or the admin layout. That's what lets four agents work at once without merge conflicts.

To see it working, run `docker compose up -d --build` and open http://localhost:5173/family. You're sent to the emulator's sign-in page at http://localhost:4100. Enter `maria.johnson@example.com` and you land back on the family page as Maria. Open http://localhost:5173/admin as Maria and the page says "Staff only". Sign out, sign in as `diane.carter@winshape.example`, and the console opens with Diane's name in the sidebar footer.

## Progress

- [x] (2026-09-24) Installed oxlint and oxfmt at the repo root and configured `.oxlintrc.json` with anti-slop rules. Fixed the 20 existing lint findings.
- [x] (2026-09-24) Made vue-tsc strict (`strict`, `noUncheckedIndexedAccess`, `noImplicitOverride`, `noImplicitReturns`) with 0 errors.
- [x] (2026-09-24) C# build: `Directory.Build.props` with TreatWarningsAsErrors and AnalysisMode Recommended, plus `.editorconfig` severities. Fixed the culture and ordinal findings.
- [x] (2026-09-24) Formatted the whole tree once with oxfmt and `dotnet format`, so later diffs only show real changes.
- [x] (2026-09-24) Added `scripts/check-slop.mjs`. It caught a native `confirm`-named handler and two "Something went wrong." fallbacks; all three are fixed.
- [x] (2026-09-24) Ported plan governance from servant-io/faithbase as `scripts/check-governance.mjs` and `scripts/complete-exec-plan.mjs`, with `docs/PLANS.md`, the templates, and `docs/exec-plans/index.md`.
- [x] (2026-09-24) Added husky `pre-commit` (format staged, then `verify:precommit`, with a docs-only fast path) and `pre-push` (`verify:prepush`).
- [x] (2026-09-24) WorkOS emulator in docker-compose, seeded with six personas. Added `Auth/` in the API: cookie session, policies, `CurrentUser` and `StaffUser`, and a test-only dev-login.
- [x] (2026-09-24) Added slice seams: `IEndpointModule`, `ISeedModule`, `IAuditLog`, and `ApplyConfigurationsFromAssembly`. Admin mutations now audit under the signed-in staff name.
- [x] (2026-09-24) Web: `lib/session.ts`, route guards from `meta.auth`, a slice route registry via `import.meta.glob`, a sidebar built from `meta.nav`, a NoAccess page, and real user menus.
- [x] (2026-09-24) Added a Playwright harness (`playwright.config.ts`, `e2e/fixtures.ts` with `signInAs`) and a smoke spec: 5 tests passing through the real emulator.
- [x] (2026-09-24) Wrote AGENTS.md, CLAUDE.md, docs/QUALITY.md, and README sign-in docs.

## Surprises & Discoveries

- Observation: `exactOptionalPropertyTypes` and `noPropertyAccessFromIndexSignature` produce hundreds of errors inside the vendored shadcn-vue and reka-ui components.
  Evidence: vue-tsc reported errors only under `web/src/components/ui/**` after enabling them. Both flags were dropped; see the Decision Log.
- Observation: `AnalysisMode=Recommended` flags every `ToLower()` inside EF LINQ (CA1862, CA1311, CA1304), and EF can't translate the suggested overloads.
  Evidence: those rules are disabled in `.editorconfig`, with the reason written next to them.
- Observation: the WorkOS emulator accepts any `client_id` string and puts the membership role straight into the access token.
  Evidence: a token decoded during the curl probe had `"org_id":"org_winshape_staff","role":"cet","roles":["cet"]`.
- Observation: the emulator's account picker is inside a collapsed `<details>`, so Playwright can't click it directly. Typing the email into the form is reliable and matches production AuthKit.

## Decision Log

- Decision: Call AuthKit over plain HTTP (`Auth/WorkOs.cs`, two requests) instead of the WorkOS.net SDK.
  Rationale: we only need the authorize URL and the code exchange. The browser-facing URL (`localhost:4100`) and the server-to-server URL (`workos:4100` inside docker) differ, and the SDK has a single base URL. Two small HTTP calls are easier to read in an RFP code review.
  Date/Author: 2026-09-24 / Claude
- Decision: Issue our own cookie session after the code exchange, and read `role` from the WorkOS access token without verifying its signature.
  Rationale: the token arrives directly from WorkOS over the back channel in the same request, so it can't have been tampered with in a browser. The app never accepts WorkOS tokens from clients, so it doesn't need to pin the emulator's signing key. That keeps local setup to one docker service.
  Date/Author: 2026-09-24 / Claude
- Decision: Link guests to households by email. First sign-in creates an empty household.
  Rationale: this needs no schema change, and it matches how the seed identifies Maria. Production would store the WorkOS user id on Person. That's a one-column migration, deferred until a slice needs it.
  Date/Author: 2026-09-24 / Claude
- Decision: Staff are members of the WorkOS organization `org_winshape_staff`, with a role of `cet`, `finance`, `host`, or `admin`. The `Staff` policy admits cet, finance, and admin; host coordinators get their own policy.
  Rationale: this mirrors how WinShape would use WorkOS organizations with Entra SSO. Hosts are partner organizations, not console users.
  Date/Author: 2026-09-24 / Claude
- Decision: Drop `exactOptionalPropertyTypes` and `noPropertyAccessFromIndexSignature` from the strict flags.
  Rationale: they're incompatible with the vendored shadcn-vue components, which we don't want to fork.
  Date/Author: 2026-09-24 / Claude
- Decision: Slices commit their own migration; the integrator regenerates one combined migration per wave.
  Rationale: `MigrateAsync` runs on startup and in tests, so a slice without a migration can't run. Regenerating at merge avoids hand-merging the model snapshot.
  Date/Author: 2026-09-24 / Claude

## Verification

- `npm run lint`: oxlint 0 warnings; `[slop] ok (197 files)`.
- `npm run typecheck`: vue-tsc, 0 errors.
- `npm run check:api`: build 0 errors; `dotnet format --verify-no-changes` clean.
- `npm run test:api`: `Passed! - Failed: 0, Passed: 16` (11 existing and 5 new auth tests).
- `npm run test:e2e`: `5 passed` (desktop and phone) against `docker compose up -d --build`.
- `npm --prefix web run build`: built.

## Outcomes & Retrospective

The repo now enforces the same plan-and-progress discipline as servant-io/faithbase, and it has real sign-in. Feature slices have seams that avoid shared-file edits. The single remaining shared hotspot is the EF model snapshot, handled by per-wave migration regeneration. Next: the master plan `docs/exec-plans/active/2026-09-24-true-demo-a-b.md` runs wave 1.

## Context and Orientation

The API is `api/Camp.Api`, an ASP.NET Core minimal API on .NET 10 with EF Core and SQL Server. The web app is `web/`, Vue 3 with shadcn-vue. Sign-in lives in `api/Camp.Api/Auth/`:

- `WorkOs.cs` holds the options and the two-call HTTP client.
- `Identity.cs` holds claim names, policies, `CurrentUser`, and `StaffUser`.
- `AuthEndpoints.cs` serves `/api/auth/login`, `/callback`, `/logout`, `/me`, and `/dev-login` (Testing only).
- `AuthSetup.cs` holds the DI wiring.

The slice seams live in `api/Camp.Api/Infrastructure/Modules.cs`. On the web side, `web/src/lib/session.ts` holds session state, `web/src/lib/nav.ts` holds route meta types and sidebar groups, and `web/src/router.ts` has the guards and the slice route glob.

## Plan of Work

Done as listed in Progress.

## Validation and Acceptance

- [x] `npm run lint` (pass)
- [x] `npm run typecheck` (pass)
- [x] `npm run check:api` (pass)
- [x] `npm run test:api` (pass, 16/16)
- [x] `npm run test:e2e` against `docker compose up -d --build` (pass, 5/5)

## Idempotence and Recovery

The seed modules and household creation are idempotent. To reset demo data, run `docker compose down -v && docker compose up -d --build`. The emulator is in-memory and re-seeds from YAML on every start.

## Interfaces and Dependencies

- New npm dev dependencies at the root: husky, oxlint, oxfmt, and @playwright/test.
- New docker image: `ghcr.io/workos/emulate:0.13.1`.
- No new NuGet packages.
