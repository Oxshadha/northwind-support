# Approach

This document complements the [README](README.md). The README explains what was implemented and how to run it. Here, I explain the process behind the work: how I interpreted the requirements, investigated the starter code, made and revised design decisions, addressed problems, and verified the result. Commit messages provide a short history; the reasoning and supporting evidence are recorded here.

For a quick overview, start with Sections 1 and 2. The remaining sections provide the requirement-by-requirement evidence, design rationale, verification results, and commit history.

## Contents

1. [Work overview](#1-work-overview)
2. [Development approach](#2-development-approach)
3. [Requirement trace](#3-requirement-trace)
4. [Architecture](#4-architecture)
5. [How the stack made the work easier](#5-how-the-stack-made-the-work-easier)
6. [Reuse](#6-reuse)
7. [Decision records](#decision-records)
8. [Implementation notes by task](#8-implementation-notes-by-task)
9. [Verification log](#9-verification-log)
10. [Known limitations and future improvements](#10-known-limitations-and-future-improvements)
11. [Commit map](#11-commit-map)

---


## 1. Work overview

| Phase | What I did | Notable finding | Commits |
| --- | --- | --- | --- |
| Understand | Read the brief, the starter code and the tests; mapped every requirement ID; listed hidden traps | Repository README and assignment disagree (Task 4 "optional", APPROACH.md); DEFECT-104 exists only in the PR template | |
| Task 1 | Regression tests first, then the hook fix; filters in SQL | First fix still sent a request per keystroke; lint rule `react-hooks/set-state-in-effect` forced a cleaner design | `fe6877d` `2a8384e` `38c0db4` |
| Task 2 | Researched how real routing engines assign and tie-break; built rules as small domain classes; lock for concurrency | References jumped to TCK-1003 (identity gaps); the original create path failed 11 of 12 concurrent requests | `53f11ae` `7c2f67b` `f7ad1b6` |
| Task 3 | Researched OWASP hashing guidance, enumeration CVEs and token storage; central policy; integration tests | Default PBKDF2 iterations below OWASP; dummy-hash timing verified by measurement | `6412404` `4233251` |
| Task 4 | Found two image traps before writing the Dockerfile | `appsettings.Development.json` would have been baked in; chiseled images fail the brief's own `whoami` check | `5cb1110` |
| Stretch | SLA summary report as one aggregate | `DATEDIFF(minute)` counts boundaries, not elapsed time | `86fb9ff` `a47afce` |
| Clean-up | Security pins, a sort bug, stale comments | The first pin (10.0.12) was still vulnerable; the advisory says 10.0.13 | `58821e6` `7f53d27` `dba6199` `00d37ee` |
| Closing gaps | Re-read my own "known limitations" and fixed the ones in scope | A deactivated agent kept access for up to 8 h; EF could not translate a null check on a left-joined projection | `58e5458` `b45a539` `f847408` |

---

## 2. Development approach

### 2.1 Understanding the requirements

I first converted the brief into a checklist of requirement IDs (FL-1 to FL-6, BR-1 to BR-8, AU-1 to AU-13, DK-1 to DK-8, plus the unnumbered bullets in 2.2, 2.3 and 2.4). Section 3 maps those requirements to implementations and evidence. I used the scoring criteria to prioritize: correct status codes and validation, rules in small testable units, one SLA indicator and a custom data hook, central authorization, a cause-level defect fix, boundary tests, narrow SQL projections with filters before paging, and honest documentation.

The repository README and assessment brief did not fully agree: the README described Task 4 as optional and requested an APPROACH.md file, while the assessment did not. I followed the stricter interpretation: I completed Task 4 and documented my approach here.

### 2.2 Identifying likely failure cases

Several requirements had edge cases that were easy to miss in a straightforward implementation. Before starting each task, I identified where a seemingly correct solution could break:

| Trap | Why it matters | What I did |
| --- | --- | --- |
| At risk after an escalation | Escalation restarts the clock, so "25 % of the window" cannot be measured from creation | Stored the window length (`SlaWindowSeconds`) |
| An agent at their limit escalating their own ticket | A literal "strictly below the limit" check would take the ticket away from them | Counted their load without that ticket (decision D4) |
| The 1 h floor | With the default settings the shortest premium window is 2 h, so a test using the defaults never exercises the floor | Tested the floor with its own settings |
| Architecture tests | Application and Domain may not reference ASP.NET Core, so "who is signed in" cannot be read from `HttpContext` there | `ICurrentUser` in Application, implemented in Presentation |
| The seeder skips a database that has tickets | Existing developer databases would never get passwords | Credentials are seeded separately, every start-up, for agents without one |
| Migrate only in Development | The container and the tests both need migration control independently | An explicit setting |
| Publish copies `appsettings.Development.json` | Would bake the SA password and dev JWT key into the image (DK-4) | Excluded from publish, verified inside the container |
| Chiseled base images | No shell, so `docker compose exec api whoami` (the brief's own check) fails | Standard image with the non-root user; chiseled noted as the production step |
| DEFECT-104 | Mentioned only in the PR template | Recorded as an assumption, not invented |

### 2.3 Evaluating design decisions

For decisions with significant consequences or high rework cost, I followed three steps:

1. **Research** what real systems do. For example: how ServiceNow, Zendesk and Salesforce assign work and break ties; OWASP's password storage figures; real CVEs where unknown users were detectable by timing; how SLA breach rate is defined in reporting tools.
2. **Write a short "decision, challenge, verdict" table** and argue against my own first idea. Several first ideas did not survive: ignoring stale responses became real cancellation, `SlaStartedAtUtc` became `SlaWindowSeconds`, the first package pin was replaced.
3. **Rank by these criteria, in order:** what the brief says; correct behaviour in real use (concurrency, time zones, security); testability; the least machinery that does the job.

Section 7 records the significant decisions in a lightweight ADR format (context, decision, alternatives, consequences).

### 2.4 Implementation sequence

- **Bottom up:** domain rules first (pure C#, unit tested), then application handlers, then persistence and SQL, then HTTP, then the UI. Each layer compiled and passed before the next.
- **Tests first for anything that was a defect:** I watched each regression test fail on the old code before fixing it (DEFECT-117, the reference gap, the due-date sort).
- **Small, separate commits:** refactors separated from features (`2a8384e`, `86fb9ff`), and the defect fix separate from the filtering, as the brief asks.

### 2.5 Verification strategy

I tested at four levels, each covering a different type of failure:

| Level | Catches | Example |
| --- | --- | --- |
| Unit tests | Rule logic and boundaries | At risk at exactly 25 %; due exactly now |
| Real SQL on SQLite with the real EF model | Translation and query shape | The SLA filter and the report CASE agree with the C# rule for every seeded ticket |
| The HTTP pipeline (WebApplicationFactory) | Wiring: authentication, routing, validation | Every routed endpoint is 401 without a token |
| The real system: SQL Server 2022, Docker, headless Chrome | Provider behaviour, concurrency, timing, UI | 12 simultaneous creates; login timings; screenshots |

Two verification practices were especially useful:

- **Breaking the code on purpose** to prove a test can fail. Removing the fallback policy made four tests fail and named the leak ("GET /api/agents answered OK without a token"). Disabling the assignment lock made 11 of 12 concurrent requests fail.
- **Measuring instead of assuming:** login timings, image sizes, the Docker cache, the number of SQL statements a report takes.

### 2.6 Commit discipline

Before every commit I set aside everything not in it (`git stash --keep-index --include-untracked`) and ran the suites on exactly what would be committed. So every commit in the history builds and passes on its own, not just the last one.

---

## 3. Requirement trace

### Task 1

| ID | Requirement | How | Evidence |
| --- | --- | --- | --- |
| 1.1 | Fix the cause, not the effect | Effect dependencies derived from the whole filter set; only search debounced | [useTicketList.ts](apps/web/src/features/tickets/hooks/useTicketList.ts) |
| 1.1 | Regression test asserting the request | Four tests on `getTickets` calls; three fail on the original hook | [useTicketList.test.ts](apps/web/src/features/tickets/hooks/useTicketList.test.ts) |
| 1.1 | Root cause in commit and README | `fe6877d`; README "The defect" | |
| 1.1 | Immediate request per filter; none per keystroke; SLA filter needs no wiring | Value-keyed request; `useDebouncedValue`; SLA filter added later with no hook change | Same tests |
| FL-1 | Exact matches | `WhereIf` per field | `TicketQueriesTests.Priority_IsAnExactMatch`, `Status_...`, `CategoryAndCustomer_...` |
| FL-2 | `unassignedOnly` | `AssignedAgentId == null` | `UnassignedOnly_*` |
| FL-3 | Search title, reference, customer; trimmed; blank ignored | `Contains` on three columns after `Trim()` | `Search_*` (6 tests) |
| FL-4 | AND | Chained `Where` | `Filters_CombineWithAnd` |
| FL-5 | In the database, before count and paging | Filter, then `CountAsync`, then sort and page | `CountAndPages_DescribeTheFilteredSet_NotEveryTicket`; SQL read from logs |
| FL-6 | Next filter is one filter | `slaStatus` added as one line | `TicketQueries.ApplyFilters` |

### Task 2

| ID | Requirement | How | Evidence |
| --- | --- | --- | --- |
| BR-1 | Priority | `PriorityPolicy.Decide` | `PriorityPolicyTests` |
| BR-2, BR-3 | Windows, premium half, 1 h floor | `SlaPolicy.WindowFor` | `SlaPolicyTests` (8 windows, floor, exact floor) |
| BR-4, BR-5 | Specialists, eligibility, fewest open, ties, nobody eligible | `AgentSelector.Select` | `AgentSelectorTests` |
| BR-6 | Configurable, no category names | `SlaOptions` validated at start-up; category flags | `SlaOptionsValidator`; `Policies` test helper uses flags |
| BR-7 | Escalation | `Ticket.Escalate`, `AgentSelector.Reevaluate`, `EscalateTicketCommandHandler` | `TicketEscalationTests`, `EscalateTicketCommandHandlerTests` |
| BR-8 | SLA status, injectable now | `SlaEvaluator.Evaluate(..., nowUtc)` | `SlaEvaluatorTests` (boundaries) |
| 2.2 | Triage on create, 201 when unassigned, `triage` reasons | `RaiseTicketCommandHandler`, `RaisedTicketDto` | `RaiseTicketCommandHandlerTests` |
| 2.2 | Escalate endpoint 200/400/404/409 | `POST /api/tickets/{id}/escalate` | Handler tests; validator tests; checked over HTTP |
| 2.2 | History newest first | `GET /api/tickets/{id}/escalations` | `Escalations_AreListedNewestFirst_WithAgentNames` |
| 2.2 | Migration, keys, lengths, delete behaviour, index | `TicketEscalationsAndSlaWindow` | README "Database"; `PersistenceModelTests` |
| 2.2 | SLA filter in SQL | `SlaSql.Is` | `SlaStatus_FilteredInSql_MatchesTheDomainRule_ForEveryTicket` |
| 2.3 | One SLA indicator, not colour alone | `SlaIndicator` + `sla.ts` (label, icon, tone; exhaustive `Record`) | `SlaIndicator.test.tsx` |
| 2.3 | SLA filter in the bar | `TicketFilters` | Checked in the browser; needed no hook change |
| 2.3 | Escalation panel: validation, pending, errors incl. 409, refresh without reload | `EscalationPanel`, `TicketDetailPage` | `EscalationPanel.test.tsx` |
| 2.3 | History; triage result; control disabled when not escalatable | `EscalationHistory`, `TriageResult`, `escalationBlocker` | `EscalationHistory.test.tsx`, `EscalationPanel.test.tsx`; checked in the browser |
| 2.3 | Fully typed, no `any`, no `@ts-ignore` | ESLint `no-explicit-any` as error; typecheck in CI | `npm run lint`, `npm run typecheck` |
| 2.4 | Backend and frontend tests as listed | See README "Testing" | |

### Task 3

| ID | How | Evidence |
| --- | --- | --- |
| AU-1 | `Agents.PasswordHash` via migration; column chosen over a Users table (D9) | `AgentPasswordHash` migration |
| AU-2 | Identity `PasswordHasher`, PBKDF2-HMAC-SHA512, 210,000 iterations | `IdentityPasswordHasherTests` decodes the hash header |
| AU-3 | Hashed at seed time; also for databases seeded before | `SupportDbSeeder.SeedCredentialsAsync`; README credentials |
| AU-4 | Token, expiry, agent; identical 401; inactive refused | `LoginCommandHandlerTests`; `AuthenticationTests` |
| AU-5 | `GET /api/auth/me` | `Me_Is401WithoutAToken_AndNamesTheAgentWithOne` |
| AU-6 | Fallback policy; login, Swagger, health open | `EveryEndpoint_RequiresSignIn_ExceptSignInAndHealth` |
| AU-7 | Key from config or `Jwt__SigningKey`; `.env.example`; 480 min | `JwtOptionsValidatorTests` |
| AU-8 | `escalatedBy` (name and agent id) from the token | `WhoEscalated_ComesFromTheToken_NotFromTheRequestBody` |
| Beyond AU | Deactivation revokes access at once, not at token expiry | `ATokenStopsWorking_TheMomentItsAgentIsDeactivated` |
| AU-9 | `/login`: labelled fields, validation, pending, error | `auth.flow.test.tsx` |
| AU-10 | Token attached in `client.ts`; storage trade-off in README | `client.test.ts` |
| AU-11 | Redirect, then back to the requested page | `sends a visitor to /login, then back to the page they wanted` |
| AU-12 | Name in header, sign-out | `signs out from the header, back to /login` |
| AU-13 | Any 401 on a token-carrying call ends the session | `ends the session when the API refuses a token it was sent`; checked in the browser with a tampered token |

### Task 4

| ID | How | Evidence |
| --- | --- | --- |
| DK-1 | Two stages; project files and build settings copied before source | `apps/api/Dockerfile`; restore `CACHED` after a `.cs` edit |
| DK-2 | `.dockerignore` excludes bin, obj, node_modules, .git, .env, the web app (and tests, docs) | `.dockerignore` |
| DK-3 | `USER $APP_UID`, HTTP 8080, no certificates | `whoami` printed `app` |
| DK-4 | Settings from run-time environment; dev settings not published | `/app` listing shows only `appsettings.json` |
| DK-5 | Compose `api` service, port 5080, `sqlserver` by name, waits for healthy | `docker-compose.yml` |
| DK-6 | Migrate and seed on an empty volume; production strategy in README | Logs show three migrations and the seed |
| DK-7 | `/health` with a database check, open, mapped centrally | `Health_IsOpen_AndReportsTheDatabase` |
| DK-8 | Web app against the container | Signed in and listed tickets through the proxy |

### How the scoring areas are addressed

| Area | Where to look |
| --- | --- |
| .NET / backend | ProblemDetails for 400/401/404/409; FluentValidation on every request body and query; async throughout; one middleware for errors |
| OOP and SOLID | `PriorityPolicy`, `SlaPolicy`, `AgentSelector`; interfaces only at real seams (`IClock`, `ICurrentUser`, `IAssignmentLock`, `IPasswordHasher`, `ITokenService`, repositories) |
| React / TypeScript | One `SlaIndicator`; `useTicketList` and `useDebouncedValue`; loading, error and empty states; no `any`; labelled inputs via `Field` |
| Security | Hashing, generic failures with equal timing, key in configuration, fallback policy, revocation on deactivation, actor from token, nothing baked in, non-root container, dependency advisories fixed |
| Debugging | DEFECT-117 cause; and the bugs found during verification |
| Testing and quality | Boundary tests, `FixedClock`, parity tests, mutation checks, constants instead of magic numbers |
| SQL | Committed migrations, `AsNoTracking`, narrow projections, no N+1 (workloads in one query), filters before paging, one deliberate index |
| Design patterns | README "Design patterns" |
| Git and documentation | Small commits, each tested in isolation; this file and the README |

---

## 4. Architecture

The starter code uses a layered monolith based on Clean Architecture, with features organized within each layer and dependencies directed inward. Architecture tests enforce these boundaries. I kept this structure and extended it where required. Introducing microservices would have added complexity without addressing an assignment requirement.

```text
                        +---------------------------------------------+
  React (apps/web)      |  Presentation (ASP.NET Core)                |
  login, list, detail   |  Controllers: Tickets, Auth, Reports        |
  client.ts adds token  |  Fallback auth policy, JwtBearer, /health   |
  -------- HTTP ------> |  HttpCurrentUser : ICurrentUser             |
                        |  ExceptionHandlingMiddleware, ValidationFilter
                        +----------------------+----------------------+
                                               | depends on
                        +----------------------v----------------------+
                        |  Application                                |
                        |  Handlers: RaiseTicket, EscalateTicket,     |
                        |  Login, GetCurrentAgent, GetSlaSummary ...  |
                        |  Abstractions: IClock, ICurrentUser,        |
                        |  IAssignmentLock, IPasswordHasher,          |
                        |  ITokenService, IReportQueries ...          |
                        +----------------------+----------------------+
                                               | depends on
                        +----------------------v----------------------+
                        |  Domain (no frameworks)                     |
                        |  Ticket (+ Escalate, StartSla)              |
                        |  TicketEscalation, SlaEvaluator, SlaPolicy, |
                        |  PriorityPolicy, AgentSelector, Agent       |
                        +---------------------------------------------+
                                               ^ implements
                        +----------------------+----------------------+
                        |  Infrastructure (EF Core, SQL Server)       |
                        |  TicketQueries, ReportQueries, SlaSql       |
                        |  SqlServerAssignmentLock (sp_getapplock)    |
                        |  IdentityPasswordHasher, JwtTokenService    |
                        |  SlaOptions, JwtOptions, migrations, seeder |
                        +---------------------------------------------+
```

**Raising a ticket:** the controller passes the request (validated by the filter) to `RaiseTicketCommandHandler`. The handler loads the customer and category, asks `PriorityPolicy` for the priority and `SlaPolicy` for the window, then inside `IAssignmentLock` issues the reference, starts the SLA clock, asks `AgentSelector` for an owner from one workload query, and saves. The response is the re-read ticket plus the three reasons.

**Escalating:** inside the lock, the handler loads the ticket, rejects it early if it is Critical, Resolved or Closed, asks `AgentSelector.Reevaluate` whether the owner stays, and calls `Ticket.Escalate`, which raises the priority, restarts the clock and records the history row. The actor comes from `ICurrentUser`.

**Listing tickets:** `TicketQueries` joins the labels, applies each supplied filter (the SLA filter through `SlaSql`), counts, sorts, pages and projects only the needed columns.

---

## 5. How the stack made the work easier

The starter project already provided several useful architectural boundaries and reusable components. I built on those instead of replacing them:

**Backend**

- **Architecture tests** told me immediately where something could not go (for example, reading `HttpContext` in Application), which pushed the design toward `ICurrentUser`.
- **The CQRS split** (repositories and aggregates for writes, query classes with projections for reads) meant the new reads (history, report) slotted into `TicketQueries` and a new `ReportQueries`, with no tracked entities.
- **`ValidationFilter` + FluentValidation** validate any action argument, so new requests (escalate, login, report query string) only needed a validator class.
- **`ExceptionHandlingMiddleware`** maps exception types to status codes, so handlers just throw (`BusinessRuleViolationException` is 409, the new `UnauthorizedException` is 401).
- **`IClock` with `FixedClock`** made every SLA boundary testable to the second.
- **`TicketBuilder`** let tests start from any valid state without reaching into the aggregate.
- **Central package management with transitive pinning** made the security fix one line that applies to every project.
- **`PersistenceModelTests`** fail if the model changes without a migration, so I could not forget one.
- **The SQLite provider** already in the test project let me run the real EF queries against the real seed data without a server.
- **EF command logging in Development** let me read the SQL for every claim I make about it.
- **`WebApplicationFactory`** let me test authentication through the real pipeline.

**Frontend**

- **`Field`** wires label, hint, error and `aria-describedby`, so every new form is accessible by default.
- **`Badge`** and the existing tones gave the SLA indicator its colours with no new CSS colours.
- **`useAsyncData`** loads the escalation history with loading and error states for free.
- **One `client.ts`** meant attaching the token and handling 401 was a change in one place.
- **TypeScript unions** plus `Record<SlaStatus, ...>` make a new SLA state a compile error until it has a label and icon.
- **ESLint's React hooks rules** rejected my first DEFECT-117 fix (`set-state-in-effect`), which led to a cleaner derived-state design.
- **Vitest fake timers** made the debounce tests exact; **`createMemoryRouter`** with the real route table made the sign-in redirect test realistic.

**Containers**

- The .NET images already run on port 8080 and include an unprivileged `app` user, so DK-3 needed one `USER` line.
- The compose file already had a SQL Server health check, so `depends_on: service_healthy` was enough to order start-up.

---

## 6. Reuse

**Reused as it was:** `Field`, `Badge`, `ErrorMessage`, `Spinner`, `EmptyState`, `Pagination`, `useAsyncData`, `client.ts`, `IClock`, `FixedClock`, `TicketBuilder`, the validation filter, the exception middleware, `PagedResult`, the seed data and the seeder's relative timestamps.

**Made reusable by this work:**

- `ToListItem`: one list-row projection, used by the paged list and a customer's tickets (was copied twice).
- `WhereIf`: one line per optional filter.
- `SlaSql`: one SQL definition per SLA state, used by the list filter and the report.
- `SlaPolicy`, `PriorityPolicy`, `AgentSelector`: rules used by both create and escalate.
- `useDebouncedValue`: any search box.
- `SlaIndicator` and `sla.ts`: the list, the detail page, the triage result and the filter labels.
- `escalation.ts`: the same limits and blocker logic for the panel and its tests.
- Test helpers: `SeededDatabase` (real model, real seed, SQLite), `Policies`, `PassThroughAssignmentLock`, `FakePasswordHasher`, `FakeCurrentUser`, `ApiFactory`, `SqlServerFactAttribute`.

---

<a id="decision-records"></a>

## 7. Decision records

These lightweight architecture decision records (ADRs) document choices that were consequential, difficult to reverse, or important to explain during review.

### D1. Fix DEFECT-117 by deriving the request from state

- **Context.** The effect ran only on three fields; a lint suppression hid it.
- **Decision.** Debounce only the search text; key the request by the whole filter set; abort superseded requests.
- **Alternatives.** A remount key or forced reload (hides the effect, ruled out by the brief); ignoring stale responses with a flag (my first version: the server still runs the query); TanStack Query (the production choice, but the brief scores a custom hook).
- **Consequences.** Any new filter works without wiring. `JSON.stringify` is the key; order is stable because filters are always built from the same defaults, the same technique TanStack uses.

### D2. Business rules as small domain classes

- **Context.** BR-1 to BR-7 must be shared by create and escalate and tested at boundaries.
- **Decision.** `PriorityPolicy`, `SlaPolicy`, `AgentSelector` in the domain; `Ticket.Escalate` on the aggregate.
- **Alternatives.** Rules in handlers (duplicated, mock-heavy tests); a rules engine (far too much).
- **Consequences.** Each rule has its own tests with no database. Handlers only gather data and save.

### D3. Store the SLA window, not its start

- **Context.** At risk needs the window length, and escalation restarts the window.
- **Decision.** `Tickets.SlaWindowSeconds`; At risk is `now >= DueAtUtc - 25 % of window`.
- **Alternatives.** Measure from `CreatedAtUtc` (wrong after escalation); store `SlaStartedAtUtc` (needs a date difference in SQL, which SQLite cannot run, so the filter could not be tested without SQL Server).
- **Consequences.** The SQL is a `DATEADD` on one column, translatable on both databases. The stored value is the commitment made at the time, so a later configuration change does not silently move existing tickets in or out of risk. The status itself is still never stored.

### D4. An agent at their limit keeps the ticket they escalate

- **Context.** BR-7 says keep the owner "if still eligible"; eligibility is "strictly below the limit".
- **Decision.** Evaluate the owner with this ticket left out of their open count.
- **Alternatives.** The literal count. Marcus (6/6) would lose every ticket he escalates.
- **Consequences.** Escalation never adds work to an agent, so this is the meaning that makes sense; it matches routing engines, which do not reassign on a priority change.

### D5. Tie-break: fewest open, most spare capacity, lowest id

- **Context.** BR-5 asks for a deterministic tie-break.
- **Decision.** As above.
- **Alternatives.** Lowest id alone; ServiceNow's "longest since last assignment" (needs a timestamp the model does not have).
- **Consequences.** A bigger agent takes more work when loads are equal; same input, same answer.

### D6. Serialise assignment with an application lock

- **Context.** Two simultaneous creates can both see one free slot; the reference is "max + 1".
- **Decision.** `sp_getapplock` held for the transaction, behind `IAssignmentLock`, inside EF's execution strategy, clearing the change tracker on each attempt.
- **Alternatives.** `UPDLOCK`/`HOLDLOCK` hints (the deciding rows are not the row written; reported to escalate locks and still let duplicates through); `rowversion` (no single row changes); accept and document.
- **Consequences.** Measured: with the lock, 12 simultaneous creates gave exactly 3 to Sara (her free slots) and 9 unassigned; without it, 11 of 12 failed with HTTP 500. The lock is global, which is fine at a support desk's volume. The execution strategy is mandatory here: EF refuses a user transaction while retry-on-failure is enabled unless it runs inside the strategy.

### D7. References follow the last reference, not the id

- **Context.** After the database container crashed, the next ticket was TCK-1003: SQL Server had skipped identity values by 1000.
- **Decision.** Read the newest ticket's reference and add one, inside the lock.
- **Alternatives.** A database sequence (also skips on restart unless `NO CACHE`, and needs a migration); keep using the id.
- **Consequences.** No gaps for customers; the unique index backs the lock up.

### D8. Central authorization with a fallback policy

- **Decision.** Everything requires a signed-in agent unless it opts out (`[AllowAnonymous]` on sign-in, `.AllowAnonymous()` on health). Swagger is served by middleware before routing and stays open.
- **Alternative.** `[Authorize]` per controller: a new controller would be open by default.
- **Consequences.** An integration test walks every routed endpoint; it found the leak immediately when I removed the policy on purpose.

### D9. Credentials on `Agent`

- **Decision.** A nullable `PasswordHash` column.
- **Alternative.** A Users table linked to Agents: a 1:1 join with no other user type.
- **Consequences.** One migration, one query at sign-in. If customers ever sign in, a Users table becomes worth it.

### D10. Hashing at 210,000 iterations, with rehash

- **Context.** Identity defaults to 100,000 iterations; OWASP gives 210,000 for PBKDF2-HMAC-SHA512.
- **Decision.** 210,000, and upgrade hashes made with fewer at sign-in. A test decodes a hash to prove PRF 2 (HMAC-SHA512) and the count.
- **Consequences.** About 90 ms per sign-in on this machine, deliberately.

### D11. Equal work on every sign-in failure

- **Context.** Real CVEs: unknown users answered faster, so accounts could be enumerated; one fix had a dummy-hash helper that one path never reached.
- **Decision.** Exactly one password check on every path, the dummy hash for an unknown email.
- **Consequences.** Measured medians: 98 ms (unknown email), 93 ms (wrong password), 88 ms (inactive). A theory test asserts one check per path, so a future early return fails the build.

### D12. No configuration in the image; migrations by setting

- **Decision.** `appsettings.Development.json` is never published; the container gets connection string, key and environment from compose; `Database:MigrateAndSeedOnStartup` controls start-up migration.
- **Consequences.** The same image could run anywhere with different settings. A `dev-only-` key is refused outside Development, so the compose default cannot leak into production.

### D13. One SQL definition of each SLA state

- **Context.** The report needed to classify tickets in SQL too; a third copy of the rule would drift.
- **Decision.** `SlaSql.Is(status)` predicates; the filter applies one; `SlaSql.StatusOf` composes them into a single CASE for the report, using expression trees.
- **Consequences.** Parity tests on SQLite and SQL Server compare both against the C# rule.

### D14. Deactivation revokes access immediately

- **Context.** A JWT is valid until it expires, so a deactivated agent kept up to eight hours of access. I had listed this as the weakest part of the authentication.
- **Decision.** In the JwtBearer `OnTokenValidated` event, confirm the agent still exists and is active (`IAgentQueries.IsActiveAsync`, a primary-key seek reading one bit), and fail the token otherwise.
- **Alternatives.** Short tokens with refresh (refresh tokens are "not wanted" in the brief); a cached denylist of agent ids (needs a cache, which is out of scope, and only pays off at high traffic); a security stamp claim (needs a stamp column and a way to change it, more than this needs).
- **Consequences.** One cheap query per authenticated request. An integration test signs in, deactivates the agent, and expects the same unexpired token to get 401.

### D15. Escalations record the actor's id, not just a name

- **Context.** Two agents can share a name, and names change; an audit row should identify the person.
- **Decision.** `TicketEscalations.EscalatedByAgentId`, a nullable foreign key (restricted, indexed by EF), set from the token alongside the name.
- **Consequences.** The name stays for a readable history. Rows written before sign-in existed keep a null id, which is accurate: their actor was free text.

### D16. The report lists idle agents and every priority

- **Context.** Grouping only the tickets drops anyone with none, which reads as "no data" and hides exactly the people a team lead looks for when rebalancing work.
- **Decision.** The agent view starts from the agents table and left-joins the totals: every active agent appears, zeros included; inactive agents only while they have tickets in the period; "Unassigned" joins with `UNION ALL`. Every priority is always listed (filled from the known enum values, not counted).
- **Consequences.** Still one statement, which the command-counting test checks. EF could not translate a null test on the joined projection, so the query tests the joined row's key column instead and uses `COALESCE` for the counts.

### D17. Filters in the URL: researched, deliberately not built

- **Context.** Keeping list filters in the URL lets refresh, back and forward, and shared links keep the view; it is recommended practice for list screens.
- **Decision.** Not now. It needs the search text debounced before the URL is written, a history policy (replace or push), and two-way sync when the URL changes from outside. All of it lands in `useTicketList`, the most carefully specified code in the brief (DEFECT-117), for a feature the brief does not ask for.
- **Consequences.** Listed as the next UI step. The derived-request design makes it straightforward later: read the initial filters from the URL and write the settled query back.

---

## 8. Implementation notes by task

### Getting the environment working

The development machine initially lacked the .NET SDK and had very little free disk space. I installed .NET 10 in the user profile and cleared tool caches. Docker Desktop later became unresponsive when the disk filled up and needed a restart. To avoid disrupting other projects, I stopped SQL Server between sessions and did not prune Docker volumes that contained their databases.

### Task 1

I reproduced the defect in a test before touching the hook: change Status, expect one request with that status. Three new tests failed on the original code, one (debounced typing) already passed, which told me the debounce itself was fine and only the dependencies were wrong.

My first fix keyed the request off the filters object, and the keystroke test caught that every keystroke created a new object and so a new request. Keying by value fixed it. ESLint's `set-state-in-effect` rule then rejected setting loading state inside the effect, so I derived `isLoading` from "is the current result for the current request" instead. When I challenged the stale-response guard, it became `AbortController` cancellation, which also stops the server query.

For filtering, I extracted the list projection first (a separate refactor commit), wrote 14 tests against the real seed data and watched 12 fail, then added `ApplyFilters`. On SQL Server I read the generated statements and checked case-insensitive search and literal `%` and `_`.

### Task 2

Before coding I researched how ServiceNow, Zendesk and Salesforce route work, which settled the owner-keeps-ticket and tie-break decisions, and how to serialise assignment in SQL Server, which settled the application lock.

I built the domain rules first with their tests (windows, floor, priority, selection, re-evaluation, escalation, boundaries), then the persistence. The pivot from `SlaStartedAtUtc` to `SlaWindowSeconds` happened when I realised the SLA filter's SQL could not be tested on SQLite. The migration backfills the window for existing tickets; I checked on SQL Server that all 40 got the exact value.

Verifying on SQL Server found the reference jump to TCK-1003. I wrote a test that inserts a ticket with a skipped id, watched it produce TCK-1003, then fixed the cause. To prove the lock was needed, I disabled it and fired 12 simultaneous creates: 11 failed with HTTP 500 on duplicate references, a bug the starter code already had.

For the UI I took screenshots in headless Chrome and reviewed them. That found three problems: the triage explanations ran into the agent's name, a 409 left the page showing a stale priority, and the escalate panel sat below the fold on a laptop (measured at y = 942 on a 900 px screen). After moving it into the main column it starts at y = 409. I also checked that input is safe (React escapes output; a test renders an XSS payload as text) and that every colour comes from the existing theme.

### Task 3

Research changed two things: Identity's default iteration count is below OWASP's figure, and timing leaks are a real, recurring CVE class. I added integration tests that run the real pipeline, then removed the fallback policy on purpose to see them fail. On SQL Server I confirmed the seeder gave credentials to an already-seeded database, measured the sign-in timings, and walked the whole flow in Chrome: redirect, validation, refusal, return to the requested ticket, the agent's name in the header, escalation recorded under that name, and a tampered token ending the session. The email placeholder and the "no registration" decision came from questions during review.

### Task 4

The two traps (published development settings; chiseled images without `whoami`) were found before writing the Dockerfile. Verification followed the brief's list exactly, plus a check inside the running container that no development settings file exists.

### Stretch

I researched how SLA reports define breach rate (breached over tickets with an SLA) and how EF Core translates conditional counts. I refactored the SLA predicates into `SlaSql` first (its own commit), then built the report. Reading the generated SQL confirmed one statement; a test now counts the commands. Re-reading my own SQL I noticed `DATEDIFF(minute)` counts boundaries, so a 2-second resolution can count as a minute; I switched to seconds. The report's tests need SQL Server, so they are opt-in and create and drop their own database.

### Closing the known gaps

When I wrote the limitations, I reviewed each one again and asked whether it could be fixed within the brief's scope. Three could; one should not be:

- **Revocation.** I wrote the test first: sign in as Ben, deactivate him, call the API with the same token. It got `200 OK`, proving the gap, then 401 after adding the `OnTokenValidated` check.
- **The escalation actor's id.** A migration adds a nullable foreign key; the domain records the id from the token alongside the name; the tests assert both.
- **Idle agents in the report.** The first version, with a null check on the left-joined totals, failed because EF Core could not translate it. Checking the joined row's key column instead works. The SQL Server tests confirm one statement, the five active agents and "Unassigned" listed with zeros for an empty period, and inactive Yuki left out.
- **Filters in the URL.** Researched and deliberately left for later (D17).

### Clean-up

`dotnet list package --vulnerable` showed a transitive package with a high and a moderate advisory. My first pin, 10.0.12, was still listed as vulnerable; the advisory's own data says the first patched 10.x release is 10.0.13, which is what is pinned. `npm audit` showed two high advisories in development tools only; patch updates fixed them. A test exposed "due soonest" listing tickets without a due date first. Four comments that had become untrue were corrected.

---

## 9. Verification log

| What | How | Result |
| --- | --- | --- |
| SLA filter vs C# rule, SQL Server | Each state filtered, compared with the evaluator's classification | WithinSla 3/3, AtRisk 3/3, Breached 22/22, Met 12/12, NotApplicable 0/0 |
| Concurrency with the lock | 12 simultaneous Billing creates, Sara with 3 free slots | 3 to Sara (6/6), 9 unassigned, 0 errors, unique references |
| Concurrency without the lock | Same, lock disabled temporarily | 11 of 12 HTTP 500 (duplicate reference) |
| Sign-in timing | 15 attempts each, medians | Unknown email 98 ms, wrong password 93 ms, inactive 88 ms |
| Fallback policy | Removed on purpose | 4 integration tests failed, naming `GET /api/agents` |
| Revocation | Sign in, deactivate the agent, reuse the token | `200 OK` before the fix (the gap), 401 after |
| Report, empty period | SQL Server, one command counted | 5 active agents and "Unassigned" with zeros; inactive agent left out; all 4 priorities listed |
| Migrations on an existing database | API start against the developer database | Each new migration applied; windows backfilled exactly; credentials issued to 6 agents |
| Docker clean start | `down -v && up --build` | Healthy, 3 migrations, 40 tickets, `whoami` = `app` |
| Docker cache | One `.cs` edit, rebuild | Restore `CACHED`; only copy and publish re-ran |
| Image size | `docker image inspect` | 122 MB runtime image, 343 MB SDK |
| Report | SQL from logs; command-counting test | One statement per request |
| Escalate panel position | Measured in headless Chrome at 1360 x 900 | Before: y = 942 (off-screen). After: y = 409, whole form visible |
| Dependencies | `dotnet list package --vulnerable`; `npm audit` | None in any project; 0 npm advisories |

---

## 10. Known limitations and future improvements

The assignment implementation meets the tested requirements, but several areas would need further work for a larger or production deployment.

**Query and assignment scalability.** Filtering takes place in SQL, with indexes on the main filter fields. However, `LIKE '%x%'` search can require scans, deep offset pagination and exact counts become expensive, and the due-date sorting expression can prevent an index-ordered scan. At a larger scale, I would consider full-text search, keyset pagination (using creation time and ID), estimated counts where acceptable, and a computed indexed sort column. The SLA summary currently evaluates its SQL `CASE` expressions over the reporting period; snapshots, a columnstore index, or a read replica could become useful as reporting volume grows. The global assignment lock also serializes ticket creation; partitioning it by category or using a single assignment worker with a queue would be options if throughput required it.

**Authentication and session management.** Revocation was the main limitation until I fixed it: every authenticated request now re-checks that the agent is still active, so deactivation takes effect immediately (D14). What remains: `sessionStorage` is accessible to injected JavaScript, and sign-in has no rate limiting or account lockout, which the brief rules out. The per-request check also costs one primary-key lookup per call. Production improvements would include shorter-lived access tokens with an HttpOnly-cookie refresh-token flow (or a backend-for-frontend), a cached denylist in place of the per-request lookup at high traffic, and sign-in rate limiting with lockout.

**User interface.** Filters are not kept in the URL, so refresh, back and forward, and shared links lose them (D17). There is no automated end-to-end suite, and the escalation history is not paged.

**Container hardening and deployment.** The current image was chosen to satisfy the assignment's shell-based `whoami` verification. Before production, I would consider `aspnet:10.0-noble-chiseled-extra` (non-root and without a shell or package manager, while retaining ICU), a read-only root filesystem, deployment health probes using `/health`, managed secret storage, migrations as a reviewed deployment step with a separate identity, CI image scanning, and HTTPS termination at the ingress or reverse proxy.

**Implementation references.** The most relevant files for reviewing these decisions are `AgentSelector.cs` (routing rules), `SqlServerAssignmentLock.cs` (concurrency and retry strategy), `TicketQueries.cs` and `SlaSql.cs` (query composition), `LoginCommandHandler.cs` (consistent sign-in failure handling), `useTicketList.ts` (the regression fix), and `AuthenticationTests.cs` (authorization enforcement).

---

## 11. Commit map

| Commit | What and why |
| --- | --- |
| `fe6877d` | DEFECT-117: request derived from all filters, only search debounced, superseded requests aborted |
| `2a8384e` | Refactor: one list projection, before adding filters |
| `38c0db4` | Filters in SQL before count and paging; tests on the real seed |
| `53f11ae` | Task 2 backend: rules, triage, escalation, history, SLA status and filter, lock, migration |
| `7c2f67b` | References no longer jump when SQL Server skips identity values |
| `f7ad1b6` | Task 2 UI: SLA indicator and filter, escalation panel and history, triage result |
| `6412404` | Task 3 backend: credentials, hashing, JWT, fallback policy, actor from token, integration tests |
| `4233251` | Task 3 UI: login, protected routes, token in the client, sign-out |
| `5cb1110` | Task 4: Dockerfile, compose service, `/health`, nothing baked in |
| `86fb9ff` | Refactor: one SQL definition per SLA state |
| `a47afce` | Stretch: SLA summary report in one statement |
| `58821e6` | Security: transitive package pinned to the patched version |
| `7f53d27` | Security: dev-tool advisories patched |
| `dba6199` | "Due soonest" no longer lists tickets without a due date first |
| `00d37ee` | Comments that had become untrue, corrected |
| `58e5458` | Deactivation revokes access at once: per-request active check |
| `b45a539` | Escalations record the actor's agent id (migration) |
| `f847408` | SLA report lists idle agents and every priority, still one statement |
| (this commit) | This file and the README |
