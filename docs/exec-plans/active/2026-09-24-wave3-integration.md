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
- [x] (2026-09-24) Health gating on C3: K6 questions carry a `Health` flag (the seeded medication pair has it); `GET /api/admin/forms/registrations/{id}/answers` applies `HealthAccessRules.Refusal` and either returns health answers with a `health.viewed` audit row or leaves them out with `healthWithheld` (the reason). The raw `answers` map is gone from `GET /api/admin/registrations/{id}`. Test: `FormsTests.Health_answers_on_C3_follow_the_health_access_rules_and_each_view_is_audited`.
- [x] (2026-09-24) Form four-eyes: saving a draft records the admin in `FormVersion.EditedBy`/`EditedByEmails`; `FormViews.ApprovalBlock` refuses anyone on those lists. K6 shows "Edited by". Test: `FormsTests.An_admin_who_edited_someone_elses_draft_cannot_approve_it`.
- [x] (2026-09-24) "Day Camp · Rome" is now "Rome Day Camp" (slug unchanged) and `e2e/family.spec.ts` matches `View details for Day Camp · Atlanta`. Forms' seeded history moved from 2026 to 2027-08..2028-02 like polish's.
- [x] (2026-09-24) `e2e/admittance.spec.ts` reads the queue subtitle from the heading's own block, not the breadcrumb.
- [x] (2026-09-24) Admittance waiver: R2's review step shows the program's waivers, an "I agree for us both" box and a signature; submit refuses (400, nothing authorized) without them and stores which versions were accepted; approval writes the `WaiverAcceptance` rows. Test: `AdmittanceTests` (refusal in `Missing_answers_are_refused...`, the acceptance in `Approving_captures_once...`).
- [x] (2026-09-24) Staff No access: role mismatches (and console staff opening a family page) go to `/admin/no-access`, inside `AdminLayout`; guests and hosts still get `/no-access` in the public shell. `e2e/polish.spec.ts` checks the sidebar is there.
- [x] (2026-09-24) `npm run test:api` 212/212. With each fix reverted, the three new or extended API tests fail (3 of 3).
- [ ] Gates, test:api, two full e2e runs on a fresh stack.
- [ ] Persona click-through at 1440×1000 and 390×844.

## Surprises & Discoveries

(none yet)

## Decision Log

- Decision: Health answers are marked per question (`FormQuestion.Health`, a "Health question" checkbox in K6), not guessed from keys or labels. The seed marks the medication pair on Atlanta, Overnight and Rome.
  Rationale: admins add questions in K6; only the author knows which ones are health information. A flag copies with every new version, so a draft can't drop it silently (it is part of the version signature too).
  Date/Author: 2026-09-24 / Claude
- Decision: On C3, withheld health answers produce no `health.view_denied` row; shown ones write `health.viewed` each time the answers load.
  Rationale: opening a registration isn't a request for health data, and a denied row on every C3 visit by CET would bury the real denials from "View health form". A view that returns health answers is a view, the same as access's endpoint.
  Date/Author: 2026-09-24 / Claude
- Decision: `GET /api/admin/registrations/{id}` no longer returns the raw `answers` map.
  Rationale: forms keeps writing every answer (medication included) to `Registration.AnswersJson`, so the old map would hand the medication answers to every staff role. The C3 card already reads the gated forms endpoint; nothing else used the map.
  Date/Author: 2026-09-24 / Claude
- Decision: Editors are tracked as two "; "-joined columns on `FormVersion` (names and emails), matched like the existing author and sender checks.
  Rationale: the approval rule compares both email and actor name because dev sign-in shares emails per role; a separate table would be more than a prototype needs.
  Date/Author: 2026-09-24 / Claude
- Decision: Rename forms' program to "Rome Day Camp" and also tighten the family spec to `Day Camp · Atlanta`.
  Rationale: either alone fixes the spec; both keep a future "Day Camp …" program from breaking it again, and "Rome Day Camp" reads naturally on the program list.
  Date/Author: 2026-09-24 / Claude
- Decision: The couple signs the waiver on R2's review step (collected at submit), not on F5 after approval. Applications submitted before the retreat had a waiver (the seeded queue) still create a registration with no signature, which F5 shows as "to sign".
  Rationale: it matches the registration wizard (sign before paying), the card hold already happens on that step, and it is three columns on the application plus one loop at capture. Seeded applications aren't given made-up signatures.
  Date/Author: 2026-09-24 / Claude
- Decision: The staff No access page is the same component at `/admin/no-access` inside `AdminLayout`; `/no-access` stays for guests and host coordinators (whose shell is the host portal).
  Rationale: a staff member keeps the sidebar and account menu, and the page needs no copy changes.
  Date/Author: 2026-09-24 / Claude

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
