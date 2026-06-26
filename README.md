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

**The full calorie-tracking-through-diet-generation loop is implemented and building green**, including the household, AI-driven diet, diet presets, and UX-overhaul batch shipped 2026-06-26 ([PR #104](https://github.com/abrahamFerga/NutriForge/pull/104)). The full design chain
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
- **Agentic** (`src/NutriForge.Infrastructure.Ai`): the **NutritionAssistant** on **Microsoft
  Agent Framework** (`Microsoft.Agents.AI`). A `ChatClientAgent` with read tools (`search_foods`,
  `get_daily_targets`, `get_today_summary`, `get_profile`) routed through the same Application
  services as the REST API — so the LLM proposes and narrates, but **deterministic code owns every
  number** (ADR-0004). Provider-swappable (OpenAI primary), per-user persisted sessions, served at
  `POST /api/v1/assistant/chat`. The `RecipeGenAgent` generates recipe names, ingredients, and steps
  from a plan brief; `RecipeGenerationService` resolves every ingredient against the deterministic
  `NutritionReference` table so the LLM never touches a calorie number.
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

### Build, run & verify

```bash
# build everything
dotnet build NutriForge.slnx

# REQUIRED before running: the NutritionAssistant is a core capability, so the API won't
# start until the `openai-api-key` Aspire parameter has a value. Set it once (recommended):
dotnet user-secrets --project src/NutriForge.AppHost set Parameters:openai-api-key "sk-..."
#   …or export OPENAI_API_KEY=sk-...   (bridged to the parameter; PowerShell: $env:OPENAI_API_KEY="sk-...")
#   …or leave it unset and let the Aspire dashboard prompt you for the value on first run.
#   (optional) export OPENAI_CHAT_MODEL_NAME=gpt-4o-mini   # default model

# OPTIONAL: a YouTube Data API key lets recipe import auto-read the video DESCRIPTION (where the
# recipe usually is). Without it import still works via oEmbed (title/thumbnail) + pasted text, so
# it never blocks startup — it's a first-class Aspire parameter with an empty default.
#   dotnet user-secrets --project src/NutriForge.AppHost set Parameters:youtube-api-key "AIza..."
#   …or export YOUTUBE_API_KEY=AIza...   (bridged to the parameter)
#   …or set/leave it in the Aspire dashboard's Parameters; unset is fine (import just degrades).

# run the whole system (API + Postgres + Redis + SPA) — needs Docker running
./scripts/run-and-wait.ps1          # or: dotnet run --project src/NutriForge.AppHost
#   → open the Aspire dashboard URL it prints; the SPA + API resources are listed there.
#   (Integration tests are exempt from the key requirement — they assert the assistant's 503 path.)

# tests: Application 154 + Domain 41 green. Integration tests boot the real AppHost (Postgres + Redis) via Docker.
dotnet test NutriForge.slnx
```

The calorie-tracking slice is **runtime-verified**, not just build-green: the Aspire integration
tests (`tests/NutriForge.IntegrationTests`) boot the whole AppHost with real containers and drive
profile → derived target (2345 kcal) → food search → diary log → day rollup → GDPR export, plus
the auth/RBAC contract (401 anonymous, 403 user-on-admin, 200 admin). The committed
[`http/`](http/) request catalog and the run script let an agent or a human exercise every
endpoint by hand.

In dev the API uses a local **dev-auth** scheme (no live OIDC tenant required): send
`X-Debug-Subject` to act as a user, `X-Debug-Role` to pick `user`/`admin`; **no subject header ⇒
anonymous**.

**Production auth (Entra External ID, #56).** Outside Development the dev scheme is disabled — the
API **fails to start** unless a real OIDC authority is configured, so a deployment can never be
bypassed with headers. Both ends are wired: the API validates Entra JWTs (`sub`→user, `roles`→admin;
see `AuthSetup.cs`) and the SPA runs the MSAL Auth-Code + PKCE flow whenever the three `VITE_AUTH_*`
values are set (`src/NutriForge.Web/src/lib/auth.ts`). To go live:
1. Create an **Entra External ID** (CIAM) tenant + a sign-up/sign-in user flow (one-time, manual —
   Terraform can't provision the tenant). See [`infra/entra/README.md`](infra/entra/README.md).
2. Run the **`infra/entra`** Terraform module against that tenant — it creates the **API** app
   (exposes `access_as_user` + the `admin` app role) and the **SPA** app (public client, redirect
   URIs, pre-authorized on the API). It also assigns `admin` to the principals you list.
3. Feed its outputs in: `auth_authority` / `auth_audience` → `infra/environments/<env>.tfvars` (the
   subscription infra injects `Authentication__Authority` / `Authentication__Audience` into the API
   Container App), and `vite_auth_env` → the SPA build (`VITE_AUTH_*`).

Until the tenant exists the OIDC path can't be exercised end-to-end; dev-auth remains the local
fallback. (`Authentication__RoleClaim` defaults to `roles`.)

**Epic 4 (low-friction logging) is built**: barcode lookup with Open Food Facts
fetch-on-miss (`GET /api/v1/foods/barcode/{gtin}`) and natural-language entry
(`POST /api/v1/diary/parse` — "2 eggs and toast" → confirmable candidates; the LLM only parses,
deterministic code owns the numbers; needs `OPENAI_API_KEY`).

The **differentiators are built**: **Epic 5** — computed-nutrition recipes (`/api/v1/recipes`)
and the consolidated, aisle-grouped, pantry-aware shopping list (`/api/v1/shopping-lists`); and
**Epic 6** — the flagship **diet generation** (`/api/v1/diet-plans`): desire/intent → PARSE (LLM,
optional) → FILTER (deterministic, allergen-safe) → SELECT → VERIFY → **OR-Tools LP REPAIR** →
EXPLAIN, then the closed loop (accept → shopping list → adherence). The LLM proposes intent/taste/
explanation; deterministic code owns every number and every allergen gate.

### Latest product additions (2026-06-26, [PR #104](https://github.com/abrahamFerga/NutriForge/pull/104))

- **Saved household (#100)**: people the user regularly cooks for are saved once as `HouseholdMember`
  records and auto-attached to every new plan — no re-adding a partner each time. `GET/PUT /api/v1/household`.
- **Diet presets (#102)**: any plan's parameters can be saved as a `DietTemplate` and re-run in one
  tap; the saved household auto-attaches. `GET/POST/DELETE /api/v1/diet-templates`, `POST /{id}/generate`.
- **AI full-diet with fresh recipes (#101)**: `POST /api/v1/diet-plans/auto` generates a
  self-contained recipe set per plan (2–4 recipes per meal type, tagged `plan-generated`). Plan
  recipes are excluded from the recipe browser, keeping the catalog clean. `DELETE
  /api/v1/recipes/ai-generated` clears catalog AI recipes on demand. Numbers are deterministic
  throughout — the LLM writes names and steps only (ADR-0004, ADR-0016).
- **Modern UX overhaul (#103)**: one obvious primary action per screen, deep-slate + emerald/teal
  design system, route-entrance animation, gradient nav pill, per-screen `PageHeading`. Light mode
  fixed via CSS variable slate-scale inversion — one block in `index.css` covers all ~400 utility
  references. Plain language throughout; jargon ("block-size", "macro-strategy", "GTIN", "formula")
  removed from all user-facing text.
