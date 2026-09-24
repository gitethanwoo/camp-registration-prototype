# Wave 2 integration: setup, finance, ops and host on one stack

- Plan Type: ExecPlan
- Status: Completed
- Owner: Claude (wave 2 integrator)
- Started: 2026-09-24
- Completed: 2026-09-24

> Maintain this file in accordance with `docs/PLANS.md`.

## Purpose / Big Picture

Wave 2 built four slices in parallel, each on its own database: program setup for Alex (K2–K5, K7, K12), finance for Marcus and Maria (FN1–FN4, O6, O7), operations for Diane (O1–O3, O5) and the host portal for Grace (H1–H3). The branch `wave2` merges all four with one combined `Wave2` EF migration and fixes what only shows up once they share a database and a sidebar.

After this plan, a presenter can bring up one fresh stack (`docker compose down -v && docker compose up -d --build`), run `npx playwright test` twice in a row and see it green both times, and walk each wave 2 persona (Alex, Marcus, Maria's scholarship, Diane's operations, Grace) plus the wave 1 personas without dead ends. A CET or Finance user no longer sees sidebar entries for pages their role can't open.

## Progress

- [x] (2026-09-24 07:15Z) Read AGENTS.md, the master plan's Wave 2 section, the wave 1 integration plan and the four completed slice plans.
- [x] (2026-09-24 07:22Z) Branch `wave2` from `main` (0d8c80c); merged `slice/setup`, `slice/finance`, `slice/ops`, `slice/host` with `--no-ff`. The only conflict was `CampDbContextModelSnapshot.cs` (took main's).
- [x] (2026-09-24 07:26Z) Deleted the four slice migrations, restored main's snapshot, generated `Wave2`, and carried setup's `TR_AuditEvents_Immutable` trigger SQL into its Up and Down. No other slice migration had `migrationBuilder.Sql`. A fresh stack migrates (Initial, Wave1, Wave2), seeds, and the trigger refuses `DELETE FROM AuditEvents`.
- [x] (2026-09-24 07:30Z) Setup and finance both seeded a program with slug `family-camp`; setup's is renamed "Family Weekend" (see Decision Log). 6 SetupTests failures fixed.
- [x] (2026-09-24 07:39Z) Cross-slice fixes: shared `money()` prints two decimals whenever there are cents; Family Camp published; admittance submit and group checkout refuse an unpublished program; transfers keep a scholarship and drop a session-scoped code; `PUT /api/admin/pools` is admin-only; the sidebar hides role-restricted entries.
- [x] (2026-09-24 07:39Z) `npm run test:api` 171/171.
- [x] (2026-09-24 07:50Z) Settlement seeding moved after setup's seed (FN2 showed setup's seeded payments as unsettled).
- [x] (2026-09-24 08:05Z) Gates pass; two full e2e runs on a fresh stack, 49 and 49.
- [x] (2026-09-24 08:15Z) Persona click-through at 1440×1000 and 390×844 (Alex, Marcus, Maria, Diane, Grace, Sam, Pastor Dave): 74 screenshots, no sideways scroll, no 5xx or page errors, sidebar entries match each role.

## Surprises & Discoveries

- Observation: `slice/setup` (Order 900) and `slice/finance` (Order 200) each seed a program with slug `family-camp`. Finance ran first, so setup's seed saw the slug and skipped its demo program; setup's backfill then marked finance's Family Camp as Draft. Six SetupTests failed ("Sequence contains no elements", "Expected PendingApproval, actual Draft").
  Evidence: `api/Camp.Api/Features/Setup/SetupSeed.cs` `SeedFamilyCamp` returns early when the slug exists; `api/Camp.Api/Features/Finance/FinanceSeed.cs` `ProgramSlug = "family-camp"`.
- Observation: the finance worktree (`~/dev/camp-wt/finance`) sits on 5eb385e, one commit past `slice/finance` (6a71b5a): a review-fix commit adding `web/src/features/finance/money.ts`, an FN2 resolve lock with a concurrency test, and phone/paging fixes. `slice/finance` doesn't have it, so `wave2` doesn't either. Merging the unlisted commit was refused in this session; it's left for the owner to decide (see Outcomes).
  Evidence: `git log slice/finance..5eb385e` lists one commit.
- Observation: finance builds the seeded Fiserv settlement batches from every payment that exists when its seed runs (Order 200). Setup's seed (Order 900) adds last summer's Family Weekend payments afterwards, so on a fresh combined stack FN2 would show those 14 seeded payments as "Captured, not yet settled". Settlement seeding moved to `FinanceSettlementSeed` (Order 1000).
  Evidence: `FinanceSeed.SeedSettlements` reads `PaymentOperations` with `CreatedAt < today`; `ReconciliationEndpoints.Unsettled` counts operations with no settlement line.
- Observation: a staff-approved transfer recomputed a percent code from the code's current terms and threw away anything else in `Registration.DiscountCents`. Finance's O7 awards live in that same column, so moving a camper with both a percent code and a scholarship dropped the scholarship. A K5 code scoped to one session also followed the camper to a session checkout would refuse it for.
  Evidence: `TransferService.DiscountAfterMove` before this plan; the new StaffCx test fails against it.
- Observation: admittance saves a draft only for a published program, but submit (which authorizes the card) and group checkout (which charges) didn't check again, so a program sent back to draft by K2 after a family started still took money.
  Evidence: the new AdmittanceTests and GroupsTests cases fail against the pre-fix services.

## Decision Log

- Decision: Rename setup's demo program to "Family Weekend" (slug `family-weekend`, waiver "Family Weekend Release and Waiver"), keep finance's as "Family Camp", and publish finance's Family Camp.
  Rationale: the two programs can't be one. Setup's is a returning program waiting on its first publish with no 2028 registrations; finance's already has 53 paid households, settled payments and scholarships. Of the two, finance's is the canonical FC (Part 3) that families are registered in, and the task asks that the finance scholarship program be published. An unpublished program with paid registrations would also show as Draft on K2 and be refused by setup's checkout guard. Setup's rename touches only its seed, its tests and its spec.
  Date/Author: 2026-09-24 / Claude
- Decision: Admittance submit returns 409 and group checkout returns 400 for an unpublished program, before any card call. Staff decisions on applications already submitted (approve, decline) and staff-approved transfers within a program are not blocked.
  Rationale: the same rule as `Checkout.cs`: no new money for a program that isn't published. Deciding an application a family already made, or moving a camper between sessions of a program they're already in, is operations on existing registrations. That matches setup's own rule that approval covers what a program is, not day-to-day changes.
  Date/Author: 2026-09-24 / Claude
- Decision: On a transfer, a scholarship award (the sum of the registration's `ScholarshipAwardLine`s) always carries over; the code part is recomputed as before, except that a code whose K5 rule is scoped to one session is dropped when the camper moves to another. Rule dates and use caps are not rechecked.
  Rationale: the award is a fixed amount granted to that camper. A session-only code is exactly what checkout refuses for the other session, so keeping it would give a price the family couldn't get by registering there. Dates and the cap were settled when the family bought.
  Date/Author: 2026-09-24 / Claude
- Decision: `PUT /api/admin/pools/{id}` requires `Policies.Admin`. `/admin/session` shows capacity read-only to CET and Finance; the C7 waitlist's "raise capacity" link shows only to admins, with "an administrator can raise capacity" for others.
  Rationale: no e2e spec or demo step has CET or Finance change capacity (checked `e2e/*.spec.ts`); K3 makes capacity an admin change and setup's own pool editor is admin-only already. The API test for audited staff changes now shows CET refused and Alex's change audited.
  Date/Author: 2026-09-24 / Claude
- Decision: `RouteMeta.roles` (in `web/src/lib/nav.ts`, with `cetRoles`, `financeRoles`, `adminRoles` matching `Policies.Cet/Finance/Admin`) hides a sidebar entry from other roles. Tagged: setup K2–K5 and K7 (admin), FN1–FN4 (finance, admin), transfer and duplicate queues (cet, admin). Pages still render their own 403 message; there is no route guard on roles.
  Rationale: the smallest change that makes the sidebar match the API. A redirect guard would hide setup's "Programs is for admins" message, which its spec checks, and nothing else needs one. Pages readable by every console role (audit log, ops, scholarships, applications, discounts) stay untagged. The host shell was already separate.
  Date/Author: 2026-09-24 / Claude
- Decision: Dates stay as they are. Seeds describe summer 2028; anything done live is stamped with the real clock (2026). No demo clock.
  Rationale: a demo clock is a cross-cutting change to every `DateTime.UtcNow` in every slice, and seeded rows would still disagree with anything the presenter does live, only in a different direction. Shifting seeded program years is off the table because the specs and concept screens name 2028 dates. What a presenter will see: host invoice "Paid Sep 24, 2026" on an invoice issued Feb 1, 2028; live check-ins dated 2026 beside seeded 2028 ones (ops already keeps its seeded check-ins at session start); audit rows in 2026. These read as "today" in the demo, which is what they are.
  Date/Author: 2026-09-24 / Claude
- Decision: Fiserv settlement batches are seeded by a new `FinanceSettlementSeed` (Order 1000) instead of at the end of `FinanceSeed` (200).
  Rationale: the batches are built from every payment that exists at seed time, so they have to run after every seed that adds payments. Setup's runs at 900.
  Date/Author: 2026-09-24 / Claude
- Decision: The shared `money()` prints whole dollars without decimals ("$475") and any amount with cents with two ("$427.50"). No per-slice formatter.
  Rationale: `maximumFractionDigits: 2, minimumFractionDigits: 0` printed "$25,627.5". `slice/finance` itself uses the shared helper; the duplicate `money.ts` exists only in the unmerged 5eb385e.
  Date/Author: 2026-09-24 / Claude

## Verification

- (2026-09-24) After merging and generating `Wave2`, before fixes: `npm run test:api` 162 passed, 6 failed (all SetupTests, from the Family Camp slug collision).
- (2026-09-24) Fresh stack: `__EFMigrationsHistory` lists Initial, Wave1, Wave2; `sys.triggers` has `TR_AuditEvents_Immutable`; `DELETE TOP(1) FROM AuditEvents` fails with "Audit rows can't be changed or deleted."
- (2026-09-24) After the cross-slice fixes: `npm run test:api` 171 passed, 0 failed; `npm run lint` and `npm run typecheck` pass.
- (2026-09-24) First full e2e on a fresh stack (before the settlement seed change): `npx playwright test` 49 passed.
- (2026-09-24) Final: `npm run verify:precommit` pass; `npm run test:api` 171 passed, 0 failed; after `docker compose down -v && docker compose up -d --build`, no seeded payment is unsettled, and `npx playwright test` gives 49 passed, then 49 passed again straight after.
- (2026-09-24) Click-through: sidebars read Alex (all groups), Marcus (no Setup editors, no transfer or duplicate queues; Finance shown), Diane (no Setup editors, no Finance), Grace (host shell only; `/admin` sends her to `/host`). Marcus opening `/admin/setup/programs` by URL gets setup's "for admins" message. `/admin/session` shows capacity as plain numbers to Diane and Marcus. FN2 shows "$56.25" and "$2,193.75". Grace's paid invoice reads "Paid Sep 24, 2026" beside "Issued Feb 1, 2028", as the dates decision expects.
- (2026-09-24) The three new API tests (admittance unpublished, group checkout unpublished, transfer scholarship and scoped code) fail with the service changes reverted (3 of 3) and pass with them.

## Outcomes & Retrospective

Wave 2 is one working product on branch `wave2`, with one `Wave2` migration that carries setup's audit trigger. The only real merge collision was in data, not code: two slices seeded the same program slug, and the second seed quietly skipped its demo program. That cost six API tests, and nothing in the file diff showed it. Beyond that, the cross-slice work was about agreement between slices: checkout, admittance and groups now apply the same publish rule; transfers respect both setup's rule scope and finance's awards; the sidebar and the capacity endpoint match the API policies; and money prints the same everywhere. No e2e assertion was weakened, and the suite needed no changes to pass on the combined seed.

Left open:
- 5eb385e (finance review fixes: a lock on FN2 Resolve plus a concurrency test, FN2 phone layout, FN4 paging, a slice-local `money.ts`) is on the finance worktree but not on `slice/finance`, so it isn't in `wave2`. This session wasn't allowed to merge it. If it's merged, drop its `money.ts` in favour of the shared `money()`, which now prints the same output.
- Family Camp has no waiver template (finance seeded none). It's published now, so a family can register for it without signing one.
- Setup's "Family Weekend" has two sessions, one of them past, and is still pending approval; the setup spec approves it.
- The dates decision stands: seeded 2028, live actions stamped 2026.
- There's no role guard on routes; restricted pages opened by URL show their own 403 message.
- Wave 1 carry-overs are unchanged (split orders on F1 and F4, the account menu loading only on mount, David Johnson not being an emulator persona).

## Context and Orientation

The repository is a WinShape camp registration prototype: an ASP.NET Core minimal API in `api/Camp.Api` (EF Core, SQL Server), a Vue 3 app in `web/`, and Playwright specs in `e2e/`. `AGENTS.md` explains the slice seams. The docker stack runs SQL Server (:14333), the WorkOS emulator (:4100), the API (:5080) and nginx serving the web app (:5173). The API migrates and seeds on startup: the core seed, then each slice's `ISeedModule` by `Order` (finance 200, setup 900).

Files this plan changes outside a single slice, and why:

- `api/Camp.Api/Data/Migrations/*_Wave2*`, `CampDbContextModelSnapshot.cs`: the combined migration, with the audit trigger SQL.
- `api/Camp.Api/Features/Setup/SetupSeed.cs`, `api/Camp.Api.Tests/SetupTests.cs`, `e2e/setup.spec.ts`: "Family Weekend".
- `api/Camp.Api/Features/Finance/FinanceSeed.cs`: Family Camp published.
- `api/Camp.Api/Features/Admittance/AdmittanceService.cs`, `api/Camp.Api/Features/Groups/GroupService.cs`: publish guard, with tests in `AdmittanceTests.cs` and `GroupsTests.cs`.
- `api/Camp.Api/Features/StaffCx/TransferService.cs`: scholarship and rule scope on a move, with a test in `StaffCxTests.cs`.
- `api/Camp.Api/Features/AdminEndpoints.cs`, `api/Camp.Api.Tests/AuthTests.cs`, `web/src/pages/admin/SessionEditor.vue`, `web/src/pages/admin/Waitlist.vue`: admin-only capacity.
- `web/src/lib/nav.ts`, `web/src/layouts/AdminLayout.vue`, `web/src/features/{setup,finance,staff-cx}/routes.ts`: role-aware sidebar.
- `web/src/lib/format.ts`: `money()`.

## Plan of Work

Milestone 1 merges the slices and regenerates the migration. Milestone 2 fixes the seed collision and the cross-slice items, each with an API test where money or capacity is involved. Milestone 3 runs the gates and two full e2e runs on a fresh stack and fixes cross-slice e2e interactions without weakening assertions. Milestone 4 is a persona click-through at desktop and phone widths.

## Concrete Steps

From `/Users/ethanwoo/dev/camp-registration-prototype`:

    git checkout -b wave2 main
    git merge --no-ff slice/setup; git merge --no-ff slice/finance; git merge --no-ff slice/ops; git merge --no-ff slice/host
    git rm api/Camp.Api/Data/Migrations/*_{Ops,Finance,Setup,Host}.*
    git show main:api/Camp.Api/Data/Migrations/CampDbContextModelSnapshot.cs > api/Camp.Api/Data/Migrations/CampDbContextModelSnapshot.cs
    scripts/dotnet.sh ef migrations add Wave2 --project api/Camp.Api --output-dir Data/Migrations
    # then paste the TR_AuditEvents_Immutable SQL from the Setup migration into Wave2 Up/Down
    npm run verify:precommit && npm run test:api
    docker compose down -v && docker compose up -d --build
    npx playwright test && npx playwright test

## Validation and Acceptance

- [x] `npm run verify:precommit` (pass)
- [x] `npm run test:api` (171/171)
- [x] `npx playwright test` on a fresh stack, twice in a row (49, 49)
- [x] Persona click-through at 1440 and 390 wide (pass)

## Idempotence and Recovery

The migration is regenerated from `main`'s snapshot, so rerunning the steps produces the same `Wave2` migration. The wave is unreleased; no database outside local stacks has it. To recover demo data, `docker compose down -v && docker compose up -d --build`, or run the e2e suite, whose global setup drops and reseeds the database.

## Artifacts and Notes

Commits on `wave2`: the four `--no-ff` slice merges, 0846885 (Wave2 migration, one Family Camp), b06ba93 (cross-slice fixes), cc7a140 (settlement seed order), and the commit that completes this plan. Screenshots from the click-through are in the session scratchpad (`wave2-shots/`), not the repo.

## Interfaces and Dependencies

New in the web app: `RouteMeta.roles?: StaffRole[]` and `cetRoles`, `financeRoles`, `adminRoles` in `web/src/lib/nav.ts`. API: `PUT /api/admin/pools/{id}` now requires the Admin policy. Schema: the union of the four slice migrations, unchanged, plus the audit trigger.
