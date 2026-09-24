# Wave 3 integration: forms, access and polish on one stack

- Plan Type: ExecPlan
- Status: In progress
- Owner: Claude (wave 3 integrator)
- Started: 2026-09-24

> Maintain this file in accordance with `docs/PLANS.md`.

## Purpose / Big Picture

Wave 3 built three branches in parallel: the K6 registration form builder with live forms in the wizard, F5 and C3 (`slice/forms`, personas Alex, Maria, Diane); K11 staff users and K9 health settings with server-side health gating (`slice/access`, Alex); and polish (`slice/polish`): a demo clock pinned to March 2, 2028, a waiver on every published program, a role guard on staff routes, moved campers on F1/F4, and David Johnson as a sign-in persona. The branch `wave3` merges all three with one combined `Wave3` EF migration and closes what only shows up once they share a database: forms' staff answers ignore access's health rules, a form author can approve their own edits, forms' "Day Camp · Rome" collides with the family spec's selector, admittance approvals skip the retreat's new waiver, and the staff No access page sits in the guest shell.

After this plan, a presenter can bring up one fresh stack (`docker compose down -v && docker compose up -d --build`), run `npx playwright test` twice in a row and see it green both times, and walk every persona with live actions dated in the 2028 demo season.

## Progress

- [x] (2026-09-24) Worktree HEADs match their branches: forms 867913f, access 37873e1, polish 5c3b717. Nothing to bring over.
- [x] (2026-09-24) Branch `wave3` from `main` (ab49826); merged `slice/polish` (no conflicts), then `slice/forms` (using-directive conflicts in `Checkout.cs` and `GuestEndpoints.cs`, both sides kept).
- [x] (2026-09-24) Merged `slice/access`: using-directive conflict in `ProgramSetupEndpoints.cs` (both kept); `web/src/pages/admin/RegistrationDetail.vue` Answers card takes forms' `FormAnswers` and drops the Allergies and Dietary rows, as access's review fix did.
- [x] (2026-09-24) Deleted the `Forms` and `Access` migrations (polish had none), restored main's snapshot, generated `Wave3` (FormVersions, FormQuestions, FormAnswers, ProgramHealthSettings, StaffMembers, StaffSyncRuns). Neither slice migration had `migrationBuilder.Sql`. A fresh stack applies Initial, Wave1, Wave2, Wave3 and seeds with no exceptions; `/api/clock` reads 2028-03-02T15:00Z.
- [x] (2026-09-24) Clock sweep: after the merges, no `DateTime.UtcNow/Now/Today`, `DateTimeOffset.UtcNow/Now`, `new Date()` or `Date.now()` remains in `api/` or `web/src` outside the clock files (forms and access already injected `TimeProvider`; polish swept the rest). `scripts/check-slop.mjs` (run by `npm run lint`) now fails on any of them, and on `TimeProvider.System`, outside `web/src/lib/clock.ts`, `Features/Polish/DemoClock.cs` and `Features/Polish/ClockEndpoints.cs`. e2e specs may still use `Date.now()` for unique names; the test project is exempt (its fixed clock is `ApiFactory.Now`).
- [ ] Cross-slice fixes (health gating on staff answers, form four-eyes, Rome name and family spec, admittance spec locator, admittance waiver, staff No access shell).
- [ ] Gates, test:api, two full e2e runs on a fresh stack.
- [ ] Persona click-through at 1440×1000 and 390×844.

## Surprises & Discoveries

(none yet)

## Decision Log

(none yet)

## Outcomes & Retrospective

(pending)

## Context and Orientation

The repository is a WinShape camp registration prototype: an ASP.NET Core minimal API in `api/Camp.Api` (EF Core, SQL Server), a Vue 3 app in `web/`, and Playwright specs in `e2e/`. `AGENTS.md` explains the slice seams. The docker stack runs SQL Server (:14333), the WorkOS emulator (:4100), the API (:5080) and nginx serving the web app (:5173). The API migrates and seeds on startup.

## Plan of Work

Milestone 1 merges the slices (polish first, since it sweeps every clock read) and regenerates the migration. Milestone 2 routes any remaining "now" reads through the clock and adds a lint check. Milestone 3 fixes the cross-slice items, each with a test. Milestone 4 runs the gates and two full e2e runs on a fresh stack. Milestone 5 is a persona click-through.

## Concrete Steps

From `/Users/ethanwoo/dev/camp-registration-prototype`:

    git checkout -b wave3 main
    git merge --no-ff slice/polish; git merge --no-ff slice/forms; git merge --no-ff slice/access
    git rm api/Camp.Api/Data/Migrations/*_{Forms,Access}.*
    git show main:api/Camp.Api/Data/Migrations/CampDbContextModelSnapshot.cs > api/Camp.Api/Data/Migrations/CampDbContextModelSnapshot.cs
    scripts/dotnet.sh ef migrations add Wave3 --project api/Camp.Api --output-dir Data/Migrations
    npm run verify:precommit && npm run test:api
    docker compose down -v && docker compose up -d --build
    npx playwright test && npx playwright test

## Validation and Acceptance

- [ ] `npm run verify:precommit`
- [ ] `npm run test:api`
- [ ] `npx playwright test` on a fresh stack, twice in a row
- [ ] Persona click-through at 1440 and 390 wide

## Idempotence and Recovery

The migration is regenerated from `main`'s snapshot, so rerunning the steps produces the same `Wave3` migration. To recover demo data, `docker compose down -v && docker compose up -d --build`, or run the e2e suite, whose global setup drops and reseeds the database.

## Artifacts and Notes

(pending)

## Interfaces and Dependencies

(pending)
