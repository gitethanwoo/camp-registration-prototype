# Exec plans

- Active: `docs/exec-plans/active/`
- Completed: `docs/exec-plans/completed/`
- Plan rules: `docs/PLANS.md`
- MicroPlan template: `docs/exec-plans/active/_template.md`
- ExecPlan template: `docs/exec-plans/active/_exec-template.md`
- Finish a plan: `node scripts/complete-exec-plan.mjs <slug> [Completed|Abandoned|Shelved]`

## Thresholds (enforced by `npm run check:governance`)

- More than 50 changed code lines needs a MicroPlan or an ExecPlan in the same diff.
- More than 250 changed code lines needs an ExecPlan.
- Touching a critical file needs an ExecPlan. Critical files are the schema (`Data/Entities.cs`, `Data/CampDbContext.cs`), money (`Features/Checkout.cs`, `Pricing.cs`, `PaymentGateway.cs`), auth (`api/Camp.Api/Auth/**`), and `Program.cs`.
- Migrations, lockfiles and docs don't count toward the thresholds.
- A changed non-test code file can't exceed 1200 lines.
- `e2e/**` can't use `waitForTimeout(...)` unless the line says `governance: allow-waitForTimeout` and gives a reason.

## Lifecycle

A plan's `- Status:` has to match its directory. The check scans every plan on every run.

- `active/` allows `In Progress` or `In Review`. Each active plan needs a dated `## Progress` entry.
- `completed/` allows `Completed`, `Done`, `Abandoned`, or `Shelved`. A completed plan can't leave `- [ ]` validation items unchecked.
