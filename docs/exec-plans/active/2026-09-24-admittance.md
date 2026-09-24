# Admittance programs: apply, hold the card, review, and decide

- Plan Type: ExecPlan
- Status: In Progress
- Owner: Claude (slice 2 builder)
- Started: 2026-09-24
- Completed:

> Maintain this file in accordance with `docs/PLANS.md`.

## Purpose / Big Picture

Some WinShape programs are admittance-based: a couple applies to the WSM Fall Marriage Retreat, staff read the application, and only then is the seat confirmed and the card charged. Before this change the prototype refused every non-standard program ("This flow isn't part of the prototype yet"). After it, three screens work end to end on real rows.

A family (persona Maria Johnson) opens the retreat's program page, chooses Apply, and fills in a four-step application (R2): couple details, their marriage, a few questions, then review and card. The application autosaves as a draft so she can leave and resume. On submit the card is authorized for $900 but not charged, and she lands on the application status page (F7), which shows Submitted, Under review, and Decision, says "Authorized (not charged)", and tells her the next step. If staff ask a question, she answers it there. If her authorization expires, she re-enters her card there to keep her place.

A staff member on the Customer Experience Team (persona Diane Carter) opens Applications in the admin console (C6). She sees the retreat's applications grouped by stage with counts, opens one in a reader that shows the answers and the payment state ("Authorized · expires in 3 days"), and chooses Approve, Request info, or Decline (and Waitlist when the retreat is full). Approve captures the held card and confirms the seat. Decline voids the hold. Every staff decision is written to the audit log and queues a HubSpot email through the outbox.

To see it: run the API and Vite (see Concrete Steps), sign in as `maria.johnson@example.com`, open `/programs/fall-marriage-retreat`, apply with card `4242 4242 4242 4242`, then sign in as `diane.carter@winshape.example`, open `/admin/applications`, and approve the Johnsons. Maria's status page then reads "Confirmed" and "Paid".

## Progress

- [x] (2026-09-24 15:00Z) Read AGENTS.md, QUALITY.md, PLANS.md, the wave plan, screen cards R2, F7, C6, the concept PNGs, and the existing checkout, gateway, seed, and pages. Wrote this plan.
- [ ] Gateway: authorize, capture, and void on `IPaymentGateway` and the fake.
- [ ] API: entity, EF configuration, migration `Admittance`, service, guest and staff endpoints, seed module.
- [ ] API integration tests in `api/Camp.Api.Tests/AdmittanceTests.cs`.
- [ ] Web: R2 application wizard, F7 status page, C6 review queue, and the Apply entry point on the program page.
- [ ] Playwright spec `e2e/admittance.spec.ts`.
- [ ] Seven-pass self-review with desktop and phone screenshots compared to the concepts.
- [ ] All gates green; plan completed.

## Surprises & Discoveries

- Observation: the core seed sets the retreat's single "Couples" pool to 31 of 40 seats reserved without any registration rows behind them.
  Evidence: `api/Camp.Api/Data/Seed.cs`, `Reserved = 31`. The slice seed adds 31 approved applications with real registrations and paid orders so that count is a real row count, and it does not touch `Reserved`.
- Observation: `PendingPaymentReconciler` finalizes any `PaymentOrder` left in `Pending` for two minutes by looking up a charge under the order's idempotency key. An order that only holds an authorization would be "declined" by it.
  Evidence: `api/Camp.Api/Integrations/Workers.cs`. So the authorization lives on the application row, and a `PaymentOrder` is created only when the card is captured.

## Decision Log

- Decision: Store applications in a new slice entity `AdmittanceApplication` (table `AdmittanceApplications`) that carries the stage, the answers, the spouse, and the card authorization. A `Registration` and a `PaymentOrder` are created only when the card is captured.
  Rationale: `OrderStatus` has no "authorized" value and `Entities.cs` is a shared critical file this slice may not edit. Creating the registration at capture time also keeps every existing screen honest: the admin registration list, the session overview, and the family page never show a $900 "balance due" for a card that was only authorized.
  Date/Author: 2026-09-24 / Claude
- Decision: Submitting an application does not take a seat. Approval takes the seat with the same conditional `UPDATE ... WHERE Reserved < Capacity` that checkout uses, so the retreat can never be overbooked. If the retreat is full, approval is refused and staff can waitlist the couple instead. Waitlisting is refused while seats remain.
  Rationale: admittance programs get more applicants than seats; reserving on submit would lock out couples staff haven't read yet. The global contract says a waitlist is only possible when the capacity pool is full.
  Date/Author: 2026-09-24 / Claude
- Decision: Authorizations are treated as valid for 7 days from the moment they're made, and are "expiring" in the last 3. If staff approve after the authorization expired, the seat is still claimed and held for the couple, and the family is asked (by email and on F7) to re-enter a card, which is then authorized and captured at once. If an authorization expires before a decision, the family can re-authorize from F7 to keep their place.
  Rationale: this is the "authorization expiring, so the approval triggers a re-auth request" state on the C6 card and the "authorization expired, re-enter payment to keep your place" state on the F7 card. Seven days is the common card-network hold window.
  Date/Author: 2026-09-24 / Claude
- Decision: Waitlisting voids the hold. Approving a waitlisted couple later goes through the re-authorization path.
  Rationale: a hold can't outlive seven days, and a waitlist can. Voiding promptly is also kinder to the family's available credit.
  Date/Author: 2026-09-24 / Claude
- Decision: The fake gateway trusts authorization references it hasn't seen (seeded rows, or holds made before an API restart) unless it has voided or captured them itself in this process.
  Rationale: the fake is in-memory. Without this, restarting the API would make every seeded application impossible to approve. Expiry is enforced by the service from the stored timestamp, not by the fake.
  Date/Author: 2026-09-24 / Claude
- Decision: Application stages are Draft, Submitted, Under review, Info requested, Approved, Declined, and Waitlisted. Staff see everything except drafts. The family's progress bar maps them onto Submitted, Under review, and Decision. Registration-level wording uses the global vocabulary ("Application pending", "Payment pending", "Confirmed", "Waitlisted"), and payment wording uses "Authorized (not charged)", "Paid", and "Voided".
  Rationale: the C6 card asks for applications "by stage" and Approve / Reject / Request info; F7 asks for Submitted, Under review, Decision. The status vocabulary in the screen specs has no word for a declined application, so "Declined" is used as a stage only, matching the C6 concept.
  Date/Author: 2026-09-24 / Claude
- Decision: Shared-file edits. `api/Camp.Api/Integrations/PaymentGateway.cs` gains `AuthorizeAsync`, `CaptureAsync`, and `VoidAsync` (allowed by the wave plan). `web/src/pages/guest/ProgramDetail.vue` gains an Apply button for admittance programs and loses the "isn't part of the prototype yet" sentence for them.
  Rationale: the program page is the only public entry point to R2, and the wave plan says no screen in scope may be a stub. The edit is a few lines inside the existing non-standard branch, so slice 3's change to the cohort wording won't overlap much.
  Date/Author: 2026-09-24 / Claude
- Decision: Reads are open to all console staff (`Policies.Staff`); decisions need `Policies.Cet`.
  Rationale: finance may need to see what's authorized, but admitting a couple is a CET decision.
  Date/Author: 2026-09-24 / Claude

## Verification

- No code changes yet.

## Outcomes & Retrospective

Not started.

## Context and Orientation

The repository is a .NET 10 minimal API (`api/Camp.Api`) over SQL Server with EF Core, and a Vue 3 single-page app (`web/`) built from shadcn-vue components in `web/src/components/ui`. Read `AGENTS.md` first; it lists the "seams" a slice uses so it doesn't edit shared files:

- An endpoint module is a class implementing `IEndpointModule` (in `api/Camp.Api/Infrastructure/Modules.cs`); it's found by assembly scan, so no edit to `Program.cs` is needed.
- An entity is mapped with an `IEntityTypeConfiguration<T>` class next to it; `CampDbContext` applies every configuration in the assembly.
- A seed module implements `ISeedModule`; it runs after the core seed on every startup and must be idempotent.
- `IAuditLog.Record(...)` stages an audit row that the caller's `SaveChanges` commits.
- `CurrentUser` (a signed-in family, with its `HouseholdId`) and `StaffUser` are injected; policies in `api/Camp.Api/Auth/Identity.cs` guard endpoints. The household is never read from the request.
- Web pages register through `web/src/features/<slice>/routes.ts`; admin routes with `meta.nav` appear in the sidebar.

Terms. An "authorization" (or "hold") asks the card network to reserve an amount on the card without moving money. A "capture" turns that hold into a charge. A "void" cancels the hold. The fake card processor is `FakeFiservGateway` in `api/Camp.Api/Integrations/PaymentGateway.cs`; card `4242 4242 4242 4242` approves and a number ending `0002` declines. Card numbers are turned into tokens by `POST /api/fiserv-sandbox/tokenize`, standing in for Fiserv's hosted fields.

The retreat is seeded by `api/Camp.Api/Data/Seed.cs`: program slug `fall-marriage-retreat`, type `Admittance`, one session "Fall 2028" (Oct 6–8, 2028, $900 per couple), with one capacity pool "Couples" of 40.

## Plan of Work

Milestone 1, the money primitives. In `api/Camp.Api/Integrations/PaymentGateway.cs`, add to `IPaymentGateway`:

    Task<GatewayResult> AuthorizeAsync(string cardToken, int amountCents, string idempotencyKey, CancellationToken ct = default);
    Task<GatewayResult> CaptureAsync(string authorizationRef, int amountCents, string idempotencyKey, CancellationToken ct = default);
    Task<GatewayResult> VoidAsync(string authorizationRef, CancellationToken ct = default);

and implement them in the fake with the same card rules as `ChargeAsync`, idempotency by key, a `CaptureCount` counter for tests, and refusal to capture a voided hold or void a captured one.

Milestone 2, the API. Everything lives in `api/Camp.Api/Features/Admittance/`:

- `AdmittanceApplication.cs`: the entity, its enums, and its EF configuration. One application per household per session (unique index).
- `ApplicationForm.cs`: the question set for the retreat (sections, keys, labels, required flags, one conditional follow-up) and the validation used at submit.
- `AdmittanceService.cs`: submit (authorize), re-authorize, start review, request info, reply, approve (claim seat, capture, create order and registration), decline (void), waitlist (void). Each staff mutation calls `IAuditLog.Record` and adds an `OutboxEvent` for HubSpot.
- `AdmittanceEndpoints.cs`: the guest routes under `/api/admittance` (policy Family) and staff routes under `/api/admin/admittance` (reads Staff, decisions Cet).
- `AdmittanceSeed.cs`: `ISeedModule` with `Order = 100`: 31 approved couples (with registrations and paid orders, matching the 31 reserved seats), 11 submitted, 6 under review, 1 with info requested, 1 whose hold expired, and 3 declined. Maria has no application, so the demo starts clean.

Then one EF migration named `Admittance`.

Milestone 3, tests: `api/Camp.Api.Tests/AdmittanceTests.cs`.

Milestone 4, the web, in `web/src/features/admittance/`: `routes.ts`, `api.ts` (types and calls), `Apply.vue` (R2 at `/apply/:sessionId`), `ApplicationStatus.vue` (F7 at `/applications/:id`), `Applications.vue` (C6 at `/admin/applications`), and small components. Plus the Apply button in `web/src/pages/guest/ProgramDetail.vue`.

Milestone 5: `e2e/admittance.spec.ts`, the Maria-then-Diane demo path.

## Concrete Steps

From the worktree root `/Users/ethanwoo/dev/camp-wt/admittance`, with SQL Server on `localhost,14333` and the WorkOS emulator on `localhost:4100` already running:

    ConnectionStrings__Default="Server=localhost,14333;Database=CampRegistration_Admittance;User Id=sa;Password=Camp_Dev_Passw0rd!;TrustServerCertificate=True" \
      WorkOS__RedirectUri=http://localhost:5182/api/auth/callback \
      scripts/dotnet.sh run --project api/Camp.Api --urls http://localhost:5082
    API_URL=http://localhost:5082 npm --prefix web run dev -- --port 5182 --strictPort
    npm run test:api
    E2E_BASE_URL=http://localhost:5182 npx playwright test e2e/admittance.spec.ts e2e/smoke.spec.ts

The migration is generated with:

    scripts/dotnet.sh ef migrations add Admittance --project api/Camp.Api --output-dir Data/Migrations

To reset demo data, drop the `CampRegistration_Admittance` database and restart the API.

## Validation and Acceptance

- [ ] `npm run lint` (pass)
- [ ] `npm run typecheck` (pass)
- [ ] `npm run check:api` (pass)
- [ ] `npm run test:api` (pass, including every test in `AdmittanceTests`)
- [ ] `E2E_BASE_URL=http://localhost:5182 npx playwright test e2e/admittance.spec.ts e2e/smoke.spec.ts` on a fresh database (pass)
- [ ] Walkthrough: Maria applies, sees "Authorized (not charged)"; Diane approves; Maria sees "Confirmed" and "Paid" (pass)

## Idempotence and Recovery

The seed module checks for existing applications before inserting, so restarting the API is safe. Every guest and staff action checks the current stage with a conditional update, so a repeated click returns a conflict instead of double-capturing. Gateway calls carry idempotency keys derived from the application id, so a retried capture returns the original result. Dropping the slice database and restarting rebuilds everything.

## Artifacts and Notes

None yet.

## Interfaces and Dependencies

No new packages. The slice depends on `IPaymentGateway` (extended as above), `IAuditLog`, `CurrentUser`, `StaffUser`, `Policies.Family`, `Policies.Staff`, `Policies.Cet`, and the `Registration`, `PaymentOrder`, `PaymentOperation`, `CapacityPool`, and `OutboxEvent` entities in `api/Camp.Api/Domain/Entities.cs`.
