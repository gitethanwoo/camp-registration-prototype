# Wave 1 integration: one green suite on one stack, plus the reviewer's cross-slice fixes

- Plan Type: ExecPlan
- Status: In Progress
- Owner: Claude (wave 1 integrator)
- Started: 2026-09-24
- Completed:

> Maintain this file in accordance with `docs/PLANS.md`.

## Purpose / Big Picture

Wave 1 built four slices in parallel, each on its own database: family self-service (F1–F6), admittance programs (R2, F7, C6), groups and cohorts (R8, G1, G2), and staff customer service (C1, C2, C8, C9, C10, F8). The branch `wave1` merges all four with one combined `Wave1` EF migration. Each slice's Playwright spec passed on its own database, but on one shared stack five tests failed, because one slice's seed data changes what another slice's page shows. A reviewer also found money, auth, and capacity defects that only matter once the slices live together.

After this plan, a presenter can bring up one fresh stack (`docker compose down -v && docker compose up -d --build`), run `npx playwright test` twice in a row and see it green both times, and walk every persona's path without dead ends: Maria reaches her retreat application from the account menu and asks for a session transfer from a registration; Pastor Dave reaches his group tracker from the account menu; David Johnson, a co-owner, signs in and lands in the Johnson household instead of a new empty one.

## Progress

- [x] (2026-09-24 20:10Z) Read AGENTS.md, QUALITY.md, PLANS.md, the master plan, and the four completed slice plans. Diagnosed the five e2e failures from `test-results/`.
- [x] (2026-09-24 21:05Z) Family fixes: atomic finalize claim (1), idempotency key scoped to the order (2), installments covered by a balance payment (3), waitlist offers on F1 and no dead health action (4), sign-in through an adult member's email with cross-household uniqueness (5).
- [x] (2026-09-24 21:05Z) Admittance fixes: application stores the claimed pool and decline releases exactly that pool, with the Wave1 migration regenerated (6); reauthorize voids its new hold when it loses a race (7); the staff queue opens on the upcoming retreat, not the past one.
- [x] (2026-09-24 21:05Z) Staff-cx: concurrent transfer requests return 409 (8). Not reproducible on `wave1`: fc86843 already catches the unique-index violation and returns 409. Added a regression test (3 rounds of 10 concurrent requests from two signed-in clients) that passes.
- [x] (2026-09-24 21:05Z) Cross-slice links (9): F5 "Request transfer", account menu "Applications" and "My groups".
- [x] (2026-09-24 21:40Z) e2e: `workers: 1`, a global setup that resets the stack's database, and specs adjusted to the combined seed.
- [ ] Gates, API tests, two full e2e runs on a fresh stack, and a persona click-through at desktop and 390px.

## Surprises & Discoveries

- Observation: the C6 queue opened on the family slice's unpublished "Spring Marriage Retreat · Spring 2026", because `/api/admin/admittance/sessions` orders admittance sessions by start date and the page jumps to the first one.
  Evidence: `test-results/admittance-a-couple-applie-…/error-context.md` shows the breadcrumb "Spring Marriage Retreat · Spring 2026" and "2 of 80 couples confirmed". Both admittance failures (the demo path and the Marcus read-only check) come from this.
- Observation: after the merge, the family spec's `Register` button no longer existed. Day Camp has a second session (staff-cx's "June week 2"), so the program page labels each button `Register for <session>`. The family spec also registers Avery for June week before the staff-cx spec runs, and the staff-cx spec asserted Avery started unregistered.
  Evidence: `test-results/family-…/error-context.md`; with one worker in file order, staff-cx's precondition failed until it reused the existing registration.
- Observation: F5's "Request transfer" button already rendered after the merge, but it linked to `/family/registrations/WS-XXXX/transfer`. The staff-cx route is `/family/registrations/:id/transfer` with a numeric registration id, so the page loaded with `id = NaN`.
  Evidence: `web/src/features/family/RegistrationDetail.vue` resolves the route by path shape only; `web/src/features/staff-cx/routes.ts` maps `:id` through `Number(...)`.

## Decision Log

- Decision: The Playwright suite resets the docker stack's database in a global setup (stop the api container, drop `CampRegistration`, start the api, which migrates and seeds) instead of making every spec idempotent against used data.
  Rationale: the demo paths are one-time state changes on fixed WorkOS personas: Sam's first sign-in into an empty household, Maria's first Day Camp registration and retreat application, a discount code approved once, two duplicate accounts merged once. None of them can run twice on the same rows without either weakening the assertions or adding delete endpoints to the product. Resetting before the run keeps every assertion and makes `npx playwright test` rerunnable. It needs no app code. `E2E_RESET=0` skips it, and it is skipped whenever `E2E_BASE_URL` points somewhere other than the docker stack.
  Date/Author: 2026-09-24 / Claude
- Decision: Tests run with one worker, in file order (admittance, family, groups, smoke, staff-cx, then the phone project). Specs that share a persona (Maria appears in admittance, family, smoke, and staff-cx) assert what their own step produced rather than global counts that another spec's step changes.
  Rationale: several specs change Maria's household; with parallel workers the order was random.
  Date/Author: 2026-09-24 / Claude

## Verification

- (2026-09-24) Baseline on `wave1` before changes: `npm run test:api` 86/86; `npx playwright test` on a fresh stack 18 passed, 5 failed.
- (2026-09-24) New API tests for findings 1, 2, 6 and 7 fail against the HEAD versions of `BalancePaymentService.cs` and `AdmittanceService.cs` (3 of 3 runs) and pass with the fixes. The auth, F1 and F6 tests cover code that didn't exist before. `npm run test:api` 96/96; `npm run lint` and `npm run typecheck` pass.
- (2026-09-24) After `docker compose down -v && docker compose up -d --build`: `npx playwright test` 23 passed, then again straight after, 23 passed. `e2e/staff-cx.spec.ts` alone 5 passed (registers Avery itself); `e2e/family.spec.ts` alone 5 passed.

## Outcomes & Retrospective

Not started.

## Context and Orientation

The repository is a WinShape camp registration prototype: an ASP.NET Core minimal API in `api/Camp.Api` (EF Core, SQL Server), a Vue 3 app in `web/`, and Playwright specs in `e2e/`. `AGENTS.md` explains the slice seams. Each wave-1 slice owns `api/Camp.Api/Features/<Slice>/`, `web/src/features/<slice>/`, `api/Camp.Api.Tests/<Slice>Tests.cs`, and `e2e/<slice>.spec.ts`. As the integrator, this plan edits across slices and a few shared files; each such edit is listed here.

The docker stack (`docker-compose.yml`) runs SQL Server (`db`, port 14333), the WorkOS emulator (`workos`, port 4100) that signs in the personas in `infra/workos/workos-emulate.config.yaml`, the API (`api`, port 5080), and nginx serving the built web app with an `/api` proxy (`web`, port 5173). The API migrates and seeds its database on startup (`api/Camp.Api/Program.cs`): the core seed in `api/Camp.Api/Data/Seed.cs`, then each slice's `ISeedModule`.

Files this plan changes and why:

- `api/Camp.Api/Features/Family/BalancePaymentService.cs`: finalize claims the pending payment with a conditional update; the idempotency key only replays within the order in the URL.
- `api/Camp.Api/Features/Family/FamilyRegistrationEndpoints.cs`, `web/src/features/family/Payments.vue`: installments settled by a balance payment say "Covered by payment on <date>".
- `api/Camp.Api/Features/Family/FamilyEndpoints.cs`, `FamilyReadModel.cs`, `web/src/features/family/FamilyHome.vue`: waitlist entries and held offers on F1; an embedded health item that is incomplete has no action button; adult emails can't take another household's sign-in email.
- `api/Camp.Api/Auth/AuthEndpoints.cs` (critical): sign-in finds the household through an adult member with access.
- `api/Camp.Api/Features/Admittance/*`: `AdmittanceApplication.PoolId`, decline releases that pool, reauthorize voids on a lost race, sessions ordered upcoming first. `api/Camp.Api/Data/Migrations/*Wave1*` regenerated.
- `api/Camp.Api/Features/StaffCx/TransferEndpoints.cs`: concurrent transfer requests.
- `web/src/layouts/GuestLayout.vue`, `web/src/features/family/RegistrationDetail.vue`: cross-slice links.
- `playwright.config.ts`, `e2e/global-setup.ts`, `e2e/*.spec.ts`: one worker, reset, combined-seed selectors.

## Plan of Work

Milestone 1 fixes the API findings with an integration test for each in the owning slice's test file. Milestone 2 regenerates the `Wave1` migration after adding `PoolId`. Milestone 3 adds the cross-slice links and the F1/F6 display fixes. Milestone 4 makes the e2e suite deterministic. Milestone 5 runs every gate, both e2e runs, and a manual persona walkthrough with screenshots.

## Concrete Steps

From `/Users/ethanwoo/dev/camp-registration-prototype`:

    rm api/Camp.Api/Data/Migrations/*_Wave1.cs api/Camp.Api/Data/Migrations/*_Wave1.Designer.cs
    git show main:api/Camp.Api/Data/Migrations/CampDbContextModelSnapshot.cs > api/Camp.Api/Data/Migrations/CampDbContextModelSnapshot.cs
    scripts/dotnet.sh ef migrations add Wave1 --project api/Camp.Api --output-dir Data/Migrations
    npm run test:api
    docker compose down -v && docker compose up -d --build
    npx playwright test && npx playwright test

## Validation and Acceptance

- [ ] `npm run verify:precommit` (pass)
- [ ] `npm run test:api` (pass)
- [ ] `npx playwright test` on a fresh stack, twice in a row (pass)
- [ ] Persona click-through at 1440 and 390 wide (pass)

## Idempotence and Recovery

The migration is regenerated from `main`'s snapshot, so rerunning the steps produces the same `Wave1` migration. The wave is unreleased; no database outside local stacks has it. To recover demo data, `docker compose down -v && docker compose up -d --build`, or run the e2e suite, whose global setup drops and reseeds the database.

## Artifacts and Notes

None yet.

## Interfaces and Dependencies

New column `AdmittanceApplications.PoolId` (nullable int, foreign key to `CapacityPools`). No other schema change. `GET /api/family/overview` gains `waitlist`. `GET /api/family/registrations/{code}/payments` installments gain `coveredOn`.
