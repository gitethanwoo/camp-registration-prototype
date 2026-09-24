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
- [x] (2026-09-24) Wave 1 integration: merge in order 1 → 4, regenerate one combined migration, full suite green on branch `wave1` (test:api 97/97; e2e 23/23 twice on a fresh stack; reviewer fixes and cross-slice links). Not merged to `main` yet, so the validation items below that say "on main" stay open. See `docs/exec-plans/completed/2026-09-24-wave1-integration.md`.
- [ ] Wave 2 (config, finance, operations, host portal): planned after wave 1 lands.

## Surprises & Discoveries

- None yet.

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

## Concrete Steps

From the repo root:

    docker compose up -d db workos        # SQL :14333, WorkOS emulator :4100
    scripts/dotnet.sh run --project api/Camp.Api --urls http://localhost:5080
    npm --prefix web run dev               # :5173
    npm run test:api
    E2E_BASE_URL=http://localhost:5173 npm run test:e2e

Parallel worktrees can't all bind to :5080 and :5173. Each slice runs its own API and Vite on distinct ports, set with `--urls` and `vite --port`, with `API_URL=http://localhost:<port>` pointing the Vite proxy at its API. Each slice also sets its own `ConnectionStrings__Default` database name (`CampRegistration_<slice>`) and a `WorkOS__RedirectUri` that matches its Vite port.

## Validation and Acceptance

- [ ] `npm run lint` (pass)
- [ ] `npm run typecheck` (pass)
- [ ] `npm run check:api` (pass)
- [ ] `npm run test:api` on main after wave 1 merges (pass)
- [ ] `npm run test:e2e` on main after wave 1 merges, fresh `docker compose down -v && docker compose up -d --build` (pass)
- [ ] A walkthrough of every slice's demo path as the named persona (pass)

## Idempotence and Recovery

Slices are additive folders. If a slice fails review, its branch is left unmerged and the plan records why. Resetting demo data is `docker compose down -v`.

## Interfaces and Dependencies

The seams from step 0 are `IEndpointModule`, `ISeedModule`, `IAuditLog`, `CurrentUser`, `StaffUser`, `Policies`, route meta `auth` and `nav`, and `signInAs` in e2e. Wave 1 may add payment authorize, capture, and void to `IPaymentGateway` (slice 2 only).
