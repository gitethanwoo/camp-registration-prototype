# WinShape Unified Platform: screen specs and self-review kit

For every agent generating a screen: you receive **Part 1 (seven-pass self-review protocol) + Part 2 (global contract) + Part 3 (canonical dataset) + your screen card from Part 4**. Generate, critique your own output against all seven passes in Part 1, fix, and regenerate. Maximum 3 rounds.

Source of truth: WinShape PRD (FR-1 to FR-121) and RFP. The MVP is Bucket A (must exist before registration opens) and Bucket B (must exist before the first event). Bucket C is out of scope for these screens.

---

## Part 1: Seven-pass self-review protocol

After every render, look at the image and run all seven passes. Be adversarial. Assume a WinShape product owner who wrote the PRD is grading it.

### Pass 1: Requirements
- For each FR on your card, point to the pixel region that satisfies it. If you can't, it's a **defect**.
- Did you show anything from Bucket C, a Won't Have, or another screen's job? Remove it. Won't Haves include seat-hold countdown timers and "I'm on the way / I'm here" arrival notices.
- Does the primary action on the screen match the job the user came to do?

### Pass 2: Data integrity (the numbers must add up)
- Every total equals the sum of its parts: registered + open = capacity, and assigned + unassigned = total.
- A count of "N need attention" is the count of **unique people**. If you break it down by reason, say the reasons overlap.
- Each row must agree with the summary. If a row shows "waiver missing", that person must be included in the waiver-missing count.
- Waitlisted people are only possible when their **capacity pool** is full, not merely when the session is.
- Money: line items + discounts − scholarships = total. Deposit + balance = total. Discounts and scholarships together never exceed 100% of cost.
- Dates are consistent with the session and with the grade/age math. Use Part 3 values only, and never invent a conflicting number.

### Pass 3: Domain truth (WinShape-specific)
- **CampDoc** is used by Overnight Camp (ON) only. Show completion **status** plus an "Open CampDoc" link, and never any health details from it. No other program shows CampDoc.
- **Payments:** card entry is Fiserv hosted fields inside our page (label it "Secure payment by Fiserv"). Never show a raw card form that looks like ours. Refunds and voids happen on the same screen as the payment history.
- **Admittance programs** authorize the card but don't charge it until approval. Copy must say "authorized, not charged".
- **Waitlist promotion** is approved by an admin (there's no auto-promote in v1). A promoted family gets a response/payment deadline.
- **Discounts** created by hosts or partners are inert until an admin approves them. To a guest, a pending code looks exactly like an invalid one.
- **Staff** sign in with Entra SSO. Staff screens never show a password field.
- **Scope:** an admin scoped to one ministry sees only that ministry. Health data is visible only to roles that need it.
- **Families** are one account with multiple members, co-owners and a checklist. Returning members are pre-filled, never re-typed.
- **Emails** are sent by HubSpot. Screens can show "Email sent" status but never an email editor inside our app.

### Pass 4: UX craft
- **Every screen needs paired layouts:** a 390px phone frame and a desktop frame. Guest screens are phone-first. Admin and host screens retain the same tasks and data on phone, using the shadcn-vue mobile Sidebar as a left-opening drawer instead of shrinking desktop tables or boards. Tap targets ≥44px, and the primary action within thumb reach.
- **Multi-select where the real world is multi** (kids, attendees, bulk actions): checkboxes, not radio buttons.
- **Say why.** Every warning, badge or "Needs review" states its reason in words, not just a color.
- **Handle the empty, error and edge states** on your card. Show at least one of them, as an inset or second frame.
- **Accessibility (WCAG 2.1 AA):** color isn't the only signal, contrast is sufficient, and every drag-and-drop has a keyboard or menu alternative.
- **No silent data loss:** prefer autosave with undo. If a Save button remains, show an unsaved-changes indicator.
- **Scale honesty:** design for real volumes (Part 3), not a 2-column demo. Show filtering, grouping or compact density where needed.
- **Copy:** plain, warm and specific ("12 spots left for Grade 6", not "Available").

### Pass 5: Consistency
- Uses the correct shell from Part 2: nav, header, scope switcher.
- Names, dates, prices and counts match Part 3 exactly.
- Status vocabulary matches Part 2.

### Pass 6: Component fidelity (shadcn-vue)
- Name the shadcn-vue component behind every visible element. Anything you can't name is a **defect**: redraw it with a real component.
- The admin and host shells are the shadcn `Sidebar` block, not a custom nav. On desktop the sidebar is on the **left**; on phone `SidebarTrigger` opens it from the **left**. Check for the header, groups, footer, the inset page with `SidebarTrigger` and `Breadcrumb`, and the scope switcher in `SidebarHeader`.
- Lists of records are a `Data Table`, not hand-drawn rows. Include the select column, sort indicators and a row-actions menu.
- Statuses are `Badge`s using the Part 2 vocabulary. Destructive actions go through an `AlertDialog`.
- Spacing and type look like Tailwind defaults (4px scale, `text-sm` body, `text-2xl`/`text-3xl` titles). No gradients, glassmorphism, drop-shadow-heavy cards or illustration clutter.

### Pass 7: Docs check (PRD + RFP attached)
- Quote the **exact PRD sentence** for each FR on your card, then say whether the screen satisfies its "Consequences (testable)" bullets. A missed testable consequence is a **high** defect.
- Check the bucket in PRD §6. If a feature on screen is Bucket C, remove it.
- If the screen contradicts the PRD, the PRD wins. Contradicting the dataset in Part 3 is also a defect.

### Required output after each round
```json
{
  "screen_id": "R1",
  "round": 1,
  "defects": [
    {"pass": "Data", "severity": "high", "issue": "Summary says 1 camper but 2 are checked", "fix": "Bind summary to selection: 2 campers · $650"}
  ],
  "fr_coverage": {"FR-30": "checkbox list, top-left", "FR-14": "MISSING"},
  "verdict": "regenerate | ship"
}
```
Ship only when there are zero high-severity defects and every FR on the card is covered. After round 3, ship anyway and list the remaining defects.

---

## Part 2: Global contract

### Shells (use exactly one per screen)
| Shell | Used by | Layout |
|---|---|---|
| **Guest** | Public, auth, family portal, registration | Top bar: WinShape logo, Programs, My family, Help, avatar. Phone: bottom tab bar (Programs, My family, Checklist, Account). |
| **Admin console** | CET, config, finance, operations | Left sidebar, grouped: *Front desk* (Search, Registrations, Applications, Waitlists, Discounts, Documents) / *Operations* (Sessions, Groups, Rooming, Activities, Check-in) / *Finance* (Reports, Reconciliation, Payment plans) / *Setup* (Ministries, Programs, Forms, Waivers, Users, Integrations, Audit log). **Scope switcher `Ministry ▸ Program ▸ Session`** in the sidebar header, global search (⌘K), staff avatar with role in the sidebar footer. |
| **Host portal** | Host church staff | The same shadcn Sidebar block, slimmer. Header: host org name, e.g. "Grace Community Church · Host". Nav: Home, Volunteers, Invoices, Reports. |
| **Secure link** | Group event participants without an account | Minimal: logo, "Completing forms for {event}", no nav. |

Label every image in small type in the corner: "Concept · not final design".

### Design system: shadcn-vue + Tailwind (build-faithful)
Every screen must be buildable from **shadcn-vue** components (Reka UI primitives) styled with **Tailwind v4**, using the default "new-york" style and neutral base color. Draw only what those components can render. The rule is: if a developer can't build it with shadcn-vue plus Tailwind utilities in an afternoon, don't draw it.

**Admin console and host portal shells = the shadcn-vue `Sidebar` block** (the sidebar-07 pattern). Place it on the **left** on desktop; on phone, its `SidebarTrigger` opens the left sidebar as a drawer:
- `SidebarProvider` › `Sidebar collapsible="icon"` › `SidebarHeader` (logo + **scope switcher** built as a `DropdownMenu`: Ministry ▸ Program ▸ Session) › `SidebarContent` with `SidebarGroup` + `SidebarGroupLabel` per nav section (Front desk / Operations / Finance / Setup) › `SidebarMenuButton` items with lucide icons and `SidebarMenuBadge` counts › `SidebarFooter` (staff `Avatar` + name + role in a `DropdownMenu`).
- `SidebarInset` holds the page: a header row with `SidebarTrigger`, `Separator`, `Breadcrumb`, then the page title, then content.
- Global search is a `Command` dialog (⌘K).

**Component map (use these, not look-alikes):**
| Need | shadcn-vue component |
|---|---|
| KPI tiles | `Card` (`CardHeader`/`CardTitle`/`CardDescription`/`CardContent`) |
| Tables, rosters, queues | `Data Table` (TanStack) with `Checkbox` row select, column sort, `DropdownMenu` row actions, `Pagination` |
| Statuses | `Badge` (variants: default / secondary / destructive / outline + Tailwind color classes for success/warning) |
| Tabs within a page | `Tabs` |
| Record detail from a list | `Sheet` (side panel), or a full page for heavy records |
| Confirmations (refund, void, merge, approve) | `AlertDialog` |
| Forms | `Form` + `FormField` + `Input` / `Select` / `Combobox` / `Checkbox` / `RadioGroup` / `Switch` / `Textarea` / `DatePicker` / `NumberField` |
| Wizard steps | `Stepper` |
| Capacity bars | `Progress` |
| Warnings and banners | `Alert` (default / destructive) |
| Toasts ("Saved · Undo") | `Sonner` |
| Loading | `Skeleton` |
| Filters | `Popover` + `Command` faceted filter (the Data Table example pattern), `ToggleGroup` |
| Charts | shadcn-vue `Chart` (Area / Bar / Line / Donut) |
| Phone bottom sheets | `Drawer` |
| Drag boards (groups, rooming) | Columns of `Card`s in `ScrollArea`, with drag handles, plus a `DropdownMenu` "Move to…" fallback |

**Tokens:** use shadcn CSS variables (`--background`, `--foreground`, `--primary`, `--muted`, `--border`, `--ring`, `--destructive`, `--chart-1..5`). The primary is near-black (shadcn default). WinShape red appears only in the logo. The radius is `0.5rem`. The font is Inter or Geist.

**Guest shell:** not the sidebar. Use a top `NavigationMenu` on desktop and a fixed bottom tab bar (plain Tailwind) on phone. Use the same components and tokens.

### Status vocabulary (use these exact words)
- **Registration:** Draft · Application pending · Approved · Payment pending · Confirmed · Waitlisted · Offered spot · Cancelled · Transferred
- **Payment:** Paid · Deposit paid · Balance due · Plan active · Installment failed · Authorized (not charged) · Refunded · Voided
- **Forms:** Complete · Incomplete · Missing · Not required
- **Integration:** Synced · Pending · Failed (retrying) · Needs attention

---

## Part 3: Canonical dataset

Use only these values. For the extra people a screen needs, generate plausible names, but totals must stay as stated.

### Ministries and programs
- **WSC Camps:** Overnight Camp (ON), Day Camp (WSCC, run by host churches), Family Camp (FC)
- **WSM Marriage:** marriage retreats (admittance-based, couples = group registration)
- **WSL Leadership:** leadership cohorts (admittance-based, group leader registers the cohort)
- **WSCP College Program:** applications, interviews, scholarships
- **WSH Homes:** foster/adoption (minimal in MVP)

### Sessions
| Session | Dates | Capacity | Price | Notes |
|---|---|---|---|---|
| **ON · Session 3** | July 10–15, 2028 | 200 in 4 pools: Boys G3–5 (50), Boys G6–8 (50), Girls G3–5 (50), Girls G6–8 (50) | $1,450 · $250 deposit | Registered **186** = Boys G3–5 46, **Boys G6–8 50 (FULL)**, Girls G3–5 44, Girls G6–8 46. **Waitlist 9, all Boys G6–8.** Ready **159**, needs attention **27 unique** (19 CampDoc incomplete, 6 waivers missing, 8 balance due; reasons overlap). Rooming: 16 cabins × 12 beds (8 boys, 8 girls). Activities: Archery, Swimming, Climbing, Horseback, Crafts, Canoeing (24 slots each per period, 3 periods). Uses CampDoc. |
| **Day Camp · Atlanta (Grace Community Church)** | June 12–16, 2028 | 120 by grade; **Grade 6: 20 cap, 12 left** | $325 · $100 deposit · plan: 3 monthly payments | Embedded health form (no CampDoc). Host: Grace Community Church. |
| **WSM · Fall Marriage Retreat** | Oct 6–8, 2028 | 40 couples | $900/couple | Admittance-based, so the card is authorized, not charged. Uses the retreat center, so room capacity is from Oracle Opera. |
| **WSL · Emerging Leaders Cohort** | Sept 2028 | 60 attendees | $450/attendee | Group leader: Carmen Ortiz registers 14 attendees; 9 complete, 5 incomplete. |

### Families and people
- **Johnson family** (returning; attended the WSM retreat in 2026): Maria Johnson (primary), David Johnson (co-owner), **Avery** (boy, DOB Mar 4, 2017, Grade 6 in fall 2028), **Mia** (girl, DOB Aug 19, 2019, Grade 4 in fall 2028). Maria registers both kids for Day Camp: 2 × $325 = $650, $200 deposit today, 3 × $150 plan.
- **Staff:** Diane Carter (CET, all ministries), Brian Hughes (Operations, WSC), Marcus Reed (Finance), Priya Shah (Data analyst), Jamie Dalton (ON camp director).
- **Host:** Renata Alvarez, Operations Director, Grace Community Church (Rome, GA): 40-volunteer batch upload, 37 valid and 3 with errors.
- **Group leader:** Carmen Ortiz (WSL).
- **Duplicate example:** "Jordan Lee" (jlee@…, 2 accounts); one account has an active payment plan and the other has a waitlist spot, so there's a conflict to resolve.

---

## Part 4: Screen cards

Each card has these fields:
- **Shell** · **Bucket** · **FRs**
- **Job:** the one thing the user came to do
- **Must show:** required content
- **States:** at least one to render as an inset
- **Traps:** what a reviewer will catch

### Public and discovery

**P1 · Program detail page**
- Guest · A · FR-13, 14, 32
- **Job:** decide whether this program fits and register.
- **Must show:** description, dates, location, price and deposit, age/grade eligibility, requirements (waiver, health form type), a session list with **live availability per grade/pool**, and a Register CTA per session.
- **States:** a full pool shows "Join waitlist"; last few spots; Opera unreachable shows "Availability as of 2:14 PM" with an indicator.
- **Traps:** availability shown only per session and not per grade; no price; a CTA on a full session that says "Register".

**P2 · Live availability component** (appears inside P1 and R1)
- Guest · A · FR-14
- **Must show:** a count per pool ("12 spots left for Grade 6"), a low-stock treatment at 5 or fewer, a full pool leading to the waitlist, and a refresh cue ("updated just now").
- **Traps:** vague "Available"; a seat-hold countdown (Won't Have).

**P3 · Activity / skill detail**
- Guest · A · FR-24
- **Must show:** photo or media, description, age limits, remaining slots per period, and a "Select for Avery" action when in the registration context.
- **Traps:** no capacity shown.

**P4 · Private event access gate**
- Guest · A · FR-33
- **Must show:** a secret-link landing, or entry of an access code or email match; the event name once unlocked.
- **States:** invalid code, which uses the same message style as an invalid discount code.
- **Traps:** leaking event details before unlock.

### Auth
Sign-in, sign-up and password reset are hosted by WorkOS, so there's no screen to build.

**AU1 · Accept invitation**
- Guest · A · FR-8, 34
- **Job:** accept a co-owner invite, e.g. David joining Maria's family.
- **Must show:** who invited you, what access you get (register members, see history, payments), and Accept / Decline.
- **States:** expired invite.
- **Traps:** implying full control over payment methods the other adult added.

**AU2 · Account recovery (no access to email)**
- Guest · A · FR-5
- **Must show:** the documented fallback path: identity questions or verification, then a request routed to CET, with a clear timeline.
- **Traps:** a dead end at "check your email".

### Family portal

**F1 · Family home + checklist**
- Guest · A · FR-3, 27, 10 (lite)
- **Job:** see what's still needed.
- **Must show:** member cards (Maria, David, Avery, Mia); upcoming registrations; **one combined checklist across all kids** (waivers, balance due, health, activity selection), each with a do-it-now action; completed items marked done.
- **States:** everything complete ("You're all set for Day Camp").
- **Traps:** a checklist per registration instead of one list; items with no action.

**F2 · Member profile add/edit**
- Guest · A · FR-3, 21
- **Must show:** name, DOB, grade (derived and editable), and basic health (dietary, allergies, ADA needs). Household contact and payment are inherited and not re-asked.
- **States:** an age-of-majority prompt ("Avery can now claim his own account").
- **Traps:** asking for the address again.

**F3 · Household access**
- Guest · A · FR-8, 6
- **Must show:** adults with access and their permission level, Invite adult, Revoke; a shared dependent linked to another household (show as "Also linked to: …").
- **Traps:** revoking a co-owner silently removes guardian links.

**F4 · My registrations**
- Guest · A · FR-50, 10
- **Must show:** registrations grouped as upcoming vs past, across ministries (the WSM 2026 retreat appears in past), each with status and balance.
- **Traps:** splitting by ministry into separate lists.

**F5 · Registration detail (family view)**
- Guest · A · FR-27, 42, 50
- **Must show:** participant, session, status, a checklist for this registration, payment summary, and actions: Request transfer, View receipts.
- **Traps:** a self-edit button (Bucket C); cancel as self-serve (Bucket C beyond the refund policy).

**F6 · Payments and receipts**
- Guest · A · FR-47, 48, 50
- **Must show:** each payment with date, amount, method (last 4), receipt link; plan schedule with next charge date; a failed-installment banner with a Retry button.
- **States:** installment failed, with the grace period stated.
- **Traps:** total balance across all registrations (FR-54 is Bucket C; keep it per registration).

**F7 · Application status**
- Guest · A · FR-26, 46
- **Must show:** WSM retreat application progress (Submitted, Under review, Decision), "Card authorized, not charged", and the next step.
- **States:** authorization expired, so re-enter payment to keep your place.
- **Traps:** saying "charged".

**F8 · Session transfer request**
- Guest · A · FR-42
- **Must show:** current session, a destination picker limited to **another session of the same program** with live availability, any price difference, a warning if destination-session requirements need review, a reason field, and Submit. The original registration remains intact until Admin approves.
- **States:** request denied with a reason, while the original registration stays intact.
- **Traps:** implying an instant move without admin review.

### Registration wizard
Steps change by program. Show a stepper, and keep the **summary panel** (sticky; a bottom sheet on phone) live with participants, price, deposit and availability.

**R1 · Select participants**
- Guest · A · FR-30, 14
- **Must show:** **checkboxes** for Avery and Mia, pre-filled and read-only with "Edit profile"; eligibility per child ("Mia, Grade 4 · 9 spots left"); "+ Add a family member"; the summary updates to "2 campers · $650".
- **States:** one child ineligible (grade out of range), with the reason.
- **Traps:** radio buttons; editable name/DOB fields (which create duplicates).

**R2 · Admittance application**
- Guest · A · FR-26
- **Must show:** a multi-section application (WSM couple), save-and-continue, progress, and a note that the card will be authorized, not charged.
- **Traps:** looking identical to a simple registration.

**R3 · Program questions**
- Guest · A · FR-74 (rendered)
- **Must show:** configured questions rendered per participant (e.g. T-shirt size, church affiliation), with required markers and conditional logic visible (one follow-up question revealed).
- **Traps:** asking the same household question once per child.

**R4 · Activity selection**
- Guest · A · FR-23, 24
- **Must show:** per child, ranked choices or a pick for each period, remaining slots, and a link to details.
- **States:** an activity just filled, with an alternative suggested.
- **Traps:** no per-child separation.

**R5 · Cabinmate request (ON)**
- Guest · A · FR-25
- **Must show:** request up to N friends by name plus a parent email or code, a "requests aren't guaranteed" note, and mutual-request explainer copy.
- **Traps:** looking like a guaranteed room pick.

**R6 · Health step**
- Guest · A · FR-20, 21, 22, 112. Render all 3 variants.
- **(a) Day Camp:** embedded form with an "encrypted, visible only to camp staff" note.
- **(b) Third-party form:** embedded frame.
- **(c) ON:** "Health forms are completed in CampDoc" with Open CampDoc and a status that updates on return. No CampDoc account is required for Day Camp.
- **Traps:** showing CampDoc for Day Camp; displaying CampDoc health data.

**R7 · Waivers**
- Guest · A · FR-37
- **Must show:** each waiver with a scrollable text, version and date, an agree checkbox per participant where needed, and the signer's name.
- **Traps:** a single "I agree to all" with no text visible.

**R8 · Group / cohort roster entry**
- Guest · A · FR-35, 34
- **Must show:** Carmen adds attendees with **only name and email required**, rows can be partial, a paste-or-upload option, and a note that "each attendee completes their own forms by link".
- **Traps:** requiring full attendee data upfront.

**R9 · Review and cart**
- Guest · A · FR-19, 62
- **Must show:** per-participant line items, a discount code field, the total, and deposit vs pay in full vs plan choices with the schedule shown.
- **States:** invalid code, with an identical message for pending codes.
- **Traps:** math that doesn't add up.

**R10 · Checkout**
- Guest · A · FR-45, 46, 47, 48
- **Must show:** amount due today ($200), plan schedule (3 × $150 on the listed dates), **Fiserv hosted card fields** (labelled), saved card option, Pay. For admittance programs: "Authorize $900 (not charged until approved)".
- **States:** card declined, inline.
- **Traps:** a pay button that allows double submit (show a disabled/processing state); the full amount shown when a deposit was chosen.

**R11 · Confirmation**
- Guest · A · FR-28, 27
- **Must show:** "You're registered" for both kids, confirmation number, "Email sent to maria@…", and **the combined checklist of what's left**, with a CTA to the first item.
- **Traps:** a dead-end thank-you page.

**R12 · Waitlist / last spot taken**
- Guest · A · FR-36, 14
- **Must show:** "Boys Grades 6–8 filled while you were registering. You're #10 on the waitlist", what happens next (admin approval, response deadline when offered), no charge.
- **States:** "Spot offered: confirm and pay by Jul 1".
- **Traps:** a generic error; silent overbooking.

### Group event participants

**G1 · Secure-link form completion**
- Secure link · A · FR-34, 37
- **Must show:** "Carmen Ortiz registered you for the Emerging Leaders Cohort", required waivers and forms, a contact info field (email required), Submit; an optional "create an account" prompt afterwards; a withdrawal request link.
- **Traps:** showing other attendees; requiring an account.

**G2 · Group leader completion tracker**
- Guest · A · FR-34, 35
- **Must show:** 14 attendees with 9 complete and 5 incomplete, per-attendee status, Resend link, and a note that "you can't complete forms on their behalf".
- **States:** a pending withdrawal request awaiting Carmen's refund approval.
- **Traps:** an edit-form button on attendee rows.

### Admin: front desk (CET)

**C1 · Global search**
- Admin · A · FR-61
- **Must show:** one search bar (name/phone/email) with results showing households across ministries, member names and a recent activity snippet.
- **Traps:** a ministry filter that is required before searching.

**C2 · Household 360**
- Admin · A · FR-61, 65, 66, 10, 7
- **Must show:** members, adults with access, **all registrations across ministries**, balances, internal notes (timestamped, author), a verification checklist, a "possible duplicate" flag, and the Salesforce link/status.
- **Traps:** health data visible to CET by default; notes with no author.

**C3 · Registration detail (admin)**
- Admin · A · FR-49, 50, 70, 39
- **Must show:** status timeline, documents status, payment history with **Void (within window) / Refund actions on the same screen**, cancellation policy applied (refund amount calculated), and the audit trail snippet.
- **States:** original card expired, so a fallback refund method (check, ACH, credit) is offered.
- **Traps:** refund in a separate finance tool; refund without a reason field.

**C4 · Registration dashboard**
- Admin · A · FR-61
- **Must show:** a filterable table (ministry scope, session, status, balance), saved views, bulk actions, and today's counts.
- **Traps:** totals that don't match Part 3.

**C5 · Document tracking**
- Admin · A · FR-70
- **Must show:** a participants × required documents matrix for ON Session 3 (waiver, CampDoc status, photo release), filter to missing, and bulk "Send reminder" (via HubSpot).
- **Traps:** showing CampDoc content.

**C6 · Application review queue**
- Admin · A · FR-64, 46
- **Must show:** WSM applications by stage, the application reader, Approve / Reject / Request info, and the payment state ("Authorized · expires in 3 days").
- **States:** authorization expiring, so the approval triggers a re-auth request.
- **Traps:** approve without seeing the payment state.

**C7 · Waitlist management**
- Admin · A · FR-69, 36
- **Must show:** ON Session 3 Boys G6–8 waitlist (9), ordered, with the open spots count (0); Offer spot (with a deadline picker), Skip, Remove; offered entries with countdowns.
- **Traps:** auto-promote in v1; a waitlist while the pool shows open spots.

**C8 · Discount approval queue**
- Admin · A · FR-62, 63
- **Must show:** pending discounts from hosts and partners, with the **full rule config** (flat vs %, stacking/override, sessions, dates, max uses) and Approve / Reject with a note; an over-threshold badge.
- **Traps:** approving from a list without seeing the config.

**C9 · Duplicate review and merge**
- Admin · A · FR-7
- **Must show:** two Jordan Lee accounts side by side, a match reason ("same name + phone + DOB"), a field-by-field survivor picker, combined registration history, and a **conflict panel: active payment plan vs waitlist spot, requiring an explicit resolution choice**; a Salesforce merge note.
- **Traps:** one-click merge; silently discarding the conflict.

**C10 · Transfer request review**
- Admin · A · FR-42
- **Must show:** requested destination in the **same program** with live capacity, any price difference, destination-session requirement review, Approve / Deny with a reason, and a note that payment adjustment and the move are all-or-nothing.
- **Traps:** approving into a full pool.

### Admin: setup

**K1 · Ministry settings**
- Admin · A · FR-73
- **Must show:** per-ministry defaults (branding, contact, policies, approval rules, payment settings), inherited by programs.
- **Traps:** global settings with no ministry scope.

**K2 · Program list / editor**
- Admin · A · FR-67, 73
- **Must show:** programs per ministry, type (single session / admittance / cohort), attached forms and waivers, publish status, and the approval chain.
- **Traps:** publish with no approval state.

**K3 · Session editor + capacity pools**
- Admin · A · FR-68, 67, 106
- **Must show:** dates, location (owned retreat center shows "Room capacity from Oracle Opera", read-only), **capacity pools** (the Part 3 ON pools), registration open/priority dates, and waitlist mode (Admin approval, locked in v1).
- **Traps:** a single capacity number.

**K4 · Pricing and policies**
- Admin · A · FR-47, 48, 49
- **Must show:** price, deposit, plan templates (count, dates), and the cancellation/refund policy as a time-based table.
- **Traps:** the policy as free text.

**K5 · Discount rules**
- Admin · A · FR-62
- **Must show:** a rule builder (type, value, stacking, session scope, dates, usage cap) with a preview ("Avery: $325 → $292.50").
- **Traps:** a combination that could exceed 100%; show the guard.

**K6 · Registration form / question builder** (the hardest config screen)
- Admin · A · FR-74, 20
- **Must show:** question types, per-participant vs per-household scope, required, conditional logic, a live phone preview, versioning, and "changes require approval" status.
- **Traps:** looking like a generic survey tool with no scope or version.

**K7 · Waiver templates**
- Admin · A · FR-37, 103
- **Must show:** waiver text editor, versions with effective dates, attached programs, and "families accepted v3 on …".
- **Traps:** editing a live version in place.

**K8 · Activity catalog**
- Admin · A/B · FR-23, 24, 81
- **Must show:** activities with media, age limits, default capacity per period, and staff/space requirements.

**K9 · Health collection settings**
- Admin · A · FR-20, 22, 112
- **Must show:** per program/session, a choice of mechanism (embedded, third-party form, CampDoc), plus who can view health data (role list).
- **Traps:** CampDoc selectable for non-ON programs without a warning.

**K10 · Workflow stages**
- Admin · A · FR-67, 26
- **Must show:** application stages for WSM (Submitted, Review, Interview, Decision), who approves each, and notifications emitted at each stage (sent by HubSpot).

**K11 · Staff users and roles**
- Admin · A · FR-2, 6
- **Must show:** users synced from Entra, role, **ministry scope**, health-data access flag, last sign-in; deprovisioned users are shown as revoked automatically.
- **Traps:** a password reset action; "Add user" by email with a password.

**K12 · Audit log**
- Admin · A · FR-103
- **Must show:** an immutable list of who / what / when / before-after for discounts, refunds, access changes and health data access; filters; export. No edit or delete.

**K13 · Integration health**
- Admin · A · FR-100, 104, 106, 45, 107
- **Must show:** a card per system (Salesforce, HubSpot, Fiserv, Opera, CampDoc, Fusion) with last sync, failed count, and a retry queue with the error reason per item; a named owner per integration.
- **States:** Opera failed (retrying), with the effect on availability explained.

**K14 · Migration exception review**
- Admin · A · FR-110
- **Must show:** source (WSC WIN / WSCP WIN / Cvent), batch, validation results (valid / flagged / rejected counts), a flagged-record reader (malformed, suspected duplicate) with Fix / Merge / Exclude, and a "test records excluded by rule" tally.
- **Traps:** an "Import all" button.

### Admin: finance

**FN1 · Reports**
- Admin · A · FR-72
- **Must show:** registrations, revenue, attendance and demographics by ministry/program/session and date range, with a certified-metric badge on governed measures, and export.
- **Traps:** revenue that doesn't tie to FN2.

**FN2 · Reconciliation**
- Admin · A · FR-71
- **Must show:** a Fiserv settlement batch vs platform payments, matched / unmatched / fee lines, drill into an unmatched item with Resolve, and the Fusion journal status per batch.
- **Traps:** no unmatched state shown.

**FN3 · Payment plan exceptions**
- Admin · A · FR-48
- **Must show:** failed installments, retry schedule, grace-period end, days to policy action, and Contact family / Retry now.

**FN4 · Fusion journal export**
- Admin · B · FR-111
- **Must show:** journal batches (date, entries, total, status: Posted / Failed / Pending) and the error detail on failure.

### Admin: operations

**O1 · Session readiness**
- Admin · B · FR-27, 70, 80, 107
- **Must show:** ON Session 3 tiles (Registered 186/200, Waitlisted 9, Ready 159, Needs attention 27); a roster with Payment / Waiver / CampDoc / Cabin / Activity columns; a Needs-attention breakdown (19 CampDoc, 6 waivers, 8 balance; overlap noted); bulk remind; a pool breakdown showing Boys G6–8 full.
- **Traps:** 27 = one reason only; a row contradicting the counts.

**O2 · Group assignment board**
- Admin · B · FR-80
- **Must show:** a filter (Boys G6–8 = 50 campers), groups with capacity, a card per camper with grade, **request-match tag** and review reason; an "Auto-suggest" button (human approves), autosave + undo, a keyboard "Move to…" menu; totals that sum to 50.
- **Traps:** 2-column toy; a batch Save with no unsaved indicator.

**O3 · Rooming board**
- Admin · B · FR-82, 25, 106
- **Must show:** 16 cabins (12 beds each) split by gender, cabinmate requests (met / unmet), conflicts, a re-review flag after roster changes, and "Retreat center rooms from Opera" where applicable.
- **Traps:** mixed-gender cabins; ignoring requests.

**O4 · Activity schedule builder**
- Admin · B · FR-81, 85
- **Must show:** a grid of 6 activities × 3 periods with capacity (24), assigned counts, instructor slot and space, conflicts (a camper double-booked, over capacity), and assignment from camper preferences.

**O5 · Check-in / check-out**
- Admin · B · FR-84
- **Must show:** tablet layout, search/scan, per-camper status with blockers ("Balance due · Waiver missing: resolve before check-in"), authorized pickup adults at check-out.
- **Traps:** checking in a camper with outstanding blockers with no warning.

**O6 · Scholarship application (family)**
- Guest · B · FR-38
- **Must show:** a short application, household info pre-filled, document upload, status.

**O7 · Scholarship review**
- Admin · B · FR-53
- **Must show:** a queue, the application reader, award amount with a **cap check** (scholarship + discounts ≤ 100%), Approve / Deny.

**O8 · Interview scheduling and scoring**
- Admin + Guest · B · FR-41, 86
- **Must show:** available slots (family books), an interviewer view with a scoring rubric and a notes field.

### Host portal

**H1 · Host home + reports**
- Host · B · FR-87
- **Must show:** Grace Community Church Day Camp: registrations vs last year, volunteers (status), invoice balance, and upcoming deadlines.
- **Traps:** Bucket C dashboard depth (month filters, YoY drilldowns belong to FR-96).

**H2 · Volunteers + batch upload**
- Host · B · FR-88
- **Must show:** the volunteer list, Upload CSV with a template link, **a validation preview (37 valid, 3 errors with row-level reasons)**, fix inline or skip, Submit, then routed to vetting with statuses.
- **Traps:** an upload that silently drops bad rows.

**H3 · Invoices + pay**
- Host · B · FR-89
- **Must show:** invoices (period, amount, status), invoice detail with line items, and Pay via Fiserv hosted fields.

---

## Agent prompt (copy-paste)

Attach to each agent: the **PRD PDF**, the **RFP PDF**, and this file. Then send:

```
You are a senior product designer producing ONE screen for WinShape Foundation's
Unified Registration & Operations Platform proposal. Your screen: {SCREEN_ID}.

SOURCES (read before drawing, in this order of authority):
1. The PRD (attached): functional requirements FR-1..121, their "Consequences
   (testable)" bullets, and §6 MVP buckets A/B/C.
2. The RFP (attached): stack floor (.NET / SQL Server / Vue / Azure), mobile-first,
   WCAG 2.1 AA, auditability, Opera/HubSpot/Fiserv/CampDoc constraints.
3. winshape-screen-specs.md (attached): Part 2 global contract, Part 3 canonical
   dataset, and your card in Part 4.

BUILD TARGET: Vue 3 + shadcn-vue + Tailwind v4. Draw only what shadcn-vue components
and Tailwind utilities can build. Admin/host screens use the shadcn-vue Sidebar block
(SidebarProvider > Sidebar collapsible="icon" > SidebarHeader with a
Ministry ▸ Program ▸ Session scope switcher > SidebarGroups > SidebarFooter user menu;
page in SidebarInset with SidebarTrigger + Breadcrumb). Record lists are Data Tables;
statuses are Badges; destructive actions use AlertDialog; wizards use Stepper.
For every screen, generate both phone (390px) and desktop layouts. Guest screens
are phone-first. Admin/host desktop screens keep the Sidebar on the left; their
phone layouts use SidebarTrigger to open the same navigation from the left.
Treat PWA as a possible delivery form factor, not a request to invent offline
features or install prompts absent from the PRD.

PROCESS:
Step 1. Before drawing, write a short plan:
  - Each FR on your card, quoted from the PRD, with its testable consequences.
  - The exact dataset values you will show (names, dates, counts, money).
  - The shadcn-vue component for every region of the screen.
  - The edge state you will render as an inset.
Step 2. Generate both responsive layouts of the screen.
Step 3. Look at both images and run self-review Passes 1-7 from Part 1 of the spec.
  Be adversarial: a WinShape product owner who wrote the PRD is grading it.
  Check especially:
  - every testable consequence is visibly satisfied;
  - every number adds up and matches the dataset;
  - WinShape domain rules (CampDoc = ON only, status + link; Fiserv hosted fields;
    authorized-not-charged for admittance; admin-approved waitlist; pending discount
    codes look invalid; Entra SSO for staff);
  - multi-select is checkboxes, warnings say why, no silent data loss, keyboard
    alternative to drag;
  - every element maps to a named shadcn-vue component; no invented widgets;
  - nothing from Bucket C.
Step 4. Output the JSON critique (format in Part 1). If the verdict is "regenerate",
  fix every defect and repeat Steps 2-4. Stop at "ship" or after 3 rounds.

DELIVER: final phone and desktop images, the final JSON critique of both, and a component list
(region → shadcn-vue component) a Vue developer could build from.
```
