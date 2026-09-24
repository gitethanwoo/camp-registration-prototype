# Fix four defects seen in the recorded RFP demo

- Plan Type: ExecPlan
- Status: Completed
- Owner: Claude
- Started: 2026-09-24
- Completed: 2026-09-24

> Maintain this file in accordance with `docs/PLANS.md`.

## Purpose / Big Picture

A recorded walk-through of the prototype showed four small defects a buyer would notice. After this plan:

1. A family on a payment plan never sees an installment dated before "today". The demo clock says March 2, 2028. Before this change, Maria's Day Camp plan read "Installment 1 of 3 · Mar 1, 2028", a date that had already passed. Now the review step, the confirmation page and the family Payments page all show Apr 1, May 1 and Jun 1, 2028: three installments of $150, the last one on the session's balance due date.
2. Opening Setup › Users (K11) on fresh data no longer revokes anyone or pops a toast. The seeded staff roster matches the WorkOS emulator's staff organization. Morgan Ellis, the former staff member, is seeded as already revoked by an earlier sync. The sync that runs when the page opens stays quiet unless it actually changed someone.
3. Household 360 (C2) History lists everything that happened to the household: its registrations, orders, installments, waitlist spots, transfers, and admittance and scholarship applications, newest first. It used to show only events filed against the household row itself, so an approved retreat application never appeared.
4. On Reconciliation (FN2), the batch picker's "· 2 unmatched" label now updates after a line is resolved.

## Progress

- [x] (2026-09-24 18:00Z) Read AGENTS.md, PLANS.md, and the code for all four defects. Implemented the fixes and their tests.
- [x] (2026-09-24 18:30Z) `npm run test:api` (220/220) and `npm run verify:precommit` pass.
- [x] (2026-09-24 19:30Z) Fresh stack (`docker compose down -v && docker compose up -d --build`); full `npx playwright test` passes (64/64). In the browser, after resolving Dana Mitchell's line, the batch picker reads "Fiserv batch FS-2028-03-01 · 1 unmatched".

## Surprises & Discoveries

- Observation: the seeded Day Camp session contradicted the pricing screen's own rule. `PricingSetupEndpoints.Validate` refuses a plan whose first installment has passed, but the seed's balance due date of May 1 with 3 installments put the first on Mar 1, the day before the demo's "today".
  Evidence: `Seed.cs` had `PlanInstallments = 3, BalanceDueDate = new(2028, 5, 1)`. `Pricing.PlanSchedule` counted back from that date: `BalanceDueDate.AddMonths(i - (n - 1))`.
- Observation: most of the "1 updated" in the K11 sync toast came from linking a seeded row to its WorkOS user id for the first time. `StaffSync.ReconcileAsync` counted that as a change.
- Observation: the stale batch label was not a data problem. `Reconciliation.vue` already reloaded the batch list after a resolve. reka-ui's `SelectValue` keeps the text of the selected item from when that item was chosen, so it doesn't follow later changes to the item.

## Decision Log

- Decision: keep the installment count and move the schedule forward. The canonical dataset (screen-specs.md, Johnson family) is 3 × $150, so dropping past installments and spreading the balance over fewer was not an option. `Pricing.PlanDates` moves the whole monthly schedule forward by whole months until the first date is on or after today. It then caps any date at the day before the session starts.
  Rationale: the result stays monthly and always has the configured count. It never charges before today and never charges after camp begins.
  Date/Author: 2026-09-24 Claude
- Decision: also move the seeded Day Camp balance due date to Jun 1, 2028, for both June weeks.
  Rationale: this keeps the plan's last installment on the session's balance due date, so deposit payers ("due by Jun 1") and plan payers agree. The seed also passes the pricing screen's rule again. Without it, the code fix alone would give Apr 1, May 1, Jun 1 plans while deposit payers still read "due by May 1".
  Date/Author: 2026-09-24 Claude
- Decision: `Pricing.Build` and `Pricing.PlanSchedule` take `today` as a parameter rather than a `TimeProvider`.
  Rationale: they stay pure functions. Callers pass `clock.Today()`, and the Staff CX seed passes the order's creation date.
  Date/Author: 2026-09-24 Claude
- Decision: seed Morgan Ellis as Revoked, 34 days before the demo's "today". The seed also writes the sync run and `staff.revoked` audit row that did it. It does not add Morgan to the emulator config.
  Rationale: the demo still shows a revoked former staff member, with a reason on the sheet, but opening the page changes nothing. A new API test checks that the seeded active roster equals the memberships in `infra/workos/workos-emulate.config.yaml`.
  Date/Author: 2026-09-24 Claude
- Decision: exclude `health.*` audit actions from Household History.
  Rationale: Household 360 never carries health information (FR-112). Views of health records belong in the audit log, not on the family's page.
  Date/Author: 2026-09-24 Claude

## Verification

- `npm run test:api`: all tests pass, including new PricingTests theory cases, the RegistrationTests late-plan checkout, AccessTests seed-vs-config and quiet-sync tests, and the StaffCxTests household history test.
- `npx playwright test` against a freshly rebuilt stack: all specs pass. The new assertions are in family (installment dates on review, confirmation and payments), access (quiet sync), staff-cx (History after the transfer) and finance (batch picker label).

## Outcomes & Retrospective

All four defects are fixed and covered by API and e2e tests. One lesson: seed data should pass the same validation the setup screens enforce. The pricing rule already existed and would have caught the past-dated installment.

## Context and Orientation

The app is a .NET 10 minimal API (`api/Camp.Api`) with a Vue 3 web app (`web/src`) and SQL Server. The demo clock (`Features/Polish/DemoClock.cs`) is a `TimeProvider` that starts at 2028-03-02 15:00 UTC. `clock.Today()` gives a `DateOnly`. Relevant files:

- Payment plans: `api/Camp.Api/Features/Pricing.cs` (`Build`, `PlanSchedule`, `PlanDates`), called from `Features/GuestEndpoints.cs` (the quote shown on the review step), `Features/Checkout.cs` (stores `Installment` rows), `Features/Setup/PricingSetupEndpoints.cs` (preview) and `Features/StaffCx/StaffCxSeed.cs`. Seeded sessions are in `Data/Seed.cs`. The family pages `web/src/features/family/Payments.vue` and `ReceiptDialog.vue` now show the last stored installment's date rather than the session's balance due date.
- Staff access: `Features/Access/AccessSeed.cs` (roster), `Features/Access/StaffAccess.cs` (`StaffSync.ReconcileAsync`), `web/src/features/access/StaffUsers.vue` (automatic sync on open).
- Household 360: `Features/StaffCx/HouseholdEndpoints.cs` (`HistoryAsync`), `web/src/features/staff-cx/Household360.vue`.
- Reconciliation: `web/src/features/finance/Reconciliation.vue`.

## Plan of Work

Each defect was fixed where it starts, with tests added or updated next to the existing ones: `PricingTests`, `RegistrationTests`, `AccessTests`/`AccessSyncTests`, `StaffCxTests`, and the family, access, staff-cx and finance Playwright specs.

## Concrete Steps

From the repository root:

    npm run verify:precommit
    npm run test:api
    docker compose down -v && docker compose up -d --build
    npx playwright test

## Validation and Acceptance

- [x] `npm run verify:precommit` (pass)
- [x] `npm run test:api` (pass)
- [x] `npx playwright test` on a fresh stack (pass)
- [x] Browser: resolve an unmatched line on /admin/finance/reconciliation as Marcus; the picker reads "· 1 unmatched" (pass)

## Idempotence and Recovery

The seed changes apply only to a fresh database. `AccessSeed` adds only the staff rows that are missing, and writes Morgan's sync run and audit row only when it creates Morgan. Rebuild with `docker compose down -v && docker compose up -d --build`.

## Artifacts and Notes

None.

## Interfaces and Dependencies

`Pricing.Build(Session, IReadOnlyList<Person>, PaymentOption, DiscountCode?, string?, DateOnly today)` and `Pricing.PlanSchedule(Session, int remaining, DateOnly today)` now take `today`. The new `Pricing.PlanDates(Session, DateOnly today)` returns the installment dates. `AccessSeed.Staff` exposes the seeded roster.
