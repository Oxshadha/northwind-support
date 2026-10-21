# Northwind Support Desk

A support ticket management system: raise tickets, triage them automatically, escalate them with
a full history, track every ticket against its SLA, and sign in as an agent.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server 2022**, in one monorepo.

This is my submission for the candidate assignment in [`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md).
The README is the short version, as the brief asks. **[APPROACH.md](APPROACH.md)** has the full
story: how I worked, every decision record, a requirement-by-requirement trace, and the numbers
behind each verification.

---

## At a glance

| # | Task | Status | What it delivers |
| --- | --- | --- | --- |
| 1 | Defect and server-side filtering | **Done** | DEFECT-117 fixed at the cause; every filter applied in SQL before counting and paging |
| 2 | Escalation and assignment | **Done** | Automatic triage on create, escalation with immutable history, SLA status (incl. At risk), full UI |
| 3 | Login and authentication | **Done** | Agents sign in; every endpoint except sign-in and health requires a token; login UI |
| 4 | Dockerise the API | **Done** | `docker compose up` gives a migrated, seeded API, non-root, nothing baked in |
| + | Optional stretch: SLA summary report | **Done** | `GET /api/reports/sla-summary`, one SQL aggregate per request |

- **213 automated tests**: 160 unit, 14 integration (4 of them run against a real SQL Server), 4
  architecture, 35 web. All pass on a clean checkout without a database.
- **Verified beyond the tests** against SQL Server 2022, through the real HTTP pipeline, and in
  headless Chrome: generated SQL read from the logs, concurrency and timing measured, the Docker
  checks run as the brief lists them.
- **Bugs found on the way that the brief did not mention**, each fixed with a test: two
  concurrency bugs in ticket creation, ticket references jumping from TCK-0041 to TCK-1003 after a
  database restart, a vulnerable transitive package, and "due soonest" listing tickets with no
  due date first.
- **History**: small, focused commits, each tested on its own before it was made. The defect fix
  and the filtering are separate, as the brief asks.

---

## Run it

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

### Option A: API in Docker (one command)

```bash
docker compose up --build            # SQL Server + API on http://localhost:5080
npm install
VITE_API_PROXY_TARGET=http://localhost:5080 npm run web   # React app on http://localhost:5173
```

The container waits for SQL Server to be healthy, applies the migrations, seeds the demo data and
the development credentials. Check it with `curl http://localhost:5080/health`.

### Option B: API from source

```bash
cp .env.example .env     # optional: every value has a working local default
npm install
npm run db:up            # SQL Server 2022 in Docker on localhost:1433
npm run api              # terminal 1: migrates, seeds, serves https://localhost:7043 (and http://localhost:5043)
npm run web              # terminal 2: React app on http://localhost:5173
```

Swagger: `/swagger` on either API address. Sign in with `POST /api/auth/login`, then use the
**Authorize** button with the returned token.

### Development login

Every seeded agent has the same **local development password: `LocalDev-Only-2026!`**. It is a
fixture published here on purpose: it is hashed at seed time, and the seeder only runs where
`Database:MigrateAndSeedOnStartup` is on (a developer machine or the local container).

| Agent | Email | Can sign in |
| --- | --- | --- |
| Alex Turner | `alex.turner@northwind-support.example` | Yes |
| Ben Osei | `ben.osei@northwind-support.example` | Yes |
| Priya Nair | `priya.nair@northwind-support.example` | Yes |
| Sara Lindqvist | `sara.lindqvist@northwind-support.example` | Yes |
| Marcus Doyle | `marcus.doyle@northwind-support.example` | Yes |
| Yuki Tanaka | `yuki.tanaka@northwind-support.example` | No, inactive (proves the rule) |

### Tests

```bash
npm test                 # dotnet test, then the web tests; needs no database

# Optional: the four SQL Server-only tests (the report uses DATEDIFF, which SQLite lacks)
npm run db:up
SUPPORTDESK_SQLSERVER="Server=localhost,1433;User Id=sa;Password=Local_Dev_Password1;TrustServerCertificate=True;Encrypt=False" \
  dotnet test apps/api/tests/SupportDesk.IntegrationTests
```

---

## What I built, and the rules as I understood them

**Task 1.** The ticket list's filters now each send exactly one request, and only the search box
is debounced (300 ms). On the server, `status`, `priority`, `categoryId`, `customerId`,
`assignedAgentId`, `unassignedOnly` and `search` are applied in SQL with AND, before the count and
the page, so `totalCount` and `totalPages` describe the filtered set.

**Task 2.** Raising a ticket now decides everything a team lead did by hand, and explains it:

| Rule | As implemented |
| --- | --- |
| BR-1 Priority | Requested, else Medium. A category with `ForcesCriticalPriority` is always Critical |
| BR-2/3 SLA window | Base window per priority from config; premium customers get the configured multiplier (0.5), never below the configured minimum (1 h) |
| BR-4/5 Assignment | Eligible = active, specialist where the category requires one, strictly below `MaxOpenTickets`. Fewest open tickets wins; ties go to the most spare capacity, then the lowest id. Nobody eligible: created unassigned (201) with the reason |
| BR-6 Configurable | Everything above lives in the `Sla` section of `appsettings.json`, validated at start-up; categories are matched by their flags, never by name |
| BR-7 Escalation | One level up, due date restarted from now, owner kept if still eligible (else re-assigned), reason 5 to 500 characters, an immutable history row. Critical, Resolved and Closed are 409 with a message saying which |
| BR-8 SLA status | Derived, never stored. No due date: No SLA. Resolved by due: Met. Resolved late or open past due: Breached. Open with 25 % or less of its window left (inclusive, up to the due moment itself): At risk. Otherwise Within SLA |

The UI has one SLA indicator (label, icon and colour for each state) used by the list and the
detail page, an SLA filter, an escalation panel with client validation that mirrors the server,
the escalation history, and the triage result after creating a ticket.

**Task 3.** Agents sign in with email and password and get a JWT. Every endpoint requires it,
except sign-in and `/health` (Swagger UI stays open too). The UI has a login page, protected
routes that return you to the page you wanted, the signed-in agent's name and sign-out, and any
401 on a call that carried a token ends the session.

**Task 4.** A multi-stage image built from the repository root, a compose `api` service on
`localhost:5080`, `/health` with a real database check, and nothing environment-specific baked
into the image.

**Stretch.** `GET /api/reports/sla-summary?from=&to=&groupBy=agent|priority` returns, per group:
total, within SLA, at risk, breached, met, no SLA, breach rate and average resolution hours.

---

## Design decisions: chosen, rejected, and why

The full records (context, alternatives, consequences) are in [APPROACH.md](APPROACH.md#decision-records).

| Decision | Chosen | Rejected, and why |
| --- | --- | --- |
| Where the rules live | Small pure domain classes: `PriorityPolicy`, `SlaPolicy`, `AgentSelector`, plus `Ticket.Escalate`. Create and escalate call the same code | Rules in handlers or controllers: duplicated between create and escalate, and only testable with mocks |
| Fixing DEFECT-117 | Debounce only the search text; derive the request from the whole filter set; cancel superseded requests with `AbortController` | A remount key, forced reload or client-side filtering (the brief rules these out); TanStack Query (the right production tool, but the brief scores a custom data hook) |
| Making filters extensible | One `WhereIf` line per filter in `TicketQueries.ApplyFilters` | Specification classes or a dynamic expression builder: more machinery than seven optional filters need |
| At risk after escalation | Store `SlaWindowSeconds`, the length of the window that ends at `DueAtUtc` | Measuring from `CreatedAtUtc` (wrong once escalated restarts the clock); storing `SlaStartedAtUtc` (needs `DATEDIFF`, which the SQLite-backed tests cannot run) |
| Keeping the owner on escalation | Count the owner's load without the escalated ticket, so Marcus at 6/6 keeps his own ticket | The literal count: an agent at their limit would lose every ticket they escalate. Real routing engines do not reassign on a priority change either |
| Tie-break | Fewest open, then most spare capacity, then lowest id | Lowest id alone: deterministic but always favours the same agents. "Longest since last assignment" (ServiceNow's tie-break) needs data the model does not store |
| Concurrent assignment | `sp_getapplock` held for the transaction, behind `IAssignmentLock`, inside EF's execution strategy | Row locks (the rows deciding an agent's load are not the row written); optimistic `rowversion` (no single row changes). I measured the problem: without the lock, 11 of 12 simultaneous creates failed |
| Ticket references | Follow the newest ticket's reference, inside the lock | The original "highest id + 1": SQL Server skips identity values after an unclean restart, and references jumped to TCK-1003 |
| Credentials | `PasswordHash` column on `Agent` | A separate Users table: every user is an agent, so it would be a 1:1 join with nothing to add |
| Password hashing | ASP.NET Core Identity `PasswordHasher` (PBKDF2-HMAC-SHA512) at 210,000 iterations, old hashes upgraded at sign-in | The 100,000 default (below OWASP's figure); a home-made hash |
| Token storage in the browser | `sessionStorage` | `localStorage` (survives across sessions and tabs); memory only (lost on every refresh). An HttpOnly cookie is the production answer, see limitations |
| Central authorization | A fallback policy: closed unless an endpoint opts out | `[Authorize]` on each controller: a new controller would be open until someone remembers |
| Revoking access | Every authenticated request re-checks that the agent is still active (one primary-key lookup) | Waiting for the token to expire (up to 8 h of access after deactivation); a cached denylist (caching is out of scope, and the lookup is cheap at this size) |
| Who escalated | Taken from the token through `ICurrentUser`, implemented in Presentation | Reading `HttpContext` in Application: the architecture tests forbid it, rightly |
| Migrate on start-up | An explicit setting, `Database:MigrateAndSeedOnStartup`, on for development and the local container | Tying it to the environment name: the container and the tests need it independently |
| Base image | `aspnet:10.0` with the built-in non-root `app` user | Chiseled images (smaller, no shell) fail the brief's own `whoami` check; noted as the production change |
| SLA in SQL | One definition per state (`SlaSql`), used by the list filter and composed into a CASE for the report | A third copy of the rule for the report |

### Design patterns, and why each fits

1. **Policy objects (domain services).** `SlaPolicy`, `PriorityPolicy` and `AgentSelector` each
   hold one rule, take plain values and return a decision with its reason. Create and escalate
   share them, they need no mocks to test, and the triage explanation falls out of them.
2. **Specification, composed as expression trees.** `SlaSql.Is(status)` is the one SQL definition
   of each SLA state. The list filter applies it; the report builds a single CASE from the same
   predicates. A parity test runs both in the database against the C# rule.
3. **Mutual exclusion around a unit of work.** `IAssignmentLock` makes "read workloads, choose an
   owner, save" one exclusive step. The interface keeps the SQL Server detail out of the
   application layer and lets unit tests use a pass-through.

I also kept and extended what the codebase already had: repositories with a unit of work for
writes, query objects with narrow projections for reads (CQRS without a mediator), and the
options pattern, now with validation that stops the API at start-up on a bad value.

---

## The defect: DEFECT-117

**Root cause.** `useTicketList`'s effect listed only `search`, `page` and `pageSize` as
dependencies, and an `eslint-disable` comment (claiming the filters object changes every render,
which it does not) hid the omission. So changing Status, or any other filter, never re-ran the
request, and the next search request picked up whatever filters were in state by then.

**Fix** ([useTicketList.ts](apps/web/src/features/tickets/hooks/useTicketList.ts)). Only the
search text is debounced (a new reusable `useDebouncedValue` hook). The request is derived from
the whole filter set, keyed by value, so any filter change requests immediately and a new filter
(the SLA filter later) needs no wiring. A superseded request is aborted, so a slow old response
can never overwrite a newer one, and the API stops its query too.

**Verify.** `npm test`: four tests in
[useTicketList.test.ts](apps/web/src/features/tickets/hooks/useTicketList.test.ts) assert the
requests sent. Three of them fail on the original hook (I checked by swapping it back). By hand:
open the ticket list with the network tab open and change Status: one request, immediately.

## Filtering

- **Where.** [`TicketQueries.ApplyFilters`](apps/api/src/SupportDesk.Infrastructure/Queries/TicketQueries.cs),
  one `WhereIf` line per filter, applied before `CountAsync`, sort and `Skip/Take`.
- **How I checked the SQL.** Development logs every EF command, so I read the generated
  statements: one `COUNT(*)` and one paged `SELECT` with the same `WHERE`, every value a
  parameter, search as `LIKE @p ESCAPE N'\'`, only the columns the list needs. On SQL Server I also
  checked search ignores case and treats `%` and `_` as text. The filter tests run the real query on
  SQLite against the 40 seeded tickets (priority, combinations, customer-name search, counts and pages).
- **Adding the next filter.** A property on `TicketQuery`, one `WhereIf` line, one `append` in
  `buildQuery` (`apps/web/src/api/tickets.ts`), one field on `TicketFilters`. The SLA filter in
  Task 2 was added exactly like that, with no change to the list hook.

## Authentication

| Topic | What I did |
| --- | --- |
| Credential storage | `Agents.PasswordHash` (nullable, `nvarchar(200)`), added by the `AgentPasswordHash` migration. An agent without one cannot sign in |
| Hashing | Identity `PasswordHasher`: PBKDF2-HMAC-SHA512, random 128-bit salt per password, 210,000 iterations (OWASP's figure for SHA-512). A test decodes a hash to prove the algorithm and count |
| Sign-in | `POST /api/auth/login` returns `{ token, expiresAtUtc, agent }`. Unknown email, no credentials, wrong password and inactive agent give the same 401 and message, after the same work: an unknown email is checked against a dummy hash. Measured medians: 88 to 98 ms for all three failure kinds |
| Token | HMAC-SHA256 JWT with `sub`, `name`, `email`, `jti`; issuer and audience checked; 30 s clock skew instead of the default 5 min |
| Lifetime | **480 minutes**: one working shift, then sign in again |
| Signing key | From configuration or `Jwt__SigningKey`, listed in `.env.example`. Start-up fails on a missing or short key, and a `dev-only-` key is refused outside Development |
| Enforcement | A fallback authorization policy. An integration test calls every routed endpoint without a token and expects 401, except sign-in and health. Removing the policy makes four tests fail |
| Revocation | After the signature is checked, every request confirms the agent still exists and is active. A deactivated agent's token is refused at once, not when it expires: a test signs in, deactivates the agent and expects the same token to get 401 |
| Browser storage | `sessionStorage`: survives a refresh, not closing the tab, not shared between tabs. Like any script-readable storage it is exposed to XSS. React escapes all output and the app renders no HTML strings, which a test checks with an XSS payload |
| `escalatedBy` | From the token: the agent's name and id. A test sends `escalatedBy: "Mallory"` in the body and the history still records the signed-in agent |

**Production changes:** short-lived access tokens held in memory with a refresh token in an
HttpOnly, Secure, SameSite cookie (or a backend-for-frontend); a cached denylist instead of the
per-request database check once traffic makes it costly; rate limiting and lockout on sign-in; the
key from a secret store with rotation; HTTPS terminated in front of the API; agents provisioned by
invite or company SSO.

## Docker

```bash
docker compose up --build        # or: docker build -f apps/api/Dockerfile -t supportdesk-api .
```

| Variable (in `.env`) | Becomes | Default |
| --- | --- | --- |
| `API_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | `Development` (turns on Swagger) |
| `MSSQL_SA_PASSWORD` | the SA password and the API's `ConnectionStrings__Default` (server `sqlserver`) | `Local_Dev_Password1` |
| `JWT_SIGNING_KEY` | `Jwt__SigningKey` | a `dev-only-` key, refused outside Development |
| `API_MIGRATE_AND_SEED` | `Database__MigrateAndSeedOnStartup` | `true` |

| Check from the brief | How I checked it | Result |
| --- | --- | --- |
| Clean build gives a healthy API with data | `docker compose down -v && docker compose up --build` | `/health` returned `{"status":"Healthy","checks":{"database":"Healthy"}}`; three migrations applied; 40 tickets |
| Restore layer reused | Edited one `.cs` file, rebuilt with `--progress=plain` | `RUN dotnet restore` was `CACHED`; only the source copy and publish re-ran |
| Image size | `docker image inspect` | **122 MB** runtime image vs **343 MB** SDK image (compressed sizes, as Docker Desktop's containerd store reports) |
| Not root | `docker compose exec api whoami` | `app` |
| No baked configuration | Listed `/app` in the container | only `appsettings.json`; `appsettings.Development.json` is excluded from publish |
| Web app against the container | `VITE_API_PROXY_TARGET=http://localhost:5080 npm run web` | signed in and listed tickets through the proxy |

**Migrations in a real deployment.** Not on start-up. I would generate an idempotent script or a
migration bundle (`dotnet ef migrations bundle`) in CI, review it, and run it as its own pipeline
step before the new version rolls out, with a database identity allowed to change schema. Running
it from the app means every replica races to migrate, the app needs DDL rights it should not
have, a long migration fails health checks, and nobody reviews the SQL. Demo data is never seeded
outside development.

## Database

| Added | Why |
| --- | --- |
| `Tickets.SlaWindowSeconds` (int, nullable, check `> 0`) | The window that ends at `DueAtUtc`, so At risk is right after an escalation restarts the clock. The migration backfills it for existing tickets from `DueAtUtc - CreatedAtUtc` |
| `TicketEscalations` table | Id, TicketId, From/To Priority, From/To AgentId, From/To DueAtUtc, Reason (500), EscalatedBy (100), EscalatedAtUtc. Every foreign key is `Restrict`: an audit row must never disappear as a side effect. A check constraint (`ToPriority = FromPriority + 1`) holds the one-level rule in the database too |
| `TicketEscalations.EscalatedByAgentId` (nullable foreign key) | Who escalated, exactly: names change and are not unique, so the name stays for display and the id identifies the person. Null only for rows written before sign-in existed |
| **Index `IX_TicketEscalations_TicketId_EscalatedAtUtc` (TicketId ASC, EscalatedAtUtc DESC)** | The only read is "this ticket's history, newest first": SQL Server seeks to the ticket and reads rows already in order, no sort. It also serves the TicketId foreign key, so there is no separate TicketId index |
| Indexes on `FromAgentId`, `ToAgentId` and `EscalatedByAgentId` | Created by EF for the agent foreign keys; they keep the restrict check cheap if an agent row is ever deleted, and support "everything this agent escalated" |
| `Agents.PasswordHash` (nvarchar(200), nullable) | Credentials; an Identity V3 hash is 84 characters |

## SLA summary report (stretch)

One SQL statement per request: tickets created in `[from, to)` (UTC, default the last 30 days) are
classified with the shared SLA CASE, grouped, counted per state with `COUNT(CASE ...)` and averaged
with `AVG(DATEDIFF(second, ...))`. The agent view starts from the agents table, so every active
agent is listed, idle ones with zeros (the people a team lead looks for when rebalancing work);
inactive agents appear only while they have tickets in the period, and "Unassigned" joins with
`UNION ALL` in the same statement. Every priority is always listed. Breach rate is breached over
tickets that have an SLA. Tests count the commands (exactly one) and check every group against the
C# rule on a throwaway SQL Server database built by the real migrations.

---

## Testing

| Suite | Count | What it covers |
| --- | ---: | --- |
| Unit (xUnit + Moq) | 160 | SLA windows per priority and tier and the 1 h floor (with its own settings, because the defaults never reach it); forced Critical; agent selection (fewest open, specialists, limits, inactive, ties, nobody eligible); escalation (one step, due from now, owner kept or changed, history row, each rejection); SLA status at the exact boundaries; sign-in paths and the one-hash-check-per-path rule; hashing and tokens; filters and paging on real SQL (SQLite); SQL/C# parity for SLA |
| Integration (WebApplicationFactory) | 14 | The real pipeline: every endpoint 401 without a token, sign-in answers, tampered tokens, revocation on deactivation, `/health`, Swagger, actor from token; 4 opt-in tests on SQL Server for the report (incl. empty periods) |
| Architecture | 4 | Layer dependencies and "the model matches the migrations" |
| Web (Vitest + React Testing Library) | 35 | DEFECT-117 regressions, SLA indicator states, escalation validation and 409, history rendering (incl. an XSS payload), sign-in flow and redirect, token attachment and 401 handling, session expiry |

Tests that pin a defect were written first and seen to fail on the old code. For the security
rules I also broke the code on purpose and watched the tests fail (removing the fallback policy;
disabling the assignment lock).

**Deliberately not automated:** the SQL Server application lock (verified by firing 12
simultaneous creates against the real API with and without it, see APPROACH.md); browser
end-to-end tests (I drove the app with headless Chrome scripts for the screenshots but did not
add an E2E suite); visual layout and CSS; EF-generated migration code; load tests.

---

## AI usage

I used Claude Code as a pair programmer for this assignment, primarily to understand the starter code and assist with implementation. I defined the working rules and skills, directed the implementation process, and reviewed each AI-generated output before proceeding.

- **A workflow that worked:** for each task, a short "decision, challenge, verdict" table before
  any code, then a failing test, then the change, then verification against the real system. It
  caught several problems before they reached a commit.
- **Accepted as-is:** the EF Core-generated migrations (after reading them), the `WhereIf`
  helper, and the extracted list projection.
- **Changed or rejected:**
  - The first DEFECT-117 fix still sent a request per keystroke; the tests caught it.
  - Its first stale-response guard only ignored old responses. I asked for the decision to be
    challenged, and it became a real `AbortController` cancellation, so the API stops the query.
  - `SlaStartedAtUtc` was replaced by `SlaWindowSeconds` once it was clear SQLite could not run
    the date difference.
  - A test named "keeps the 409 message visible" did not actually trigger a 409; it was rewritten
    to do the real flow.
  - Screenshots showed the escalate panel off-screen on a laptop, so I had it moved.
- **Bugs AI introduced, caught before commit:**
  - The keystroke bug above.
  - A test whose timing did not match how a user types.
  - The first package pin (10.0.12) was still vulnerable. Checking the advisory's patched-version
    list caught it; the fix is 10.0.13.
  - `DATEDIFF(minute)` counts boundaries, not elapsed time; replaced with seconds.
- **Bugs AI helped find in the existing code:**
  - The DEFECT-117 root cause.
  - Concurrent creates failing with duplicate references.
  - The identity-gap reference jump.
  - The vulnerable transitive package, plus two dev-tool advisories.
  - The NULL-first due-date sort.
  - `appsettings.Development.json` being published into the image.
- **How I verified generated code:**
  - Every change ran through the full suites.
  - Each commit was tested in isolation.
  - I read the generated SQL.
  - I compared behaviour on SQL Server with the tests' SQLite results.
  - I measured instead of assuming: timing, concurrency, image sizes, the restore cache.
  - I checked each screen in headless Chrome screenshots.
  - My own questions while reviewing changed the result: the screen length (the panel move), the
    sign-in placeholder, how input is sanitised (the XSS test), and whether the colours came from
    the existing theme (they do; no new colours).

---

## Assumptions

- **DEFECT-104** appears only in `.github/pull_request_template.md`, with no description anywhere.
  I recorded it rather than guess; it looks like a leftover from another version of the brief.
- The repository README calls Task 4 optional; the assignment does not. I completed it.
- Search is case-insensitive through the SQL Server collation, and matches title, reference or
  customer name anywhere in the text.
- A ticket exactly at its due moment is At risk, not yet Breached.
- The 1 h minimum applies to the premium reduction; a base window configured below the minimum is
  rejected at start-up.
- Escalating an unassigned ticket assigns it per BR-5 if anyone is eligible.
- A ticket cannot be raised in an inactive category (409).
- `EscalatedBy` stores the agent's full name, for a readable history.
- Report periods use ticket creation time, `[from, to)` in UTC. Every priority and every active
  agent is listed, with zeros when they had no tickets; inactive agents only when they did.
- Only agents sign in. Customers do not, and there is no self-registration: this is an internal
  staff tool, where accounts are provisioned.

## Known limitations, and what I would do next

- **Authentication:**
  - The token is in `sessionStorage`, readable by injected script. Production would hold it in
    memory with an HttpOnly refresh cookie.
  - There's no rate limiting or lockout on sign-in (the brief says not to build them). Together
    with storage, this is now the weakest part.
  - The per-request "still active?" check costs one primary-key lookup per call. A cached denylist
    would replace it at high traffic.
  - No registration, password reset, roles, SSO or MFA (out of scope by design).
- **Scale (10 million tickets):**
  - `LIKE '%x%'` search scans; I would add a full-text index or a search service.
  - Offset paging and exact counts slow down; I would use keyset paging and an estimated count.
  - The due-date sort expression stops an index-ordered scan; I would add a computed sort column.
  - The report scans its period and repeats the SLA CASE per count; I would use a nightly
    snapshot table or a columnstore index.
- **Assignment:**
  - The lock is global, which serialises creates and escalations. That's fine for a support desk;
    I'd partition it by category if volume demanded.
  - The workload query reads every agent; fine for tens of agents.
  - Manual assignment still bypasses the specialist and capacity rules (existing behaviour, kept).
- **Production image:**
  - `aspnet:10.0-noble-chiseled-extra` (non-root, no shell, ICU kept for SQL Server).
  - A read-only file system.
  - Container health probes on `/health`.
  - Secrets from a store.
  - Migrations as a separate step.
- **UI:**
  - Filters are not in the URL, so links and back/forward do not keep them.
  - No E2E suite.
  - The escalation history is not paged.
- **CI:** the SQL Server tests are opt-in and do not run in GitHub Actions; CI changes were out of
  scope.

---

<details>
<summary><strong>API reference</strong></summary>

| Endpoint | Description |
| --- | --- |
| `POST /api/auth/login` | Sign in (open). Returns `{ token, expiresAtUtc, agent }` |
| `GET /api/auth/me` | The signed-in agent, read fresh |
| `GET /api/tickets` | Paged list: `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`, `sortBy`, `sortDirection`, `page`, `pageSize` |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Raise and triage a ticket: 201 with `{ ticket, triage }` |
| `POST /api/tickets/{id}/escalate` | Body `{ reason }`: 200 with `{ ticket, escalation }`; 400, 404, 409 |
| `GET /api/tickets/{id}/escalations` | History, newest first |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |
| `GET /api/reports/sla-summary` | `from`, `to`, `groupBy=agent\|priority` |
| `GET /health` | Database reachability (open) |

Errors are `ProblemDetails`: 400 validation, 401 not signed in, 404 not found, 409 conflict or
business rule.

</details>

<details>
<summary><strong>Commands</strong></summary>

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `docker compose up --build` | SQL Server and the API container |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

</details>

<details>
<summary><strong>Troubleshooting</strong></summary>

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait about 20 s, check `docker compose ps` |
| API stops at start-up naming `Sla` or `Jwt` | A configuration value is missing or invalid; the message says which |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app redirects to the sign-in page | Expected when signed out; use the development login above |
| SQL Server slow or crashing on Apple Silicon | Enable Rosetta in Docker Desktop, Settings, General |

</details>
