# NutriForge — Architecture Decision Records

Append-only. ADRs are numbered sequentially and never renumbered. Reverting a decision
adds a new ADR that supersedes the old one (with a back-reference); the old one stays.
Decisions already mandated by the enterprise guardrails are *constraints*, not ADRs, and
are recorded in [`ARCH.md`](ARCH.md) directly. These ADRs cover the non-default choices
and the guardrail reconciliations.

---

## ADR-0001: The user is the tenant boundary

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture (design-architecture phase)
- **Affects**: `ARCH.md` → Cross-cutting wiring (Multi-tenancy), Data model, SPA architecture

### Context

The enterprise guardrails mandate multi-tenancy enforced at the data layer via EF Core
query filters. NutriForge is a **B2C consumer** product ([README](README.md)): there is
no tenant organization above the user — each person owns only their own diary, plans, and
pantry. Imposing a separate tenant dimension would add a column and a filter that is always
1:1 with the user and never exercised.

### Decision

We will treat the **authenticated user as the isolation principal**. A global EF Core query
filter on `UserId` enforces per-user isolation on every user-owned entity; the shared
food/recipe catalog is unfiltered (public-read). No separate `TenantId` dimension at v1.

### Consequences

- **Positive**: satisfies the data-layer-isolation intent of the guardrail with the same
  mechanism (global query filters), without a dead column or a tenant-switch UI.
- **Negative**: introducing true B2B multi-tenancy later (e.g. a clinic managing many
  clients) requires adding a `TenantId` and re-keying user-owned tables — a migration.
- **Neutral**: the SPA top bar has a user menu but no tenant switcher.

### Alternatives considered

- **Full `TenantId` dimension now** — every user-owned row carries a tenant id. Rejected:
  speculative generality for a B2C app; violates "design for v1's metrics, not 10x."
- **No query filters, manual `WHERE userId` per query** — Rejected: one missed predicate is
  a cross-user data leak; the global filter is the safer default.

---

## ADR-0002: OIDC via Entra External ID, not bare Identity+JWT

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → Cross-cutting wiring (AuthN), Cloud topology (Identity)

### Context

The prior design note ([system-design §5](docs/architecture/system-design.md)) specified
"ASP.NET Core Identity / JWT." The guardrails mandate **AuthN via OIDC** (Entra ID on
Azure, Cognito on AWS). The cloud target is Azure ([ADR-0010](#adr-0010-target-azure-as-the-single-cloud)).

### Decision

We will authenticate via **OIDC against Entra External ID** (the consumer/B2C tenant
flavor). The SPA uses Authorization-Code + PKCE; the API validates JWTs as a standard
resource server. A local `User` row mirrors the OIDC `sub` for foreign keys.

### Consequences

- **Positive**: guardrail-compliant; offloads password storage, MFA, social login, and
  token issuance to a managed IdP; the API holds no credential store.
- **Negative**: local dev needs an Entra External ID tenant (or a stub OIDC provider);
  slightly more setup than self-hosted Identity.
- **Neutral**: roles (`user`/`admin`) come from token claims, not a local table.

### Alternatives considered

- **ASP.NET Core Identity + self-issued JWT** — the original note. Rejected: deviates from
  the OIDC guardrail; puts credential storage and MFA in our scope.
- **Auth0/Clerk** — capable, but off the Azure-managed path the rest of the stack uses;
  adds a vendor outside the chosen cloud.

---

## ADR-0003: Modular monolith, not microservices, at v1

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → Containers, Solution layout

### Context

The three pillars are tightly coupled through the shared food/nutrient core and share
transactions (a diary entry references a food; a plan references recipes that reference
foods). The guardrails don't mandate a deployment shape, so this is a genuine decision.

### Decision

We will ship a **modular monolith** — one deployable ASP.NET Core app with strict internal
bounded-context boundaries (one `Application.<Context>` project each, references by id
across contexts) — orchestrated locally by Aspire. We extract a service only when a named
bottleneck appears (most likely the import pipeline or DietGen under heavy LLM load).

### Consequences

- **Positive**: no distributed transactions, no chatty cross-service nutrient lookups, one
  F5 dev experience; boundaries are enforced in-process so a later split stays cheap.
- **Negative**: all contexts scale together; a runaway context can't be scaled in isolation
  until extracted.
- **Neutral**: deployment is a single Container App (plus the import worker).

### Alternatives considered

- **Microservices per pillar** — Rejected: distributed transactions and network hops for
  zero scaling benefit at this scale; premature.
- **Single project, no module boundaries** — Rejected: loses the option to split later and
  invites a big ball of mud.

---

## ADR-0004: Hybrid generate-and-check — the LLM never owns a number

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → MAF agents, Components (DietGen); the central design rule

### Context

Diet generation must hit calorie/macro targets *and* honor allergens (a safety constraint)
while still producing appetizing, varied food. Pure optimization gives correct numbers but
unappetizing menus; a pure LLM gives appetizing menus but unreliable numbers and unsafe
allergen handling (NL→structured-filter accuracy ~91% — not safe enough for allergens).

### Decision

We will use a **hybrid generate-and-check** pipeline: the **LLM owns intent, taste, variety,
and explanation**; **deterministic .NET code owns every number and every safety constraint**
(FILTER, nutrition VERIFY, allergen enforcement, LP REPAIR). Every LLM call uses structured
output bound to a C# type; `select()` returns `PlanSlot[]` (ids + servings) so it is
type-impossible for the agent to return a calorie number. Allergens are enforced
deterministically in FILTER and re-asserted in VERIFY (defense in depth).

### Consequences

- **Positive**: grounded generation keeps calorie error low (~1.5–3.7% vs target); allergen
  safety never depends on model accuracy; failures are loud (structured-output validation).
- **Negative**: more moving parts than a single LLM prompt; requires the candidate-pool tool
  and the VERIFY/REPAIR machinery.
- **Neutral**: the LLM "talks" only in EXPLAIN, reporting already-verified facts.

### Alternatives considered

- **Pure LLM plan generation** — Rejected: unreliable numbers, unsafe allergen handling.
- **Pure linear/genetic optimization** — Rejected: correct numbers, unappetizing and
  rigid menus; no natural-language front door.

---

## ADR-0005: Google OR-Tools for portion repair

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → Components (DietGen, REPAIR step); not in the guardrail tech list

### Context

The REPAIR step ([meal-plan-generation §5](docs/algorithms/meal-plan-generation.md)) tunes
per-meal serving multipliers to minimize macro deviation — a small linear program (linear,
additive nutrients, bounded weights). This needs a deterministic solver; OR-Tools is not on
the guardrail technology list, so it requires an ADR.

### Decision

We will use **Google OR-Tools** (GLOP/HiGHS linear solver) in `Application.DietGen` to solve
the portion-tuning LP. It runs in-process, in milliseconds, deterministically, with no
external service.

### Consequences

- **Positive**: provably on-target portions rather than LLM-approximate ones; fast and
  deterministic; no network dependency.
- **Negative**: a native-interop dependency (OR-Tools ships native binaries) to manage in
  build/deploy.
- **Neutral**: REPAIR tier 5b (re-select) still loops back to the LLM, bounded to ≤3 iterations.

### Alternatives considered

- **HiGHS via a thin managed wrapper** — viable and lighter; kept as the fallback if the
  OR-Tools native dependency proves awkward in Container Apps.
- **Hand-rolled gradient/heuristic scaling** — Rejected: re-implements a solved problem and
  loses optimality guarantees.

---

## ADR-0006: Log-time nutrition snapshot on diary entries

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → Data model (`DiaryEntry`)

### Context

Food data is corrected over time (an import fixes a wrong value). A user's historical diary
days must not silently change underneath them when that happens.

### Decision

We will **denormalize the computed macros `{kcal, p, f, c}` onto each `DiaryEntry` at log
time**. Day rollups read the snapshot, not the live `Food`.

### Consequences

- **Positive**: historical days are immutable and auditable; rollups don't re-query and
  recompute live food data.
- **Negative**: a later food correction does not retroactively fix past entries (correct
  behavior, but must be communicated if a user asks why an old day differs from the food's
  current values).
- **Neutral**: a small denormalized payload per entry.

### Alternatives considered

- **Recompute from live `Food` on every read** — Rejected: historical days mutate when food
  data changes; also slower (join + recompute per rollup).

---

## ADR-0007: Diets as data, not code

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → Data model (`DietType`/`DietRule`), Components (DietGen FILTER)

### Context

NutriForge must support many diets (vegan, vegetarian, keto, paleo, Mediterranean,
gluten-free, halal, kosher, …) and add more over time. The FILTER step needs a rule set per
diet.

### Decision

We will model diets as **data** — `DietType` + `DietRule` rows (kinds:
`excludeCategory | excludeIngredient | macroConstraint | requireTag`). Adding or tuning a
diet is an insert/update, not a deployment.

### Consequences

- **Positive**: new diets ship without code changes; rules are inspectable and testable as data.
- **Negative**: the rule engine must stay expressive enough for real diets; a genuinely
  novel rule kind still needs an engine change.
- **Neutral**: diet rules live in the shared catalog, seeded at migration time.

### Alternatives considered

- **A C# class/strategy per diet** — Rejected: every new diet is a code change and deploy;
  doesn't scale to the long tail of diets.
- **LLM-judged diet adherence** — Rejected: allergen/diet safety must be deterministic, not
  model-judged (see [ADR-0004](#adr-0004-hybrid-generate-and-check-the-llm-never-owns-a-number)).

---

## ADR-0008: Async plan generation via 202 + poll + outbox job

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → API surface (diet-plans), Cross-cutting wiring (Background work, Outbox), SPA

### Context

A diet-plan generation run (PARSE → FILTER → SELECT → VERIFY → REPAIR loop ≤3 → EXPLAIN)
involves multiple LLM round-trips plus solving and can take seconds. Holding an HTTP request
open for the whole run is fragile and ties up server resources.

### Decision

We will make generation **asynchronous**: `POST /api/v1/diet-plans` returns
`202 {planId, status:"generating"}`, dispatches the job through the **outbox**, and an
in-process `BackgroundService` runs the pipeline; the SPA **polls** `GET /diet-plans/{id}`
(via TanStack Query) until `ready` or `infeasible`. No external queue at v1.

### Consequences

- **Positive**: requests stay short; the job survives transient failures (outbox + retry);
  TanStack Query handles polling/caching natively; the full run is one OTel trace.
- **Negative**: clients must poll (slightly more chatter than a push); eventual-consistency
  UX needs a "generating" state.
- **Neutral**: the same job mechanism can later move to an external queue if extracted.

### Alternatives considered

- **Synchronous request/response** — Rejected: long-held connections, poor failure handling,
  timeouts on slow LLM runs.
- **SignalR/WebSocket push** — viable and lower-latency, but adds a stateful transport;
  deferred until polling proves insufficient.

---

## ADR-0009: Postgres FTS + Redis for food search, no dedicated search engine

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → Containers, Cross-cutting wiring (Caching), API surface (`/foods/search`)

### Context

Food search is the #1 hot path (~86% of logging actions). It needs to be fast and
well-ranked, but the corpus is bounded (USDA + Open Food Facts) and the query is simple
(name/brand prefix + ranking by `verificationStatus` + popularity).

### Decision

We will serve food search with **PostgreSQL full-text search**, fronted by a **Redis cache**
keyed on normalized query + filters. No separate search engine at v1.

### Consequences

- **Positive**: one fewer service to run/operate; Redis absorbs the hot path; Postgres FTS
  is plenty for a bounded catalog.
- **Negative**: advanced relevance (typo tolerance, synonyms, learning-to-rank) is limited
  vs a dedicated engine; revisit if search quality becomes a named problem.
- **Neutral**: pgvector (Phase 5) can add fuzzy ingredient matching on the same Postgres.

### Alternatives considered

- **Elasticsearch / OpenSearch / Azure AI Search** — Rejected at v1: an extra stateful
  service and operational surface for a bounded catalog; premature.

---

## ADR-0010: Target Azure as the single cloud

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → Cloud topology; all `Infrastructure.<Cloud>` partitioning

### Context

No `workflow.json` declared a cloud target. The guardrails require one cloud per deployment
with cloud-specific code partitioned behind interfaces. [tech-stack.md](docs/architecture/tech-stack.md)
already recommends Azure (Container Apps, Azure Database for PostgreSQL, Azure Cache for
Redis, Azure OpenAI, Static Web Apps) and the agentic stack (MAF/MEAI) is Azure-native.

### Decision

We will target **Azure** as the single cloud, with all cloud-specific code isolated in
`NutriForge.Infrastructure.Azure` behind interfaces from `NutriForge.Infrastructure`, so a
second target (e.g. AWS) is a finite project, not a rewrite. IaC is **Terraform**; CI/CD is
**GitHub Actions**.

### Consequences

- **Positive**: managed services line up with the stack; Aspire emits a manifest that maps
  to Container Apps; Azure OpenAI is the default MEAI provider.
- **Negative**: vendor concentration on Azure (mitigated by the interface partition + MEAI
  provider-swap to Anthropic/Ollama).
- **Neutral**: local dev uses Aspire-managed Postgres/Redis containers and Ollama, so day-to-day
  dev is cloud-free.

### Alternatives considered

- **AWS (ECS/Fargate, RDS, Cognito, Bedrock)** — equally viable; rejected only because the
  existing research and the MAF/MEAI default path point to Azure. The interface partition
  keeps this reversible.
- **Cloud-agnostic Kubernetes from day one** — Rejected: operational overhead beyond v1's needs.

---

## ADR-0011: Append-only audit log in a separate database

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture
- **Affects**: `ARCH.md` → Cross-cutting wiring (Observability/audit), Cloud topology (Audit sink)

### Context

The guardrails require append-only audit logging for every domain mutation, stored **outside
the operational DB**, so audit integrity survives an operational-DB compromise or bug.

### Decision

An EF Core `SaveChanges` interceptor will capture every domain mutation and write an
append-only audit record to a **separate Postgres database** via the **outbox** (so an audit
record never diverges from the mutation that caused it). `[Pii]` values are redacted in audit
records; the audit database is archived to append-only Azure Blob for retention.

### Consequences

- **Positive**: audit survives independent of the operational DB; outbox guarantees
  mutation↔audit consistency; PII stays out of the audit trail.
- **Negative**: a second database to provision and back up; the interceptor adds a small
  per-mutation cost.
- **Neutral**: same Postgres Flexible Server, different database — no new service type.

### Alternatives considered

- **Audit rows in the operational DB** — Rejected: violates the "outside the operational DB"
  guardrail; a single compromise taints both data and audit.
- **Log-only audit (OTel/Serilog to a sink)** — Rejected as the system of record: logs are
  not an append-only, queryable, tamper-evident store; kept as a complement, not the source of truth.

---

## ADR-0012: Clean Architecture layering inside the modular monolith

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture (dotnet-architecture phase)
- **Affects**: `ARCH.md` → Solution layout; refines [ADR-0003](#adr-0003-modular-monolith-not-microservices-at-v1)

### Context

[ADR-0003](#adr-0003-modular-monolith-not-microservices-at-v1) chose a modular monolith but
left the *internal* layering open. The codebase needs an enforceable dependency rule so the
bounded contexts (Food, Tracking, Recipes, DietGen) stay decoupled and the AI/persistence
details never leak into the domain — the `dotnet-architecture` skill's default.

### Decision

We will layer each deployable with **Clean Architecture**: `Domain` (entities, value objects,
NutritionTargets pure math — depends on nothing) ← `Application.<Context>` (use cases, ports;
depends only on Domain) ← `Infrastructure*` (EF Core, Redis, MAF/MEAI, importers; implements
the ports) ← `Api` (composition root, minimal-API endpoints). The dependency rule is
**inward-only** and enforced by project references; cross-context access is by id, never by
navigation. Package versions are centralized via **Central Package Management**.

### Consequences

- **Positive**: the domain + targets math are pure and fully unit-testable (the golden test);
  swapping EF/Redis/LLM providers touches only Infrastructure; boundaries make a later
  service-extraction cheap.
- **Negative**: more projects and some mapping/port boilerplate than a single-project app.
- **Neutral**: a feature touches multiple layers; mitigated by feature-aligned `Application.<Context>` projects.

### Alternatives considered

- **Vertical slice (feature folders, no layer projects)** — good for CRUD-heavy apps; rejected
  as the default because NutriForge has substantial shared domain logic (nutrition math, the AI
  boundary) that benefits from an explicit Domain/Application split. Vertical slices may still be
  used *inside* `Application.<Context>` where a context is mostly CRUD.
- **Transaction script / DbContext-in-controllers** — Rejected: leaks persistence into the API
  and dissolves the bounded-context boundaries the monolith depends on.

---

## ADR-0013: Realize the Azure target on Container Apps (ACR + Postgres Flex + Redis + UAMI/KV)

- **Status**: accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture (dotnet-architecture phase)
- **Affects**: `ARCH.md` → Cloud topology; realizes [ADR-0010](#adr-0010-target-azure-as-the-single-cloud)

### Context

[ADR-0010](#adr-0010-target-azure-as-the-single-cloud) pinned Azure as the cloud. The
`dotnet-architecture` skill's tested scaffold defaults to **App Service**, but NutriForge has
two compute units (the API and a *scheduled* import worker) and is Aspire-orchestrated, so its
manifest maps more naturally to Container Apps. The concrete service set, registry strategy, and
identity wiring needed to be fixed so Terraform is buildable.

### Decision

We will realize the deployment on **Azure Container Apps**: an ACA **Environment** wired to Log
Analytics; the API as an **external-ingress Container App**; the importer as a **Container App
Job** on a nightly cron (scales to zero between runs); images in **Azure Container Registry**
(admin disabled — identity-based pulls); **PostgreSQL Flexible Server** (Entra-only auth) hosting
`appdb` + `auditdb`; **Azure Cache for Redis**. A single **User-Assigned Managed Identity** is the
workload identity for ACR pull, Key Vault Secrets User, and the Postgres role; **no secret** is in
source, tfvars, or container env (the Redis key lives in Key Vault, surfaced as a KV-referenced ACA
secret). IaC is **Terraform** (remote azurerm state); CI/CD is **GitHub Actions** over **OIDC**.

### Consequences

- **Positive**: scale-to-zero on dev API and on the import job cuts cost; the Aspire manifest maps
  cleanly; one identity for every grant; deviates from the skill's App Service scaffold only where
  the workload shape demands it.
- **Negative**: more infra surface than App Service (ACA env + ACR + Job); a post-provision
  step is required — map the UAMI to a Postgres role (`CREATE ROLE "<uami-name>" WITH LOGIN; GRANT
  …`) before the app can connect.
- **Neutral**: first `terraform apply` uses a placeholder image; `cd-app.yml` rolls the real
  ACR image (`az containerapp update` / `job update`).

### Alternatives considered

- **Azure App Service (Linux)** — the skill default and simplest for a single web app; rejected
  because the separate scheduled import worker fits a Container App Job better than a WebJob, and
  the Aspire manifest targets ACA. (Recorded here so the deviation from the scaffold default is explicit.)
- **AKS** — Rejected: a cluster + platform-team overhead far beyond v1's needs.
- **ACR admin user / registry password** — Rejected: violates the no-stored-secrets guardrail;
  identity-based `AcrPull`/`AcrPush` is used instead.

## ADR-0014: Web recipe import is a deterministic, SSRF-isolated connector — AI is the fallback, not the path

**Status:** Accepted · **Context:** Epic 14 / #91.

Importing a recipe from a web URL must not depend on an LLM (cost, latency, a required key) when the
page already ships machine-readable data, and fetching a user-supplied URL server-side is a textbook
SSRF vector.

**Decision.**
- **Deterministic first.** Most recipe sites embed `schema.org/Recipe` as `application/ld+json`. The
  importer fetches the page and parses that JSON-LD (`JsonLdRecipeParser`) into the same
  `ExtractedRecipe` the AI extractor produces, so resolve → compute → confirm → save is unchanged. The
  AI extractor is used ONLY for inputs with no structured data (a YouTube description, free-text paste).
  Consequence: web import works with **no API key**; only YouTube/free-text returns 503 when AI is off.
- **Nutrition is still never trusted from the source** (ADR-0004): the parser yields names/quantities/
  steps; the catalog resolver owns every macro.
- **SSRF isolation.** The fetcher is a DEDICATED typed `HttpClient` (not the shared resilience client)
  whose `SocketsHttpHandler.ConnectCallback` resolves the host, drops every loopback / private /
  link-local / CGNAT / multicast / cloud-metadata (169.254.169.254) / IPv6-ULA address
  (`PrivateNetworkGuard`), and connects only to a surviving public IP. Every redirect hop re-enters the
  callback, so a public→internal redirect or DNS rebind is rejected too. Plus: http/https only, ~10s
  timeout, ~3 MB streamed cap, HTML content-type only, faults degrade to null.
- **ToS posture.** Only public structured metadata is read; no transcript scraping or paywalled
  content (transcript enrichment is the separate, opt-in #93).

**Alternatives rejected.** AI-only import (needless key/cost/latency for sites with JSON-LD); a host
allowlist alone (brittle + still rebindable — the IP-level connect gate is the real control);
the generic resilience HttpClient (its retries/redirects would bypass the per-connection SSRF gate).
