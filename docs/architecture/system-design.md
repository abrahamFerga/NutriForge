# Architecture: System Design

How NutriForge is structured. The guiding principle: **one shared food/nutrient core, three pillars as bounded contexts, an AI layer that proposes but never decides numbers.**

---

## 1. Shape: modular monolith, Aspire-orchestrated

Start as a **modular monolith** — one deployable ASP.NET Core app with strict internal module boundaries — orchestrated locally by **.NET Aspire**. Resist microservices until a module actually needs independent scaling. The boundaries below are *logical*; they become network boundaries only if/when load demands it.

```
                         ┌─────────────────────────────────────────────┐
   React SPA  ──HTTP──▶  │  ASP.NET Core API (modular monolith)         │
 (Vite+shadcn)           │                                              │
                         │  ┌────────┐ ┌──────────┐ ┌───────────────┐  │
                         │  │  Food  │ │ Recipes  │ │  Tracking     │  │
                         │  │  core  │ │ & Batch  │ │  (diary)      │  │
                         │  └───┬────┘ └────┬─────┘ └──────┬────────┘  │
                         │      │           │              │           │
                         │  ┌───▼───────────▼──────────────▼────────┐  │
                         │  │   NutritionTargets (pure math)         │  │
                         │  └───────────────┬────────────────────────┘ │
                         │  ┌───────────────▼────────────────────────┐ │
                         │  │  DietGen (MAF agent + LP solver)        │ │
                         │  └─────────────────────────────────────────┘│
                         └──────────────┬──────────────────────────────┘
                                        │
              ┌─────────────────────────┼───────────────────────┐
              ▼                         ▼                         ▼
        PostgreSQL              Redis (cache)            LLM provider
     (food, recipes,         (search, parse cache,    (Azure OpenAI /
      diary, plans)           sessions)                Anthropic via MEAI)

   Import workers (background): USDA FDC / Open Food Facts ──▶ Food core
```

Aspire's AppHost wires the API, Postgres, Redis, and the import worker into one F5 experience, with the dashboard for traces/logs/metrics out of the box.

---

## 2. Bounded contexts

| Context | Owns | Depends on | Pillar |
|---|---|---|---|
| **Food core** | `Food`, `NutrientProfile`, `Portion`, `Ingredient` (canonical), import pipeline | — (foundational) | all |
| **Recipes & Batch** | `Recipe`, `RecipeIngredient`, ingredient parser, consolidator, shopping list, cook schedule | Food core | batch cooking |
| **Tracking** | `User`, `Profile`, `DiaryEntry`, `Day`, goals | Food core, NutritionTargets | calorie tracking |
| **NutritionTargets** | BMR/TDEE/target/macro math (pure, no I/O) | — | shared |
| **DietGen** | `DietIntent` parse, candidate filter, MAF select agent, LP repair, `MealPlan` | Food core, Recipes, NutritionTargets | diet generation |

**Dependency rule:** Food core depends on nothing; everything depends on Food core; DietGen sits on top of all of them. No cycles. Each context is a C# project (`NutriForge.Food`, `NutriForge.Recipes`, …) with a public contract and internal implementation — the monolith enforces the same discipline a service mesh would, minus the network.

---

## 3. The AI boundary (the most important rule)

DietGen is the only context that calls an LLM, and it does so through a hard contract:

- **LLM via `Microsoft.Extensions.AI` `IChatClient`**, wrapped by a **Microsoft Agent Framework** agent for the SELECT step. Provider-swappable (Azure OpenAI, Anthropic, Ollama for local dev) — see [tech-stack](tech-stack.md).
- **The LLM only ever**: parses intent (structured output), selects from a pre-filtered candidate pool, and explains the result.
- **The LLM never**: computes nutrition, enforces allergens, or returns a plan that skips VERIFY.
- Every LLM call uses **structured output** bound to a C# type, so malformed responses fail loudly instead of poisoning a plan.

This boundary is enforced in code: DietGen's `select()` takes `IReadOnlyList<CandidateRecipe>` and returns `IReadOnlyList<PlanSlot>` (ids + servings) — it is *type-impossible* for the agent to hand back a calorie number. Full flow in [meal-plan-generation](../algorithms/meal-plan-generation.md).

---

## 4. Data flows end-to-end

**Logging a food (tracking):**
```
search "banana" → Food core (Postgres FTS + Redis cache, ranked by verificationStatus)
              → pick portion → POST diary entry → Day rollup vs. target (NutritionTargets)
```

**Generating a diet:**
```
desire text → DietGen.parse (LLM) → DietIntent
           → Food/Recipes filter (deterministic) → candidates
           → DietGen.select (MAF agent) → plan slots
           → VERIFY (compute from Food core) → REPAIR (LP) if needed
           → MealPlan persisted
```

**Batch cooking a plan:**
```
MealPlan → expand recipes to servings → parse+consolidate ingredients
        → subtract pantry → categorize by aisle → ShoppingList
        → (optional) cook schedule
```

Note the plan→shopping handoff needs no transformation: a `MealPlan` *is* recipe-instances-with-servings, which is exactly the aggregator's input.

---

## 5. Cross-cutting concerns

| Concern | Approach |
|---|---|
| **Caching** | Redis for food search results, ingredient-parse results (keyed on raw line), and LLM intent parses (keyed on normalized desire). |
| **Background work** | Import pipeline + nightly nutrient re-sync as hosted services / Aspire-managed workers. Never on a user request path. |
| **Observability** | OpenTelemetry traces/metrics/logs via Aspire dashboard; trace each DietGen run end-to-end (parse→verify→repair) so you can see where plans fail. |
| **Validation** | FluentValidation on all inputs; allergen exclusion treated as a validated invariant. |
| **Idempotency** | Imports upsert by `(provider, providerId)`; plan generation is a command with a request id. |
| **Auth** | ASP.NET Core Identity / JWT for the SPA; per-user data isolation on every query. |

---

## 6. Why a monolith first (the trade-off, stated)

The three pillars are tightly coupled through the food/nutrient core and share transactions (a diary entry references a food; a plan references recipes that reference foods). Splitting them into services early would mean distributed transactions and chatty cross-service nutrient lookups for zero scaling benefit at portfolio/early-product scale. The bounded-context discipline keeps the *option* to split later cheap. **Modular monolith now, extract a service only when a real bottleneck names itself** (most likely candidate: the import pipeline or DietGen under heavy LLM load).

---

See also: [data-model](data-model.md) · [api-design](api-design.md) · [tech-stack](tech-stack.md).
