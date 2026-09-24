# Agent guide

A WinShape camp registration prototype: a .NET 10 minimal API, Vue 3 with shadcn-vue, and SQL Server. The README covers the product. This file covers how to change the code.

## Rules

1. **Plan before code.** Work over 50 changed code lines needs a plan in `docs/exec-plans/active/`, following `docs/PLANS.md`. Over 250 lines, or any critical file, needs an ExecPlan (see `docs/exec-plans/index.md`). Keep `## Progress` current, with dated entries. Finish with `node scripts/complete-exec-plan.mjs <slug>`.
2. **Gates run on every commit** (`docs/QUALITY.md`). Never use `--no-verify`. If a gate blocks you, fix the code.
3. **Stay inside your slice.** Features plug in without editing shared files. Use these seams:
   - API endpoints: add `api/Camp.Api/Features/<Slice>/<Slice>Endpoints.cs` implementing `IEndpointModule`. It's discovered by assembly scan.
   - EF mapping: add an `IEntityTypeConfiguration<T>` in your slice. `ApplyConfigurationsFromAssembly` picks it up. New entities go in your slice folder, plus a `DbSet` only if you need one; prefer `db.Set<T>()`.
   - Seed data: add an `ISeedModule` in your slice (it runs after the core seed, ordered by `Order`).
   - Web routes: add `web/src/features/<slice>/routes.ts` exporting `routes: RouteRecordRaw[]`. Admin pages set `meta.nav` to appear in the sidebar.
   - Audit: inject `IAuditLog` and call `audit.Record(...)` for every staff mutation, before `SaveChanges`.
4. **Migrations:**
   - A slice that adds entities also adds one migration named `<Slice>` (`scripts/dotnet.sh tool restore` once, then `scripts/dotnet.sh ef migrations add <Slice> --project api/Camp.Api --output-dir Data/Migrations`), so the app and the tests run on your branch.
   - At merge the integrator deletes the wave's slice migrations and generates a single combined migration. That's how parallel snapshot edits get resolved, so don't hand-merge `CampDbContextModelSnapshot.cs`.
5. **Auth:**
   - Guests sign in through WorkOS AuthKit. Locally that's the emulator; personas are in `infra/workos/workos-emulate.config.yaml` and `e2e/fixtures.ts`.
   - Staff are WorkOS users in the `WinShape Staff` org with roles `cet`, `finance`, and `host`.
   - Inject `CurrentUser` (guests) or `StaffUser` (staff), and protect endpoints with the policies in `Auth/Identity.cs`, e.g. `.RequireAuthorization(Policies.Finance)`. Never read the household id from the request.
   - API tests sign in with `factory.SignInAsFamily(...)` or `factory.SignInAsStaff("finance")`. e2e tests use `signInAs(page, 'marcus')`.
6. **UI:** compose `web/src/components/ui` primitives. Money comes from the server as cents; format with `money()`. Copy is plain and specific (see Anti-slop in QUALITY.md).
7. **Tests:**
   - Every slice adds API integration tests in `Camp.Api.Tests/<Slice>Tests.cs`.
   - Every slice adds one Playwright spec in `e2e/<slice>.spec.ts` covering its demo path.

## Product sources

- `docs/product/screen-specs.md`: screen cards with IDs (R2, C6, F7…), the canonical dataset, the status vocabulary, and the seven-pass self-review.
- The concept PNGs, one per screen ID, are in `/Users/ethanwoo/dev/easyllama-speed-extension/WinShape screen catalogue/<area>/`. Match layout and copy, but the PRD wins when a concept disagrees with it.

## Commands

```bash
docker compose up -d db workos    # SQL :14333, WorkOS emulator :4100
scripts/dotnet.sh run --project api/Camp.Api    # API :5080
npm --prefix web run dev          # Vite :5173
npm run verify:precommit          # every gate the hook runs
npm run test:api && npm run test:e2e
```
