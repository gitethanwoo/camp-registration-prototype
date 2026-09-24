# Camp registration prototype

A working prototype of the core camp registration flow for WinShape: program discovery, the "happy path" family registration (including a full group that routes to the waitlist), and the staff console. The stack is **.NET 10**, **Vue 3**, and **SQL Server**.

> All people and data are fictional seed data.

![stack](https://img.shields.io/badge/.NET-10-512BD4) ![vue](https://img.shields.io/badge/Vue-3-42b883) ![sql](https://img.shields.io/badge/SQL%20Server-2022-CC2927)

## Run it

```bash
docker compose up --build
```

| URL                                | What                                   |
| ---------------------------------- | -------------------------------------- |
| http://localhost:5173              | Guest site                             |
| http://localhost:5173/admin        | Staff console                          |
| http://localhost:4100              | WorkOS emulator (AuthKit sign-in page) |
| http://localhost:5080/api/programs | API                                    |

**Signing in** goes through WorkOS AuthKit, served locally by the official WorkOS emulator. Enter one of these seeded emails; there's no password:

| Email                           | Who                                                    |
| ------------------------------- | ------------------------------------------------------ |
| `maria.johnson@example.com`     | Returning family: Avery (G6) and Mia (G4)              |
| `sam.rivera@example.com`        | New family. The household is created at first sign-in. |
| `pastor.dave@example.com`       | Church group leader                                    |
| `diane.carter@winshape.example` | Staff, Customer Experience (CET)                       |
| `marcus.lee@winshape.example`   | Staff, Finance                                         |
| `grace.patel@winshape.example`  | Host coordinator                                       |
| `alex.morgan@winshape.example`  | Staff, Administrator (program setup)                   |

The API migrates and seeds the database on startup. To start over with clean demo data, run `docker compose down -v`.

**Test cards** (simulated Fiserv hosted fields):

- `4242 4242 4242 4242` approves.
- `4000 0000 0000 0002` declines.
- Use any expiry, CVC, and ZIP.

### Local dev

```bash
docker compose up -d db workos api   # SQL :14333, WorkOS emulator :4100, API :5080
cd web && npm install && npm run dev   # Vite on :5173, proxies /api to :5080
```

### Tests

```bash
npm install && npx playwright install chromium   # once
npm run test:api     # xUnit integration tests; needs `docker compose up -d db`
npm run test:e2e     # Playwright against the running stack (`docker compose up -d`)
```

Integration tests boot the real app against a throwaway database, which is deleted afterwards.

## Demo script

1. **Programs**: open Day Camp · Atlanta. Availability is shown per grade, updates live every 10s, and warns "Only N spots left" when a group is low.
2. **Register** Avery (Grade 6) and Mia (Grade 4), then go through the steps:
   1. Questions: per child, plus family questions with a conditional follow-up.
   2. Embedded health form.
   3. Versioned waivers, signed per child.
   4. Review. Choose the payment plan: **$200 today, then $150 on Mar 1, Apr 1, and May 1 2028**.
   5. Enter `SUMMERFUN`. It's a pending code, so it shows the same message as an invalid code.
3. Pay with the **decline** card. The error shows inline, the seats are released, and nothing is charged. Pay again with the approve card to get the confirmation. The "email sent" status flips once the outbox dispatcher runs.
4. **My family**: one checklist across both kids, with a single balance item for the shared order.
5. **Overnight Camp**: Boys G6–8 is full. Avery goes to the waitlist and isn't charged.
6. **Staff console**, Overnight Camp Session 3:
   - 186 registered, 159 ready, 27 needing attention (19 CampDoc, 6 waivers, 8 balance; the reasons overlap), and 9 waitlisted.
   - Click a reason to open a filtered registrations table.
   - Use "Search families" to search every ministry.
   - Open a registration to cancel it, with a refund quote from the time-based policy on the same screen.
   - Sessions & capacity: raise Boys G6–8 to 51. On Waitlists, offer #1 a spot with a deadline; the offer holds a real seat.

## Contributing

Read [AGENTS.md](AGENTS.md) first. Commits run formatting, lint, anti-slop checks, typecheck, the C# build, and plan governance ([docs/QUALITY.md](docs/QUALITY.md)). Larger changes need a plan in `docs/exec-plans/` ([docs/PLANS.md](docs/PLANS.md)).

## Architecture

```
web/   Vue 3 + TS + Vite, Tailwind v4, shadcn-vue (reka-ui), vue-router
api/   ASP.NET Core minimal API, EF Core 10 (SqlServer), background workers
       Camp.Api.Tests: xUnit, WebApplicationFactory against real SQL Server
db     SQL Server 2022 (docker)
```

### Correctness under load

- **No overbooking (FR-14):**
  - Each seat is claimed with a conditional `UPDATE CapacityPools SET Reserved = Reserved + 1 WHERE Id = @id AND Reserved < Capacity`, backed by a `CHECK (Reserved <= Capacity)` constraint.
  - The loser of a race for the last seat is placed on the waitlist in the same transaction. Waitlist positions are allocated under `UPDLOCK, HOLDLOCK`.
  - Covered by `Two_families_racing_for_the_last_seat_one_confirms_and_one_is_waitlisted`.
- **Pay once (FR-45):**
  - Every checkout carries a client idempotency key, stored under a unique index and also forwarded to the processor.
  - Double-clicks, retries, and concurrent duplicates all return the original order.
  - Covered by `Double_submit_with_the_same_idempotency_key_charges_once`.
- **SQL and the processor can't share a transaction.** Checkout runs in three steps:
  1. Claim seats and create `PaymentPending` registrations.
  2. Charge the card.
  3. Finalize, or compensate on decline: release seats and cancel.

  If the process dies between steps, `PendingPaymentReconciler` asks the processor what happened and finishes the order.

- **Integrations are async.** Confirmation emails (HubSpot) and CRM sync (Salesforce) go through a transactional outbox, so a slow third party never blocks checkout.
- **Money is computed server-side.** The UI never does money math. Quotes, discounts, and plan schedules come from `Pricing.Build`, and the final installment absorbs rounding so the schedule always sums to the total.

### Domain notes

- **Grade** uses a Sept 1 cutoff: `age on Sept 1 of the session year − 5`. Each camper lands in exactly one capacity pool by gender and grade.
- **Health forms depend on the program.** Day Camp uses the embedded form. Overnight uses CampDoc, where the status is synced back and staff never see the data.
- **Waitlist mode is admin-approved (FR-69).** A cancellation frees a seat, and staff offer it with a deadline. The offer holds the seat, and `WaitlistOfferExpiry` releases it if the family doesn't respond.
- **Payment-plan dates are corrected.** The concept screens put installments after camp. Here the last installment falls on the session's balance-due date (May 1 for a June camp).

## Scope

**In:**

- Discovery with live availability (P1/P2).
- Standard registration wizard (R1–R12).
- Family checklist (F1/F4-lite).
- Staff console:
  - Session overview (O1-lite)
  - Registrations table with filters and search (C4)
  - Registration detail with cancel and refund (C3)
  - Waitlist offers (C7)
  - Capacity editor (K3)
- Background workers.

**Out (stubbed or not built):**

- Admittance programs (marriage retreats) and cohort programs (leadership). They're discoverable, but checkout rejects them.
- Real Fiserv, HubSpot, Salesforce, and CampDoc integrations. They're simulated behind interfaces.
- Field-level encryption for health data, role-based access, reporting, and program setup beyond capacity.
- Guest self-service cancellation, accepting a waitlist offer, and paying a balance.
