# NutriForge — Architecture

> **Phase 4 artifact (design-architecture).** Turns the research/design in
> [`README.md`](README.md), [`ROADMAP.md`](ROADMAP.md) (the plan), and
> [`docs/architecture/*`](docs/architecture/) (the prior design notes) into concrete,
> buildable architectural decisions, reconciled against the enterprise guardrails.
> Companion artifacts: [`DECISIONS.md`](DECISIONS.md) (ADRs) and
> [`docs/diagrams/`](docs/diagrams/) (C4).
>
> **Inputs consulted:** `README.md` (vision + 3 pillars = spec), `ROADMAP.md`
> (Phases 0–5 = epics + build order), `docs/architecture/{system-design,data-model,api-design,tech-stack}.md`,
> `docs/algorithms/{tdee-and-macros,meal-plan-generation}.md`. No `workflow.json`
> present → cloud target chosen as **Azure** per `tech-stack.md` ([ADR-0010](DECISIONS.md#adr-0010-target-azure-as-the-single-cloud)).

---

## Context (C4 L1)

NutriForge is a single system (a modular-monolith API + SPA) serving one external
actor — the **end user** (a B2C consumer tracking food, cooking in batches, and
generating diets). It consumes three external systems, all as *import-time or
provider-swappable* dependencies, never on the synchronous user hot path:

| External | Role | Direction |
|---|---|---|
| **USDA FoodData Central** | Authoritative seed for the food/nutrient core | Inbound import (nightly worker) |
| **Open Food Facts** | UPC-indexed branded foods for barcode logging | Inbound import (nightly worker) |
| **LLM provider** (Azure OpenAI prod / Anthropic / Ollama dev) | Intent parse, SELECT, narration — via MEAI `IChatClient` | Outbound, behind Polly |
| **Entra External ID** (OIDC) | AuthN for the SPA | Outbound (redirect/token) |

Diagram: [`docs/diagrams/c1-context.puml`](docs/diagrams/c1-context.puml)

The system itself owns its food/nutrient store; the LLM never owns a number that
matters (the central design rule — see [ADR-0004](DECISIONS.md#adr-0004-hybrid-generate-and-check-the-llm-never-owns-a-number)).

---

## Containers (C4 L2)

Every box below is an Aspire resource composed by `NutriForge.AppHost`.

| Container | Tech | Responsibility |
|---|---|---|
| **SPA** (`NutriForge.Web`) | Vite + React + TS + shadcn/ui + Tailwind | Search→log UI, plan view, chatbot slide-over |
| **API** (`NutriForge.Api`) | ASP.NET Core 10 minimal APIs | The modular monolith; all bounded contexts in-process |
| **Import worker** (`NutriForge.ImportWorker`) | .NET worker (hosted services) | USDA/OFF nightly sync; never on a request path |
| **PostgreSQL** | Postgres 16 + pgvector (later) | Foods, recipes, diary, plans, outbox, idempotency |
| **PostgreSQL (audit)** | Postgres 16, separate database | Append-only audit log, outside the operational DB |
| **Redis** | Redis 7 | Food-search cache, parse/intent cache, rate-limit counters, distributed lock |
| **LLM provider** | Azure OpenAI (MEAI `IChatClient`) | Parse / SELECT / explain — outbound only |

The MAF agents are **not** a separate container at v1 — they run in-process inside the
API (`NutriForge.Infrastructure.Ai`). DietGen under heavy LLM load is the named
extract-to-service candidate ([system-design §6](docs/architecture/system-design.md)),
but extraction is deferred until a real bottleneck appears.

Diagram: [`docs/diagrams/c2-containers.puml`](docs/diagrams/c2-containers.puml)

---

## Components (C4 L3) — key containers only

**Inside the API** (`NutriForge.Api`), endpoint groups map 1:1 to bounded contexts,
each delegating to its `Application.<Context>` handlers; cross-cutting behaviors
(idempotency, validation, audit, tenant filter) sit in shared pipeline behaviors.

- **Food endpoints** → `Application.Food` (search, barcode, get, user-submit)
- **Tracking endpoints** → `Application.Tracking` (profile, targets, diary, NL parse)
- **Recipe endpoints** → `Application.Recipes` (recipes, scale, pantry, shopping list)
- **DietGen endpoints** → `Application.DietGen` (async plan generate/poll/accept)
- **Assistant endpoint** → `Infrastructure.Ai` chatbot agent (slide-over)

Diagrams:
- API: [`docs/diagrams/c3-components-api.puml`](docs/diagrams/c3-components-api.puml)
- DietGen pipeline (the flagship): [`docs/diagrams/c3-components-dietgen.puml`](docs/diagrams/c3-components-dietgen.puml)

---

## Solution layout

Documents the *shape* — which projects exist and the epic each serves. The concrete
.NET 10 + Aspire backbone (the `dotnet new`/wiring steps) is owned by the
`aspire` and `entity-framework-core` stack skills and realized in `build-system`;
this section is the map they follow. Naming follows the established `NutriForge.*`
convention from [system-design §2](docs/architecture/system-design.md), not the
generic `The<Domain>` placeholder.

```
src/
  NutriForge.AppHost/                       ← Aspire AppHost: composes all resources         [all phases]
  NutriForge.ServiceDefaults/               ← OTel + health checks + Polly resilience        [Phase 0]
  NutriForge.Api/                           ← minimal APIs grouped by bounded context         [Phases 0–4]
  NutriForge.Application/                    ← shared: idempotency, outbox dispatch, pipeline  [Phase 0]
                                              behaviors, GDPR export/delete orchestration
  NutriForge.Application.Food/               ← food search, import upsert, barcode lookup      [Phase 0, 2]
  NutriForge.Application.Tracking/           ← profile, target cache, diary, NL parse          [Phase 1, 2]
  NutriForge.Application.Recipes/            ← recipe parse/compute, scaling, shopping list     [Phase 3]
  NutriForge.Application.DietGen/            ← PARSE→FILTER→SELECT→VERIFY→REPAIR→EXPLAIN        [Phase 4]
  NutriForge.Domain/                         ← entities, value objects, domain events, AND      [Phase 0]
                                              NutritionTargets pure math (no I/O)
  NutriForge.Infrastructure/                 ← EF Core, DbContexts, query filters, outbox,      [Phase 0]
                                              audit interceptor, Redis, idempotency store
  NutriForge.Infrastructure.Ai/             ← MEAI IChatClient + MAF agents (provider-swap)    [Phase 2, 4]
  NutriForge.Infrastructure.UsdaFdc/        ← USDA FoodData Central importer (connector)        [Phase 0]
  NutriForge.Infrastructure.OpenFoodFacts/  ← Open Food Facts importer (connector)             [Phase 2]
  NutriForge.Infrastructure.Azure/          ← Azure OpenAI, Key Vault, Blob audit sink         [Phase 0+]
                                              (the only cloud-specific project)
  NutriForge.ImportWorker/                   ← background host for the importers                [Phase 0]
  NutriForge.Web/                            ← Vite + React SPA                                 [Phase 1+]

tests/
  one test project per source project; integration tests use Testcontainers (Postgres + Redis).
  NutriForge.Domain.Tests carries the tdee-and-macros golden test (the worked example).
```

**Dependency rule (no cycles):** `Domain` depends on nothing. `Infrastructure*`
depends on `Domain`. `Application.<Context>` depends on `Domain` + `Application`
(shared) and talks to infrastructure through interfaces. `Api` depends on the
`Application.*` projects. Food core is foundational; DietGen sits on top of Food +
Recipes + Tracking + NutritionTargets. This is the same partial order as
[ROADMAP](ROADMAP.md)'s dependency graph, so the solution builds in phase order.

---

## Cross-cutting wiring

Every guardrail item names a concrete implementation here. No requirement left silent.

- **AuthN** — OIDC via **Entra External ID** (consumer/B2C tenant). The SPA does
  Authorization-Code + PKCE; the API validates JWTs as a resource server. Replaces
  the prior "ASP.NET Core Identity + JWT" note ([ADR-0002](DECISIONS.md#adr-0002-oidc-via-entra-external-id-not-bare-identityjwt)).
- **RBAC** — two roles at v1: `user` (owns their own data) and `admin` (triggers
  imports, reads import status). Policy classes `OwnerOnly` and `AdminOnly`; the SPA
  gates the admin/import views by the same policy names. Internal import endpoints sit
  behind `AdminOnly` + network restriction.
- **Multi-tenancy** — NutriForge is B2C; the **isolation principal is the user**, not a
  separate tenant org. A global EF Core query filter on `UserId` enforces per-user
  isolation on every user-owned entity (`Profile`, `DiaryEntry`, `MealPlan`,
  `PantryItem`, `ShoppingList`). The shared food/recipe catalog is unfiltered (public-read).
  No separate tenant dimension at v1 ([ADR-0001](DECISIONS.md#adr-0001-the-user-is-the-tenant-boundary)).
- **Observability** — OpenTelemetry traces/metrics/logs via `ServiceDefaults`,
  surfaced in the Aspire dashboard (dev) and exported to Azure Monitor (prod). Health
  checks (`/health`, `/alive`) on API + worker. **Audit logging**: an EF Core
  `SaveChanges` interceptor writes an append-only record for every domain mutation to
  the **separate audit Postgres database** via the outbox, so audit survives independent
  of the operational DB ([ADR-0011](DECISIONS.md#adr-0011-append-only-audit-log-in-a-separate-database)).
  The full DietGen run (PARSE→VERIFY→REPAIR) is one trace, so plan failures are visible
  by step.
- **Resilience** — Polly handlers (retry + timeout + circuit breaker) on all outbound
  calls: the LLM provider, USDA, and Open Food Facts. Configured centrally in
  `ServiceDefaults` and applied via typed `HttpClient`s.
- **Caching** — Redis. Cached: food-search results (keyed on normalized query +
  filters, short TTL ~5 min), ingredient-parse results (keyed on raw line, long TTL —
  parse-once), LLM intent parses (keyed on normalized desire, medium TTL), derived
  `Target` (keyed on profile version, invalidated on profile change). Redis also backs
  rate-limit counters and a distributed lock for import idempotency.
- **Background work** — a single in-process scheduler. Two kinds: (1) **import worker**
  (`NutriForge.ImportWorker`) runs nightly USDA/OFF sync via hosted services; (2) the
  **async diet-plan generation job** is dispatched through the outbox and consumed by an
  in-process `BackgroundService`. No external queue service at v1.
- **Idempotency** — two layers. (a) Writes accept an `Idempotency-Key` header; an
  idempotency store in Postgres records `(key, userId, response)` with a **24h replay
  window**, so retries return the original result. (b) Imports upsert by
  `(provider, providerId)` (natural idempotency). Diet-plan generation carries a request
  id so a re-`POST` of the same desire is deduplicated.
- **Outbox** — the transactional outbox pattern in the operational DB captures external
  side effects (audit-log writes, diet-plan job dispatch, future notifications) and a
  dispatcher relays them, so a side effect never commits without its triggering
  transaction (and vice versa).
- **Configuration & secrets** — `IOptions<T>` for every config section, validated at
  startup (`ValidateOnStart`). Secrets (LLM keys, DB/Redis connection strings, OIDC
  client secret) come from **Azure Key Vault** in prod and user-secrets/env in dev;
  Aspire injects connection strings locally.
- **Compliance** — GDPR `GET /api/v1/me/export` returns the user's full data bundle;
  account deletion runs a per-user purge across all user-owned entities + audit
  tombstone. PII fields tagged `[Pii]` (see Data model) drive export/redaction.
- **Rate limiting** — ASP.NET Core rate limiter, per-user partition + per-endpoint
  overrides. Tight on the expensive paths (`POST /diet-plans`, `/diary/parse`,
  `/recipes/import` — all LLM-backed); generous on read paths (`/foods/search`). Plus
  explicit CORS allowing only the SPA origin.

---

## Cloud topology

Cloud-agnostic by construction: all cloud-specific code lives in
`NutriForge.Infrastructure.Azure` behind interfaces from `NutriForge.Infrastructure`,
so a second target is a finite project, not a rewrite.

- **Provider**: **Azure** (chosen from `tech-stack.md`; [ADR-0010](DECISIONS.md#adr-0010-target-azure-as-the-single-cloud))
- **Compute**: Azure Container Apps (API + import worker); Aspire emits the manifest
- **Data**: Azure Database for PostgreSQL Flexible Server (operational + audit databases)
- **Vector**: Postgres + **pgvector** (deferred to Phase 5; same server, no new service)
- **Cache**: Azure Cache for Redis
- **Secrets**: Azure Key Vault (via Managed Identity — no secrets in config)
- **Identity**: Entra External ID (OIDC)
- **LLM**: Azure OpenAI (prod) — provider-swappable to Anthropic or local Ollama via MEAI
- **Static SPA**: Azure Static Web Apps / CDN
- **Audit sink**: separate Postgres database; archived to Azure Blob (append-only) for retention
- **IaC**: **Terraform** (not Bicep, even on Azure). **CI/CD**: GitHub Actions.
- **Networking**: API + worker on a VNet; Postgres/Redis reached via private endpoints;
  public ingress only to the API (behind the rate limiter/CORS) and the static SPA.
- **Region**: **single region** at v1 — the spec states no data-residency or geo-redundancy
  requirement, so a multi-region topology would be premature. Revisit (add an ADR) if a
  residency or DR requirement appears.

---

## Data model (concrete)

EF Core 10 code-first, schema-per-bounded-context sharing one operational database
([data-model.md](docs/architecture/data-model.md)). One `DbContext` per context
(`FoodDbContext`, `TrackingDbContext`, `RecipeDbContext`, `DietGenDbContext`),
each owning its schema; cross-context references are by id, not navigation, to keep
boundaries honest.

**Shared catalog (unfiltered, public-read):**
- `Food` (id, name, brand?, gtin? *indexed*, canonicalIngredientId?, verificationStatus
  enum, source jsonb `{provider, providerId}` *unique*) — `1:1 NutrientProfile`, `1:N Portion`
- `NutrientProfile` (per-100g vector; macros required, micros nullable)
- `Portion` (foodId, name, grams)
- `Ingredient` (canonical: canonicalName, category→aisle, density?, preferredUnit,
  defaultFoodId?) — `1:N IngredientAlias`
- `Recipe` (schema.org-aligned; `nutritionPerServing` **computed**, never copied) —
  `1:N RecipeIngredient`
- `RecipeIngredient` (rawText, quantity, unit, ingredientId, foodId, note)
- `DietType` / `DietRule` (diets-as-data; [ADR-0007](DECISIONS.md#adr-0007-diets-as-data-not-code))

**User-owned (EF query filter on `UserId`):**
- `User` (id, oidcSubject) — identity lives in Entra; `User` holds the local mirror + id
- `Profile` (userId, **[Pii]** sex, **[Pii]** birthDate, **[Pii]** height_cm,
  **[Pii]** weight_kg, **[Pii]** bodyFatPct?, activityLevel, goal, macroStrategy,
  allergens[], dislikes[], preferredDiets[])
- `Target` (derived, cached: userId, kcal, protein_g, fat_g, carb_g, computedAt, formula)
- `DiaryEntry` (userId, date, mealSlot, sequence, foodId, portionId, quantity,
  **snapshot {kcal,p,f,c}** denormalized at log time — [ADR-0006](DECISIONS.md#adr-0006-log-time-nutrition-snapshot))
- `MealPlan` (userId, intent jsonb, horizonDays, status) — `1:N PlanSlot`
- `PlanSlot` (planId, day, mealSlot, recipeId, servings)
- `ShoppingList` (planId, generatedAt) — `1:N ShoppingItem`
- `ShoppingItem` (listId, ingredientId, totalQuantity, unit, aisleCategory, pantryCovered)
- `PantryItem` (userId, ingredientId, quantity, unit)

**Migrations strategy:** EF Core migrations per `DbContext`, applied at startup in dev
(Aspire) and as a Container Apps job step in prod. Import upserts by natural key, never
delete-and-reload. The `Domain.NutritionTargets` math is pure (no entity) — its golden
test is the worked example from [tdee-and-macros.md](docs/algorithms/tdee-and-macros.md).

**PII:** the `[Pii]` attribute on `Profile` fields (and `User.email` mirror) drives the
GDPR export bundle and the deletion purge; audit records redact `[Pii]` values.

---

## API surface (concrete)

Full surface in [api-design.md](docs/architecture/api-design.md). Conventions, made
guardrail-compliant:

- **Grouping**: by bounded context under `/api/v1/...` (foods, ingredients, profile,
  targets, diary, recipes, pantry, shopping-lists, diet-plans, me).
- **Versioning**: URL segment (`/api/v1`).
- **Errors**: Problem Details (RFC 9457 `application/problem+json`) on every error path,
  including the `infeasible` diet-plan response (honest reason + suggested relaxations).
- **Writes**: `Idempotency-Key` header honored on all mutations; 24h replay window.
- **Async flagship**: `POST /api/v1/diet-plans` → `202 {planId, status:"generating"}`;
  poll `GET /api/v1/diet-plans/{id}` until `ready|infeasible`; `accept`; then
  `POST /diet-plans/{id}/shopping-list` closes the loop into Recipes
  ([ADR-0008](DECISIONS.md#adr-0008-async-plan-generation-via-202--poll--outbox-job)).
- **Rate limits**: per-user partition; tight on `/diet-plans`, `/diary/parse`,
  `/recipes/import` (LLM-backed); generous on `/foods/search`.
- **Compliance**: `GET /api/v1/me/export`, `DELETE /api/v1/me` (per-user GDPR delete).
- **Admin**: `/internal/import/*` behind `AdminOnly` + network restriction; never called
  by the SPA.

---

## MAF agents

The agentic surface uses **Microsoft Agent Framework over MEAI `IChatClient`**, in
`NutriForge.Infrastructure.Ai`, provider-swappable. The hard rule (the AI boundary):
**every LLM call uses structured output bound to a C# type; the LLM never computes a
number, enforces an allergen, or skips VERIFY** ([ADR-0004](DECISIONS.md#adr-0004-hybrid-generate-and-check-the-llm-never-owns-a-number)).

Two are true MAF **agents** (tool-using, possibly multi-turn); the rest are single-shot
structured-output calls via `IChatClient`:

- **MealSelectAgent** (DietGen SELECT) — **purpose**: compose a plan from a pre-filtered
  candidate pool, optimizing soft objectives (variety, cuisine balance, no-repeat,
  prep-batching). **Tools**: `QueryCandidatePool(intent) → CandidateRecipe[]` (the agent
  sees only real, allergen-safe rows — it cannot invent a "0-calorie pizza").
  **Output (structured)**: `PlanSlot[]` (recipe ids + servings) — type-impossible to
  return a calorie number. **Memory**: stateless per generation; the request is the context.
- **NutritionAssistant** (the always-present chatbot slide-over, per the family
  convention) — **purpose**: conversational help that *invokes domain actions*, never
  fabricates numbers. **Tools**: `SearchFoods`, `LogDiaryEntry`, `GetTargets`,
  `StartDietPlan`, `ExplainPlan` — all routed through the same Application handlers as
  the REST API, so deterministic code still owns every number. **System prompt**: scoped
  helper; defers all arithmetic to tools. **Conversation persistence**: conversations
  stored per user (Postgres) so the panel resumes across sessions.

Single-shot structured-output calls (not multi-turn agents, but on the same AI boundary):

- **DietIntentParser** (PARSE) — free text → typed `DietIntent`; cached by normalized desire.
- **NlDiaryParser** — "2 eggs and toast" → candidate `DiaryEntry[]` for user confirm;
  same parse layer as PARSE, built once.
- **IngredientParser** (Recipes) — raw line → structured `RecipeIngredient`; parse-once, cached.
- **PlanExplainer** (EXPLAIN) — narrate the **already-verified** plan; reports facts, never generates them.

All calls go through Polly; all are traced as part of the DietGen span.

---

## SPA architecture

`NutriForge.Web` — Vite + React + TypeScript.

- **Routing**: React Router; route-per-pillar (`/diary`, `/recipes`, `/plan`, `/profile`)
  plus `/admin/import` gated by the `admin` role.
- **State**: **TanStack Query** for all server state — it is the right tool for the async
  plan-generation flow (`POST` → `202` → poll `GET` until `ready`, with caching/retry),
  and it caches food search on the hot path. Local UI state via React hooks/context.
- **Components**: shadcn/ui primitives (owned/copied, not imported) + Tailwind. Shared
  **`DataTable`** (TanStack Table) for diary/recipe/shopping lists; shared **`Form`**
  (shadcn `Form` + `react-hook-form` + `zod`) for profile/recipe input; **Recharts** for
  the calorie ring + macro breakdown + weekly trend. The **chatbot is a slide-over panel**
  present on every route (NutritionAssistant).
- **Chrome**: consistent shell — sidebar nav + top bar with user menu. (No tenant switch
  at v1 — single B2C user; see [ADR-0001](DECISIONS.md#adr-0001-the-user-is-the-tenant-boundary).)
- **Feature folders**: one per bounded context (`features/food`, `features/tracking`,
  `features/recipes`, `features/dietgen`), each with its queries, components, and zod schemas.
- **Validation**: zod schemas shared in shape with the backend FluentValidation rules.

---

## Diagrams checked into the repo

- [`docs/diagrams/c1-context.puml`](docs/diagrams/c1-context.puml) — system + actors + external systems
- [`docs/diagrams/c2-containers.puml`](docs/diagrams/c2-containers.puml) — Aspire resource topology
- [`docs/diagrams/c3-components-api.puml`](docs/diagrams/c3-components-api.puml) — endpoint groups → handlers → infra
- [`docs/diagrams/c3-components-dietgen.puml`](docs/diagrams/c3-components-dietgen.puml) — the PARSE→…→EXPLAIN pipeline

---

## Traceability

| Solution module | Epic (ROADMAP) |
|---|---|
| `ServiceDefaults`, `Infrastructure`, `Application`, `Application.Food`, `Domain` (NutritionTargets), `Infrastructure.UsdaFdc`, `ImportWorker` | Phase 0 — Foundations |
| `Application.Tracking`, `Web` (diary slice) | Phase 1 — Calorie tracking |
| `Infrastructure.OpenFoodFacts`, `Infrastructure.Ai` (NlDiaryParser) | Phase 2 — Barcode + NL |
| `Application.Recipes` | Phase 3 — Recipes & batch cooking |
| `Application.DietGen`, `Infrastructure.Ai` (MealSelectAgent, parsers, explainer) | Phase 4 — Diet generation |
| pgvector enablement, cook schedule | Phase 5 — Polish |

Each ADR cites the `ARCH.md` section it affects (see [`DECISIONS.md`](DECISIONS.md)).
