# True demo of the A+B MVP, built in parallel slices

- Plan Type: ExecPlan
- Status: In Progress
- Owner: Claude (orchestrator)
- Started: 2026-09-24
- Completed:

> Maintain this file in accordance with `docs/PLANS.md`.

## Purpose / Big Picture

The prototype covers one happy path: a returning family registers for Day Camp, and staff manage the session. For the RFP we want a "true demo" of the A+B MVP, meaning every flow an evaluator would ask about works end to end on real data. That includes a brand-new family adding a child, a marriage-retreat application that holds a card until approval, a church group whose members complete forms by secure link, and front-desk staff searching, merging, approving discounts, and moving campers between sessions. Screens we don't need for the story, such as the long tail of settings, stay out.

When this plan is done, a presenter can sign in as each seeded persona (see `README.md`) and walk the flows listed under Milestones without hitting a stub. Every flow has an API integration test and a Playwright spec that pass on freshly seeded data.

## Progress

- [x] (2026-09-24) Step 0, the foundation: quality gates, WorkOS sign-in, and slice seams. See `docs/exec-plans/completed/2026-09-24-foundation-guardrails-auth.md`.
- [x] (2026-09-24) Wave 1, slice 1: family self-service (F1–F6, plus add child). See `docs/exec-plans/completed/2026-09-24-family.md`.
- [x] (2026-09-24) Wave 1, slice 2: admittance programs (R2, F7, C6, card hold). See `docs/exec-plans/completed/2026-09-24-admittance.md`.
- [x] (2026-09-24) Wave 1, slice 3: groups and cohorts (R8, G1, G2). See `docs/exec-plans/completed/2026-09-24-groups.md`.
- [x] (2026-09-24) Wave 1, slice 4: staff customer service (C1, C2, C8, C9, C10, F8). See `docs/exec-plans/completed/2026-09-24-staff-cx.md`.
- [x] (2026-09-24) Wave 1 integration: merge in order 1 → 4, regenerate one combined migration, full suite green on branch `wave1` (test:api 97/97; e2e 23/23 twice on a fresh stack; reviewer fixes and cross-slice links). Merged to `main` as 25eddf2 and pushed; pre-push gates passed. See `docs/exec-plans/completed/2026-09-24-wave1-integration.md`.
- [x] (2026-09-24) Wave 2 planned: four slices (setup, finance, ops, host), defined under "Wave 2" in Plan of Work. Added the `Admin` policy and the admin persona Alex Morgan for the setup slice.
- [x] (2026-09-24) Wave 2, slice 5: program setup (`setup`). Built and reviewed on `slice/setup`; merged into `wave2`.
- [x] (2026-09-24) Wave 2, slice 6: finance (`finance`). Built and reviewed on `slice/finance`; merged into `wave2`.
- [x] (2026-09-24) Wave 2, slice 7: operations (`ops`). Built and reviewed on `slice/ops`; merged into `wave2`.
- [x] (2026-09-24) Wave 2, slice 8: host portal (`host`). Built and reviewed on `slice/host`; merged into `wave2`.
- [x] (2026-09-24) Wave 2 integration on branch `wave2` (not yet merged to main): one `Wave2` migration with the audit trigger, cross-slice fixes, `npm run test:api` 171/171, `npx playwright test` 49 and 49 on a fresh stack. See `docs/exec-plans/completed/2026-09-24-wave2-integration.md`.

## Surprises & Discoveries

- Wave 1: every slice passed its own e2e on its own database, but 5 of 23 specs failed once the seeds were combined. One slice's seed added a second Day Camp session (so "Register" became "Register for June week…"), another's added a past retreat session that the review queue opened by default, and several specs mutated Maria's household. Wave 2 slices therefore seed into their own programs and sessions where they can, never rename or add sessions to programs other slices' specs use, and write specs that assert on their own rows rather than global counts.
  Evidence: `docs/exec-plans/completed/2026-09-24-wave1-integration.md`.
- Wave 2: code merged cleanly, but two slices seeded the same program slug (`family-camp`), and the later seed skipped its own demo program without an error. Future slices should give seeded programs a slice-specific slug, or fail loudly when the slug they need is taken.
  Evidence: `docs/exec-plans/completed/2026-09-24-wave2-integration.md`.

## Decision Log

- Decision: Build in vertical slices, one agent each, in separate git worktrees. A separate reviewer agent checks each slice against its screen cards and `docs/QUALITY.md` before merge.
  Rationale: the seams from step 0 (endpoint modules, seed modules, route globs, sidebar from route meta) keep slices out of each other's files. Review by a different agent catches what the builder rationalizes away.
  Date/Author: 2026-09-24 / Claude
- Decision: Wave 1 is the four slices above. Wave 2 waits for wave 1 to land.
  Rationale: wave 1 covers the flows evaluators ask about first (new family, admittance, groups, front desk). Wave 2 needs wave 1's entities, such as applications and transfers.
  Date/Author: 2026-09-24 / Claude
- Decision: Skip the long tail entirely (integration health, migration exceptions, workflow stage settings).
  Rationale: nothing in the demo story needs them, and the RFP doesn't score them as demo items.
  Date/Author: 2026-09-24 / Claude

## Verification

- (2026-09-24) Step 0: `npm run test:api` 16/16 pass, and `npm run test:e2e` 5/5 pass on `docker compose up -d --build`.

## Outcomes & Retrospective

Not started.

## Context and Orientation

Start with `AGENTS.md`. It covers the slice seams, auth, migrations, and commands. The product source is `docs/product/screen-specs.md`, where each screen has a card with an ID (F1, R2, C6…) listing its job, what it must show, its states, and its traps. The concept images for each screen are in `/Users/ethanwoo/dev/easyllama-speed-extension/WinShape screen catalogue/<area>/<ID>-*.png`; `03-family/` also holds F and G screens, and `06-admin-cx/` holds C screens.

Existing code a slice will read but should not restructure:

- `api/Camp.Api/Domain/Entities.cs`: households, people, programs (`ProgramType` includes Standard, Admittance, and Cohort), sessions, capacity pools, orders, registrations, payments, installments, waivers, discount codes (with `DiscountStatus.Pending`), the waitlist, the outbox, and audit.
- `api/Camp.Api/Features/Checkout.cs`: seat claiming and charging. It rejects non-Standard programs today.
- `api/Camp.Api/Integrations/PaymentGateway.cs`: the fake Fiserv gateway. Card `4242…` approves and `…0002` declines.

## Plan of Work

Each slice owns one folder on each side: `api/Camp.Api/Features/<Slice>/` and `web/src/features/<slice>/`, plus `api/Camp.Api.Tests/<Slice>Tests.cs` and `e2e/<slice>.spec.ts`. A slice may edit a shared file only when the slice's own section below allows it, and must record every such edit in its own ExecPlan's Decision Log.

**Slice 1, family self-service (`family`), persona Maria or Sam.**

- F1 family home: extend the existing `web/src/pages/guest/Family.vue` (owned by this slice).
- F2 member profile: add and edit a child or adult, including date of birth, gender, and grade derived with the Sept 1 rule in `Domain/Eligibility.cs`.
- F3 household access: invite a co-guardian by email and list who has access. Store invitations as a slice entity.
- F4 my registrations, F5 registration detail (family view), and F6 payments and receipts, including paying a remaining balance with the fake gateway.
- Demo: Sam signs in for the first time, adds a child, and registers them for Day Camp. Maria pays her balance.
- May edit: `GuestLayout.vue` nav entries, `Family.vue`.

**Slice 2, admittance programs (`admittance`), personas Maria (applicant) and Diane (reviewer).**

- R2 admittance application for the marriage retreat (a WSM program in the seed), with the card authorized but not captured.
- F7 application status for the family.
- C6 application review queue for staff: approve, which captures the held card and confirms the seat; decline, which voids the hold; or waitlist.
- May edit: `Integrations/PaymentGateway.cs`, to add authorize, capture, and void to `IPaymentGateway` and the fake. It must not change `Checkout.cs`; admittance gets its own service.

**Slice 3, groups and cohorts (`groups`), persona Pastor Dave.**

- R8 group roster entry: a leader registers a church group for a cohort program and enters names and emails.
- G1 secure-link form completion: each attendee gets a tokenized link, opened without signing in, to fill in their forms and waivers.
- G2 group leader tracker: who's done and who's missing, with resend.
- Demo emails go through the existing outbox; show the link in the UI for the demo.
- May edit: nothing shared. Anonymous token routes are guest routes without `meta.auth`.

**Slice 4, staff customer service (`staff-cx`), personas Diane (cet) and Marcus (finance).**

- C1 global search, replacing the header "Search families" link target (may edit `AdminLayout.vue` for that one link).
- C2 household 360.
- C8 discount approval queue, where the pending `SUMMERFUN` code gets approved. Seed a few pending codes.
- C9 duplicate review and merge: seed a likely duplicate household, and merge people and registrations with an audit trail.
- C10 transfer request review, plus the guest side F8 session transfer request. The family requests a move; staff approve, which moves the seat under the same capacity rules as checkout; or deny.
- May edit: `AdminLayout.vue` (search link only).

Every slice, in order:

1. Write `docs/exec-plans/active/<date>-<slice>.md` from the ExecPlan template and commit it first.
2. Build API endpoints with integration tests, then pages, then the Playwright spec.
3. Keep Progress current, and commit at each green step. Hooks must pass; never use `--no-verify`.
4. Self-review with the seven passes in `docs/product/screen-specs.md` Part 1, take phone and desktop screenshots of each screen, and compare them with the concept PNGs.
5. Move the plan to completed with `node scripts/complete-exec-plan.mjs <slug>`.

**Wave 2.** Same rules as wave 1. Seeds go into each slice's own programs, sessions, or rows; never add or rename sessions on programs another slice's spec uses. e2e runs with one worker after a database reset (`e2e/global-setup.ts`), so specs run in file order against one shared seed; assert on your own rows. Skipped as long tail: K1, K8, K10, K13, K14, O4, O8. K6 (question builder), K9 and K11 are candidates for a wave 3.

**Slice 5, program setup (`setup`), persona Alex (admin).**

- K2 program list and editor: programs per ministry, type, attached waivers, publish status with an approval state (Draft → Pending approval → Published; no publish without approval).
- K3 session editor with capacity pools: extend or replace the existing `web/src/pages/admin/SessionEditor.vue` (owned by this slice). Dates, location, capacity pools (never one number), registration open and priority dates, waitlist mode locked to Admin approval. The owned retreat center shows room capacity as read-only "from Oracle Opera".
- K4 pricing and policies: price, deposit, payment plan templates, and the cancellation/refund policy as a time-based table that checkout and refunds read. Changing a price never changes existing orders.
- K5 discount rules: a rule builder (type, value, stacking, session scope, dates, usage cap) with a preview on a real camper, and a guard that blocks combinations over 100%. Must stay compatible with existing `DiscountCode` and the C8 approval queue.
- K7 waiver templates: versions with effective dates, never editing a published version in place, attached programs, and acceptance counts per version.
- K12 audit log: immutable, filterable, exportable list over the existing audit rows, including before/after where recorded.
- Policies: setup screens require `Policies.Admin`; K12 is readable by any staff.
- May edit: `SessionEditor.vue`, `Checkout.cs` and `Pricing.cs` only to read the new policy/plan data (record in the Decision Log; ExecPlan required).

**Slice 6, finance (`finance`), persona Marcus (finance); Maria for O6.**

- FN1 reports: registrations, revenue, attendance, demographics by ministry, program, session and date range; certified-metric badge on governed measures; CSV export. Revenue ties to FN2 to the cent.
- FN2 reconciliation: a seeded Fiserv settlement batch vs platform payments, with matched, unmatched and fee lines, drill-in with Resolve (audited), and Fusion journal status per batch.
- FN3 payment plan exceptions: failed installments, retry schedule, grace-period end, days to policy action, Retry now (through the fake gateway) and Contact family (outbox).
- FN4 Fusion journal export: batches with date, entries, total, status (Posted, Failed, Pending) and error detail.
- O6 scholarship application (family, guest route) and O7 scholarship review (staff) with the cap check: scholarship plus discounts never exceed 100% of the price. An approved award reduces the order balance through the server.
- May edit: `GuestLayout.vue` menu (one "Scholarships" entry).

**Slice 7, operations (`ops`), persona Diane (cet) or Alex.**

- Seed Overnight Camp (ON) Session 3 to the Part 3 canonical numbers: 200 capacity in pools including Boys G6–8, 186 registered, 9 waitlisted, 159 ready, 27 needing attention (19 CampDoc, 6 waivers, 8 balance, with overlap). Seed only into ON Session 3 and its own campers.
- O1 session readiness: tiles, roster with Payment, Waiver, CampDoc, Cabin, Activity columns, a needs-attention breakdown that explains the overlap, bulk remind (outbox), and a pool breakdown. Every count ties to the roster.
- O2 group assignment board: filter, groups with capacity, a card per camper with grade, request-match tag and review reason, Auto-suggest that a human approves, autosave with undo, and a keyboard "Move to…" menu. Totals sum to the filter count.
- O3 rooming board: 16 cabins of 12 beds split by gender, cabinmate requests met or unmet, conflicts, and a re-review flag after roster changes. No mixed-gender cabin is possible.
- O5 check-in and check-out: tablet layout, search, per-camper blockers that stop check-in without an explicit, audited override, and authorized pickup adults at check-out.

**Slice 8, host portal (`host`), persona Grace (host).**

- A host shell of its own (routes with `meta.auth: 'host'` if needed: may add that value to the router guard and `RouteMeta`, recording it in the Decision Log). Grace sees only Grace Community Church's event.
- H1 host home: registrations vs last year, volunteers by status, invoice balance, upcoming deadlines.
- H2 volunteers with CSV batch upload: template link, validation preview with row-level reasons (the demo file has 37 valid rows and 3 errors), fix inline or skip, Submit, then vetting statuses. Nothing is dropped silently. Ship the demo CSV under `e2e/fixtures/`.
- H3 invoices: list, detail with line items, and Pay through the fake gateway. Payments are recorded server-side.
- May edit: `web/src/router.ts` and `web/src/lib/nav.ts` for the host auth value only; `Auth/AuthEndpoints.cs` only if the host role needs a different landing route.

## Concrete Steps

From the repo root:

    docker compose up -d db workos        # SQL :14333, WorkOS emulator :4100
    scripts/dotnet.sh run --project api/Camp.Api --urls http://localhost:5080
    npm --prefix web run dev               # :5173
    npm run test:api
    E2E_BASE_URL=http://localhost:5173 npm run test:e2e

Parallel worktrees can't all bind to :5080 and :5173. Each slice runs its own API and Vite on distinct ports, set with `--urls` and `vite --port`, with `API_URL=http://localhost:<port>` pointing the Vite proxy at its API. Each slice also sets its own `ConnectionStrings__Default` database name (`CampRegistration_<slice>`) and a `WorkOS__RedirectUri` that matches its Vite port.

## Validation and Acceptance

- [x] Wave 1: `npm run lint`, `npm run typecheck`, `npm run check:api` (pass, 2026-09-24)
- [x] Wave 1: `npm run test:api` on main (97/97, 2026-09-24)
- [x] Wave 1: `npm run test:e2e` on a fresh stack (23/23 twice, 2026-09-24)
- [x] Wave 1: walkthrough of each persona's demo path at desktop and phone width (2026-09-24)
- [ ] Wave 2: all of the above on main after wave 2 merges (on branch `wave2`, 2026-09-24: lint, typecheck, check:api pass; test:api 171/171; e2e 49/49 twice on a fresh stack; persona walkthrough at desktop and phone)

## Idempotence and Recovery

Slices are additive folders. If a slice fails review, its branch is left unmerged and the plan records why. Resetting demo data is `docker compose down -v`.

## Interfaces and Dependencies

The seams from step 0 are `IEndpointModule`, `ISeedModule`, `IAuditLog`, `CurrentUser`, `StaffUser`, `Policies`, route meta `auth` and `nav`, and `signInAs` in e2e. Wave 1 may add payment authorize, capture, and void to `IPaymentGateway` (slice 2 only).
