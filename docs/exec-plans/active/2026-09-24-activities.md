# Activities: catalog, schedule builder, activity choice and cabinmate requests for Overnight Camp

- Plan Type: ExecPlan
- Status: In Progress
- Owner: Claude (activities slice builder)
- Started: 2026-09-24
- Completed:

> Maintain this file in accordance with `docs/PLANS.md`.

## Purpose / Big Picture

Overnight Camp (ON) campers spend three activity periods a day in things like archery, swimming and climbing. Before this plan the product only stored one made-up activity name per camper on the operations placement row (`OpsPlacement.Activity`), which O1 showed as a column. Nobody could define activities, families could not choose them, and staff could not build or check a schedule.

After this plan:

- K8 Activity catalog (`/admin/setup/activities`, Setup nav "Activities", admin only): Alex Morgan creates and edits activities (image, description, what to bring, grade limits, default capacity per period, staff ratio or instructor requirement, space). Every create and edit writes an audit row.
- O4 Activity schedule builder (`/admin/ops/activities`, Operations nav "Activities", staff): ON Session 3 has two age blocks, Juniors (grades 3–5) and Seniors (grades 6–8), each a grid of 6 activities × 3 periods with capacity 24. Each cell shows assigned / capacity, the instructor and the space. The page lists conflicts (a camper booked twice in one period, a slot over capacity, a camper outside an activity's grade limits), lets staff move a camper to another activity in the same period when it has room, and runs "Assign from preferences" after a person confirms a preview. Totals tie to the roster.
- R4 Activity selection (a registration wizard step, ON only): one tab per camper, ranked choices (1st, 2nd, 3rd) per period from the activities offered to that camper's block and grade, live "slots left", and a Details link to P3. If a slot fills while the family is choosing, the choice shows "Just filled", an amber notice explains it, and the next choice is suggested. At checkout the server assigns each camper to the highest-ranked choice with room, inside the same transaction that claims seats; if none has room it answers 409 with alternatives and the wizard returns to this step. Families who registered without choosing can choose later from the family home checklist ("Choose activities").
- P3 Activity detail (`/activities/:id`, and a sheet from R4): what it is, grade limits, space, staff ratio, what to bring, and periods with slots left.
- R5 Cabinmate request (a wizard step, ON only): up to 2 friends by name and a parent email or friend code (the friend's confirmation code). The page says requests aren't guaranteed and that mutual requests are most likely to be met. Matched requests become `OpsBuddyRequest` rows so O3 rooming shows them met or not met.
- O1 Session readiness reads the Activity column and filter from the new assignments.

## Progress

- [x] (2026-09-24 00:00Z) Worktree `/Users/ethanwoo/dev/camp-wt/activities` on `slice/activities` from `main` 70609ee; read AGENTS.md, PLANS.md, QUALITY.md, the exec-plan index, the master plan, `docs/product/screen-specs.md` and the K8/O4/R4/P3/R5 concepts; read Checkout, GuestEndpoints, Register.vue, the Ops slice and the Family overview.
- [x] (2026-09-24 17:10Z) Milestone 1: entities, `Activities` migration, seed, endpoints, checkout integration, O1/O5 change, `ActivitiesTests.cs` (18 tests; `npm run test:api` 238 passing, up from 220). Reverting the capacity guard, the decline release or the household check each fails tests (6 failures together).
- [x] (2026-09-24 19:40Z) Milestone 2: web slice `web/src/features/activities/` (K8 catalog and editor sheet, O4 schedule with block tabs, cell sheet, keep and move, assign preview; R4 `ActivityStep` with just-filled notice and suggested next choice; P3 page and sheet; R5 `CabinmateStep`; family route `/family/activities/:registrationId`), seven local SVGs in `web/public/images/activities/`, O1 filter and column from the new `activity` shape, F1 "Choose activities" / "Change activities" item. API tweaks: the family activities GET returns `sessionId` (for the P3 sheet) and O4 rows carry `gradeMin`/`gradeMax` (to offer only moves that fit the camper's grade). `e2e/activities.spec.ts` (3 tests) passes on a fresh database.
- [ ] Milestone 3: `e2e/activities.spec.ts`, full gates, screenshots, seven-pass review.
- [ ] Milestone 4: plan completed with `node scripts/complete-exec-plan.mjs activities`.

## Surprises & Discoveries

- Observation: 186 ON Session 3 campers cannot fit one 6 × 3 × 24 grid: 6 activities × 24 seats is 144 seats per period.
  Evidence: pools 46 + 50 + 44 + 46 = 186 confirmed campers; 186 > 144.

## Decision Log

- Decision: Run the schedule per age block. ON Session 3 has two blocks, Juniors (G3–5, Boys G3–5 + Girls G3–5 = 90 campers) and Seniors (G6–8, Boys G6–8 + Girls G6–8 = 96 campers). Each block has its own 6 activities × 3 periods × 24 seats (144 seats per period for 90 or 96 campers). O4 has a block switcher; block totals add up to the session roster.
  Rationale: one grid cannot seat 186 campers per period (144 seats). Grade blocks match how the pools are already split and keep ages together, which the grade limits need anyway.
  Date/Author: 2026-09-24 / Claude
- Decision: Slot counts are a counter column `ActivitySlot.Assigned` changed only by conditional SQL (`UPDATE … SET Assigned = Assigned + 1 WHERE Id = @id AND Assigned < Capacity`), the same pattern Checkout uses for `CapacityPool.Reserved`. Slots left = capacity − assigned.
  Rationale: concurrency-safe with no oversell and no extra locking. There is no database check constraint so the seed can show one over-capacity slot as a conflict for O4 to resolve.
  Date/Author: 2026-09-24 / Claude
- Decision: Activity choices are optional in the checkout request on the server; the wizard requires a first choice in every period. Waitlisted campers keep their choices but get no assignment. When a payment is declined and the registration is cancelled, its activity seats are released.
  Rationale: existing ON checkout tests and other slices post no choices; a family that skips can choose later from the family home.
  Date/Author: 2026-09-24 / Claude
- Decision: Friend code is the friend's order confirmation code (`WS-XXXXXX`). A request matches a confirmed or payment-pending registration in the same session by the friend's name (full name, or first name when it's unique in that household) and either the household's or an adult's email, or the order's confirmation code. A matched request is stored as `CabinmateRequest` and mirrored to `OpsBuddyRequest`; an unmatched request is stored and matched when the friend registers later.
  Rationale: O3 already judges `OpsBuddyRequest` rows. Families don't know other families' ids; email or code plus a name is enough to identify a camper without showing anyone's data.
  Date/Author: 2026-09-24 / Claude
- Decision: "Assign from preferences" only fills periods that have no assignment for campers who have preferences. Campers without preferences stay "Not chosen" so their family can still choose. Preview first, then a confirmed POST, audited.
  Rationale: the card says a person confirms; overwriting staff moves or a family's later choice would lose work.
  Date/Author: 2026-09-24 / Claude
- Decision: F1 demo household: one ready, fully paid Girls G3–5 filler camper's registration, order and person move into Pastor Dave Kim's household (last name Kim) during the Activities seed, with no activity choice. The e2e family flow also uses Pastor Dave.
  Rationale: the card asks for a persona with an ON Session 3 registration without activities, preferably one no other slice's e2e depends on. Maria's household (Johnson) is used by the family, finance, forms, admittance, polish and staff-cx specs; Sam starts empty; Pastor Dave is used only by the groups spec, which reads group pages and not the family home. 186/9/159/27 are unchanged because the registration stays.
  Date/Author: 2026-09-24 / Claude
- Decision: R4 keeps the wizard's draft separate (`useActivityDraft`, one store per session in session storage) and renders as slice components; Register.vue only decides when the steps show and what goes in the checkout payload.
  Rationale: keeps the shared wizard change small (about 60 lines) and lets the family route reuse the same step pieces (`PeriodPicker`, ranking helpers, detail sheet).
  Date/Author: 2026-09-24 / Claude
- Decision: R4 stacks the three periods, with each period's activities in two columns, instead of the concept's three period columns side by side.
  Rationale: the wizard's step card sits beside the order summary (about 700px wide at 1440), so three columns left about 200px per period and the names, slots left and "Just filled" badge wrapped and overlapped (seen in the review screenshots).
  Date/Author: 2026-09-24 / Claude
- Decision: Shared-file edits (recorded as they land): see Artifacts and Notes.
  Rationale: the task lists which shared files this slice may touch.
  Date/Author: 2026-09-24 / Claude

## Verification

- 2026-09-24 Milestone 1: `npm run test:api` → 238 passed, 0 failed. Spot-check with the conditional `Assigned < Capacity` removed from `ActivityRules.ClaimAsync`, `ActivityRules.ReleaseAsync` removed from the decline branch of `FinalizeAsync`, and the household filter neutralised in the family activities route: 6 of 18 activity tests fail (concurrency, 409, ranked fallback, staff move, decline, other household), then pass again once restored.

## Outcomes & Retrospective

## Context and Orientation

The repository is a .NET 10 minimal API (`api/Camp.Api`) over SQL Server with EF Core, and a Vue 3 web app (`web/`) built from shadcn-vue primitives in `web/src/components/ui`. Slices plug in through seams: `IEndpointModule` maps routes, `IEntityTypeConfiguration<T>` maps entities, `ISeedModule` (with `Order`) inserts demo rows, and `web/src/features/<slice>/routes.ts` exports `routes` (guest), `adminRoutes` (admin, with `meta.nav`).

Relevant existing code:

- `api/Camp.Api/Features/Checkout.cs`: `CheckoutService.CheckoutAsync` claims seats with a conditional update inside one transaction, then charges after commit; `FinalizeAsync` cancels registrations and releases seats on decline.
- `api/Camp.Api/Features/GuestEndpoints.cs`: `/api/sessions/{id}/register-context` and `/api/family/checkout`.
- `api/Camp.Api/Features/Ops/*`: `OpsReadModel` (the ON roster), `OpsSeed` (Order 170), `OpsBuddyRequest`, O1 `ReadinessEndpoints`, O5 `CheckInEndpoints`.
- `api/Camp.Api/Features/Family/FamilyEndpoints.cs`: `/api/family/overview` builds the F1 checklist.
- `web/src/pages/guest/Register.vue`: the registration wizard.

## Plan of Work

1. API slice under `api/Camp.Api/Features/Activities/`: `ActivityEntities.cs` (Activity, ActivityBlock, ActivitySlot, ActivityPreference, ActivityAssignment, CabinmateRequest and their configurations), `ActivityService.cs` (slots left, choice validation, assignment with ranked fallback, release, cabinmate matching), `ActivityCatalogEndpoints.cs` (K8), `ActivityScheduleEndpoints.cs` (O4), `ActivityGuestEndpoints.cs` (P3, R4 context, R5 check, family post-registration choice), `ActivitiesSeed.cs` (Order 175, after Ops).
2. Shared edits: Checkout (choices and cabinmates on the request; assignment inside the seat transaction; 409 exception; release on decline), GuestEndpoints (register-context flags; 409 mapping), Ops (O1 and O5 read the new assignments; `OpsPlacement.Activity` removed), Family overview (activity checklist items), Register.vue (two extra steps rendered by slice components).
3. Migration `Activities`.
4. Web slice under `web/src/features/activities/`.
5. Tests and e2e.

## Concrete Steps

From `/Users/ethanwoo/dev/camp-wt/activities`:

    scripts/dotnet.sh tool restore
    scripts/dotnet.sh ef migrations add Activities --project api/Camp.Api --output-dir Data/Migrations
    npm run test:api

## Validation and Acceptance

- [ ] `npm run verify:precommit` (pass)
- [ ] `npm run test:api` (pass)
- [ ] `npx playwright test` full suite, one worker, fresh database, twice (pass)
- [ ] Screenshots of K8, O4, R4, P3, R5 and the family activities page at 1440×1000 and 390×844 compared with the concepts

## Idempotence and Recovery

The seed checks for existing activities and does nothing if they exist. To start over, drop the slice database and restart the API, which migrates and reseeds.

## Artifacts and Notes

Shared-file edits:

- `api/Camp.Api/Features/Checkout.cs`: `CheckoutParticipant` gains optional `Activities` and `Cabinmates`; activity/cabinmate validation errors merge into the existing error dictionary; `ActivityCheckout.ApplyAsync` runs inside the seat transaction before commit (throws `ActivityFullException` → rollback); the decline branch of `FinalizeAsync` calls `ActivityRules.ReleaseAsync`.
- `api/Camp.Api/Features/GuestEndpoints.cs`: checkout maps `ActivityFullException` to 409; register-context adds `activities` (true when the session has activity blocks).
- `api/Camp.Api/Features/Family/FamilyEndpoints.cs`: overview checklist concatenates `ActivityChecklist.ForAsync`.
- `api/Camp.Api/Features/Ops/OpsEntities.cs` and `OpsSeed.cs`: `OpsPlacement.Activity` and its seeded names removed (the migration drops the column).
- `api/Camp.Api/Features/Ops/ReadinessEndpoints.cs`: O1 `activities` filter list and roster `activity` (now `{ names, state, label }`) come from `ActivityReadModel`.
- `api/Camp.Api/Features/Ops/CheckInEndpoints.cs`: O5 `activity` label comes from `ActivityReadModel`.
- `web/src/pages/guest/Register.vue`: `steps` is computed; when register-context says `activities` and at least one chosen camper will be seated, the R4 (`ActivityStep`) and R5 (`CabinmateStep`) steps follow the questions. Continue on R4 refreshes slots left and stays once if a ranked choice just filled. Checkout sends each seated camper's `activities` and `cabinmates`; a 409 with `conflicts` returns to R4 with the just-filled state. Review shows each camper's activities. State lives in `useActivityDraft` (session storage), not in the wizard.
- `web/src/features/ops/types.ts` and `SessionReadiness.vue`: `ReadinessRow.activity` is `{ names, state, label }`; the O1 filter adds "Chosen, not placed"; column and CSV header "Activities".
- `web/src/features/family/types.ts` and `FamilyHome.vue`: checklist kind `activities`; a done activities item still links ("Change activities").

## Interfaces and Dependencies

- `POST /api/checkout` accepts optional `activities` (per participant, per period, ranked activity ids) and `cabinmates` (per participant). 409 body: `{ title, conflicts: [{ personId, firstName, period, alternatives: [{ activityId, name, remaining }] }] }`.
