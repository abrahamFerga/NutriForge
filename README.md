# NutriForge

> **Working name.** A nutrition platform that does three things well: **tracks calories**, **plans batch cooking**, and **generates diets from plain-language desires**. This repo currently holds the *research and system design* — the engineering groundwork before a line of product code is written.

NutriForge is scoped as a portfolio-grade, enterprise-shaped system: **.NET 10 + Aspire + Microsoft Agent Framework** on the back end, **React + Vite + shadcn/ui** on the front end. The research below is biased toward *how to build it*, not toward reviewing existing consumer apps.

---

## The three pillars

| Pillar | One-line job | Hardest part |
|---|---|---|
| **1. Calorie tracking** | Let a user log what they eat and roll it up against a daily target. | Food data quality + fast, low-friction logging (search, barcode, NLP). |
| **2. Batch cooking** | Turn a set of chosen recipes into a scaled cook plan + a consolidated, aisle-grouped shopping list. | Ingredient normalization, unit conversion, and de-duplication across recipes. |
| **3. Diet generation** | Turn a free-text desire ("high-protein vegan, ~2000 kcal, hate mushrooms") into a structured, nutritionally valid meal plan. | Hitting calorie/macro targets *and* honoring soft preferences — a constrained optimization problem with an LLM front door. |

These are not three apps. They share one food/nutrient core, one recipe model, and one user-profile model. The diet generator *produces* meal plans; batch cooking *operationalizes* them; calorie tracking *measures adherence* to them. The loop closes.

```
desire ──▶ [diet generation] ──▶ meal plan ──▶ [batch cooking] ──▶ cook plan + shopping list
                                     │                                        │
                                     ▼                                        ▼
                               [calorie tracking] ◀────────── what the user actually ate
                                     │
                                     ▼
                          adherence + adjust next plan
```

---

## How this repo is organized

```
NutriForge/
├── README.md                         ← you are here
├── ROADMAP.md                        ← phased build plan (MVP → v1 → v2)
├── SOURCES.md                        ← every external source cited, by topic
└── docs/
    ├── research/
    │   ├── 01-calorie-tracking.md        How logging & food databases actually work
    │   ├── 02-batch-cooking.md           Scaling, consolidation, shopping lists
    │   ├── 03-diet-generation.md         LP / genetic / LLM approaches compared
    │   └── 04-nutrition-data-sources.md  API shootout (USDA, Nutritionix, Edamam, …)
    ├── architecture/
    │   ├── system-design.md              Modular monolith + Aspire, bounded contexts
    │   ├── data-model.md                 Core entities + ER diagram
    │   ├── api-design.md                 REST surface
    │   └── tech-stack.md                 Why .NET 10 + Aspire + MAF + React
    └── algorithms/
        ├── tdee-and-macros.md            BMR → TDEE → calorie target → macro split
        └── meal-plan-generation.md       The hybrid generate-and-check algorithm
```

**Read order for a newcomer:** this file → `docs/research/*` → `docs/architecture/system-design.md` → `docs/algorithms/*` → `ROADMAP.md`.

---

## The one design decision that drives everything

**Own a normalized food/nutrient store; treat external nutrition APIs as importers, not as your runtime dependency.**

Every credible system here (MyFitnessPal, NutriGen, the research frameworks) ends up with a *local* nutrient database it controls, seeded from public data (USDA FoodData Central is free and authoritative) and enriched from commercial sources where needed. You do not want a meal-plan solver or a barcode scan blocking on a third-party rate limit. See [`docs/research/04-nutrition-data-sources.md`](docs/research/04-nutrition-data-sources.md) and [`docs/architecture/data-model.md`](docs/architecture/data-model.md).

---

## Status

**Calorie-tracking MVP is implemented and building green.** The full design chain
([`SPEC.md`](SPEC.md) → [`PLAN.md`](PLAN.md) → [`ARCH.md`](ARCH.md) → [`DECISIONS.md`](DECISIONS.md))
is in place, the 6-epic backlog lives on GitHub
([issues](https://github.com/abrahamFerga/NutriForge/issues) · milestones in build order),
and **Epics 1–3 (Foundations, Food & Nutrition Core, Calorie Tracking)** are built:

- **Backend** (`src/`): .NET 10 + Aspire modular monolith — OIDC/JWT auth (with a local dev scheme),
  RBAC (`OwnerOnly`/`AdminOnly`), per-user EF Core query-filter isolation, append-only audit in a
  separate database via a transactional outbox, idempotency + rate-limiting + Problem Details
  middleware, GDPR export/erasure, the verification-tiered food catalog with search, the pure
  `NutritionTargets` math, and the daily diary with log-time snapshots.
- **Frontend** (`src/NutriForge.Web`): Vite + React + TS + Tailwind + shadcn-style UI + TanStack
  Query — dashboard (calorie ring, macros, weekly trend), diary (search → log), profile, and the
  always-present NutritionAssistant slide-over.
- **Tests** (`tests/`): the TDEE golden test, the calorie safety-floor guard, diary-snapshot
  immutability, and per-user isolation — all green.

### Solution layout

```
src/
  NutriForge.AppHost/            Aspire orchestration (Postgres ×2, Redis, API, worker, SPA)
  NutriForge.ServiceDefaults/    OpenTelemetry + health checks + resilience
  NutriForge.Api/                Minimal-API endpoints grouped by bounded context
  NutriForge.Application/        Services, DTOs, validators, context ports (Food, Tracking)
  NutriForge.Domain/             Entities, value objects, NutritionTargets pure math
  NutriForge.Infrastructure/     EF Core contexts, audit/outbox, Redis cache, migrations
  NutriForge.ImportWorker/       Background host for nightly importers
  NutriForge.Web/                React SPA
tests/                           Domain / Application / Aspire integration tests
```

### Build & run

```bash
# build + unit tests
dotnet build NutriForge.slnx
dotnet test tests/NutriForge.Domain.Tests tests/NutriForge.Application.Tests

# run the whole system (API + Postgres + Redis + SPA) — needs Docker + the Aspire workload
dotnet run --project src/NutriForge.AppHost
```

In dev the API uses a local **dev-auth** scheme (no live OIDC tenant required); the SPA sends an
`X-Debug-Subject` header so you can click straight into the app. Next up off the backlog: Epic 4
(barcode + natural-language logging), then the differentiators (recipes, diet generation).
