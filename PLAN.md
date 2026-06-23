# NutriForge — Plan

> **Phase 3 artifact (plan-system).** Translates [`SPEC.md`](SPEC.md) into a build order.
> Produced after [`ARCH.md`](ARCH.md) (the workflow was entered mid-stream), so this plan is
> written to stay consistent with the already-decided architecture and with the phased
> [`ROADMAP.md`](ROADMAP.md). No `workflow.json` is present; the "connectors" are the
> nutrition-data importers and the LLM provider. Build order is authoritative for
> [`build-system`](build-system).

## Epics (in build order)

1. **Foundations** — the cross-cutting enterprise scaffold, pulled from the guardrails; delivers
   no product capability on its own. Auth (OIDC), per-user data isolation (the "tenant" is the
   user — ADR-0001), observability + append-only audit, RBAC scaffold, idempotency / rate-limit /
   outbox / Problem Details middleware, secrets via the cloud store, the SPA dashboard shell +
   always-present chatbot panel, and the importer/connector registry. Depends on: nothing.
   *(The Aspire backbone + ServiceDefaults are already scaffolded.)*
2. **Food & Nutrition Core** — the shared kernel every pillar reads. Capabilities (from SPEC):
   *Trustworthy food search & logging* (the verification-tiered catalog + portions + search; the
   diary half lands in Calorie Tracking). Also builds the pure `NutritionTargets` math (consumed
   by epics 3 and 6) and the offline import pipeline. Depends on: Foundations.
3. **Calorie Tracking** — the demoable MVP. Capabilities (from SPEC): *Personalized targets*
   (profile → derived target with safety floor) and *Daily diary & progress* (log with log-time
   snapshot, day rollup vs. target, trend). Depends on: Food & Nutrition Core.
4. **Low-friction Logging** — Capabilities (from SPEC): *Low-friction logging* (barcode lookup +
   natural-language entry → confirmable diary entries). Depends on: Calorie Tracking, Food Core.
5. **Recipes & Batch Cooking** *(differentiator)* — Capabilities (from SPEC): *Computed-nutrition
   batch cooking* (schema.org-aligned recipes with computed per-serving nutrition; ingredient
   parse → canonical resolution → consolidation → pantry subtraction → aisle-grouped shopping
   list). Depends on: Food & Nutrition Core. **Hard rule:** must precede epic 6 — diet generation
   verifies against recipes with computed, trustworthy nutrition.
6. **Diet Generation + Closed Loop** *(differentiator, flagship)* — Capabilities (from SPEC):
   *Desire-to-plan diet generation* (parse → filter → select → verify → repair → explain; diets as
   data; async generate/poll/accept) and *The closed loop* (accepted plan → one-click shopping
   list, and logged intake → adherence readout). Depends on: Food Core, Calorie Tracking (targets),
   Recipes & Batch Cooking.

> Differentiators (epics 5–6) come last so they can slip without blocking the v1 tracking MVP.

## Module list

| Module (.NET project) | Bounded context | Capabilities served | Skills used to build it |
|---|---|---|---|
| `NutriForge.Application` (shared) + `NutriForge.Infrastructure` + `NutriForge.Infrastructure.Azure` + `NutriForge.ServiceDefaults` | foundations | (cross-cutting: auth, isolation, audit, idempotency, outbox, rate-limit) | dotnet-aspire-base, dotnet-architecture, rbac (inline), multi-tenant/user-isolation (inline) |
| `NutriForge.Web` (shell) | foundations | (dashboard shell + chatbot panel) | frontend-design, dashboard-portal (inline), industry-chatbot (inline) |
| `NutriForge.Domain` | shared kernel | food/recipe entities + `NutritionTargets` pure math | dotnet-architecture, entity-framework-core |
| `NutriForge.Application.Food` + `NutriForge.Infrastructure.UsdaFdc` + `NutriForge.Infrastructure.OpenFoodFacts` + `NutriForge.ImportWorker` | food core | Trustworthy food search & logging (search/catalog half) | entity-framework-core, pluggable-connectors |
| `NutriForge.Application.Tracking` | tracking | Personalized targets; Daily diary & progress | entity-framework-core, dotnet-architecture |
| `NutriForge.Infrastructure.Ai` (NL parse) | tracking ↔ dietgen (shared parse layer) | Low-friction logging (NL entry) | agent-framework-csharp |
| `NutriForge.Application.Recipes` | recipes & batch | Computed-nutrition batch cooking | entity-framework-core, agent-framework-csharp (ingredient parse) |
| `NutriForge.Application.DietGen` + `NutriForge.Infrastructure.Ai` (agents) | dietgen | Desire-to-plan generation; The closed loop | agent-framework-csharp, entity-framework-core |

> Per-context `Application.<Context>` / `Infrastructure.<Connector>` projects are created with
> their epic, not upfront. The shared backbone projects already exist.

## Data model sketch

Conceptual entities and relationships only — concrete schemas live in [`ARCH.md`](ARCH.md).

**Shared catalog (public-read, not user-isolated):**
- **Food** — name, brand?, barcode?, verification tier, provenance; `1:1` NutrientProfile, `1:N` Portion; maps to a canonical Ingredient. Audited on mutation.
- **NutrientProfile** — per-100g macro/micro vector (macros required, micros nullable).
- **Portion** — named serving → grams.
- **Ingredient** (canonical) + **IngredientAlias** — de-dup anchor with category→aisle, density, preferred unit, default food.
- **Recipe** + **RecipeIngredient** — schema.org-aligned; per-serving nutrition **computed**, never copied.
- **DietType** + **DietRule** — diets as data (exclude-category / exclude-ingredient / macro-constraint / require-tag).

**User-owned (isolated by `UserId`; mutations audited):**
- **User** — identity mirror (OIDC subject). **Profile** — sex, birth date, height, weight, body-fat, activity, goal, macro strategy, allergens, dislikes, diets. **[Pii]** on sex/birthDate/height/weight/bodyFat (+ email mirror).
- **Target** — derived (cached) calorie + macro tuple.
- **DiaryEntry** — date, meal slot, food, portion, quantity, **log-time nutrition snapshot** (immutable history).
- **MealPlan** + **PlanSlot** — intent + per-day/meal recipe selections.
- **ShoppingList** + **ShoppingItem** — consolidated, aisle-grouped, pantry-aware.
- **PantryItem** — on-hand stock, subtracted at list generation.

PII flows through audit logging and the GDPR export/delete path. Audit records are stored outside the operational DB (ADR-0011).

## RBAC model (refined)

Policy names use `<Module>.<Action>`; code references the policy, not the role. All `user`
policies are owner-scoped (the row's `UserId` must equal the caller).

| Role | Policies | Notes |
|---|---|---|
| `user` | `Profile.View`, `Profile.Edit`, `Targets.View`, `Food.Search`, `Food.Submit`, `Diary.View`, `Diary.Edit`, `Recipes.View`, `Pantry.Edit`, `ShoppingList.Create`, `ShoppingList.View`, `DietPlan.Create`, `DietPlan.View`, `DietPlan.Accept`, `DietPlan.Delete` | Owner-scoped to own data; reads the shared food/recipe catalog. Cannot run imports or set verification tiers. |
| `admin` | `Import.Run`, `Import.View`, `Food.Verify` | Operates the catalog; has **no** access to any user's diary, profile, or plans. |

## Integration surface

No inbound webhooks at v1 — every external system is consumed outbound, on the import path or
behind the AI boundary, never on the synchronous user request path.

| Connector | Direction | Purpose | Webhook routes | Per-tenant config |
|---|---|---|---|---|
| USDA FoodData Central | outbound | Seed/refresh the food catalog (all 4 datasets → per-100g vectors + portions) | none (scheduled pull) | none (global catalog) |
| Open Food Facts | outbound | UPC-indexed branded foods for barcode logging | none (scheduled pull) | none (global catalog) |
| LLM provider | outbound | Intent parse, candidate selection, narration, NL/ingredient parse (structured output, behind Polly) | none | none (global; per-user data passed in request, never stored at provider) |

## Background work

| Job | Trigger | Cadence | Outbox required? |
|---|---|---|---|
| Food catalog import (USDA + Open Food Facts) | scheduled | nightly (cron) | no — idempotent upsert by `(provider, providerId)`; inbound fetch, no external side effect |
| Diet-plan generation (parse→…→explain) | reactive (on `POST /diet-plans`) | per request, long-running, ≤3 repair loops | **yes** — dispatched via the outbox (ADR-0008) |
| Audit-log write | reactive (every domain mutation) | per mutation | **yes** — relayed to the separate audit DB via the outbox (ADR-0011) |
| Target recompute | reactive (on profile change) | per change | no — in-process, same transaction |
| Parse-cache warm (ingredient / intent) | reactive | per distinct raw line / desire | no — cache write, no external side effect |

## Open questions for design-architecture

Most technology questions are already resolved in [`ARCH.md`](ARCH.md)/[`DECISIONS.md`](DECISIONS.md)
(this plan was written after them). The genuinely still-open items are product/scope, for the
human to answer before epics 5–6:

1. **Recipe seed for v1** — diet generation's FILTER needs a candidate pool large enough to fill a
   7-day *restricted* plan (e.g. vegan, ≤20-min prep). How many recipes, and sourced how (URL
   import + manual only, or a seeded set)? Below a threshold, epic 6 returns "infeasible" by design.
2. **v1 diet-type seed set** — which of vegan/vegetarian/keto/paleo/Mediterranean/gluten-free/
   halal/kosher ship as `DietRule` rows in v1 vs. v2?
3. **Low-friction logging sequencing (epic 4)** — search-only tracking MVP first with barcode + NL
   as a fast-follow inside v1, or all three at once? (Research ranks search P0, barcode P1, NL P2.)
4. **Scale target** — concurrent users and plans/day for v1; confirms diet generation stays
   in-process (ARCH's default) rather than extracting early.
5. **LLM provider for local/dev** — confirm a local model is acceptable for dev (cost/offline) with
   the managed provider in prod, both behind the same `IChatClient` boundary.

*(Resolved already in ARCH: cloud target = Azure Container Apps; relational store + cache;
auth = OIDC Entra External ID; single region; the hybrid generate-and-check AI boundary.)*
