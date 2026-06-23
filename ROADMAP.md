# NutriForge Roadmap

A phased build plan. The ordering is deliberate: **build the shared food core first, prove the calorie-tracking loop, then batch cooking, then the flagship diet generator last** — because diet generation depends on everything beneath it (recipes with computed nutrition, the targets math, the food store).

Don't build the LLM pillar first. It's the most impressive demo and the most dependent on solid foundations.

---

## Phase 0 — Foundations (the unglamorous, load-bearing part)

**Goal:** an Aspire app with a populated, searchable food store.

- [ ] Aspire AppHost wiring API + PostgreSQL + Redis.
- [ ] `Food` bounded context: `Food`, `NutrientProfile`, `Portion`, `Ingredient` entities + EF Core migrations.
- [ ] **USDA FoodData Central import pipeline** (all 4 datasets → normalized per-100g vectors + portions). Free, authoritative seed.
- [ ] Food search endpoint (Postgres FTS + Redis cache), ranked by `verificationStatus`.
- [ ] `NutritionTargets` service (BMR→TDEE→target→macros) with the worked-example golden test.

**Exit criteria:** can search "banana", get correct nutrition + portions; can compute a target tuple from a profile.

---

## Phase 1 — Calorie tracking (MVP, end-to-end vertical slice)

**Goal:** a usable food diary. This is the first thing a user touches.

- [ ] Auth (Identity + JWT) + per-user isolation.
- [ ] `Profile` + derived `Target` (consumes Phase 0 math).
- [ ] `DiaryEntry` CRUD with **log-time nutrition snapshot**.
- [ ] Day rollup vs. target; range/trend summary.
- [ ] React SPA: search → log → daily ring + macro breakdown (Recharts).

**Exit criteria:** a user can log a full day by search-and-select and see remaining calories/macros. **This is the demoable MVP.**

---

## Phase 2 — Barcode + NL logging (reduce friction)

- [ ] Open Food Facts import (UPC-indexed) → `GET /foods/barcode/{gtin}`.
- [ ] Barcode scan in the SPA (camera → UPC → food).
- [ ] **NL parse endpoint** (`/diary/parse`, LLM structured output) — *first use of MAF/MEAI*, deliberately on the low-stakes logging path before the high-stakes plan path.

**Exit criteria:** log by scanning a package; log "2 eggs and toast" with confirmation.

---

## Phase 3 — Recipes & batch cooking

**Goal:** recipes with trustworthy computed nutrition, and a real shopping list.

- [ ] `Recipe` + `RecipeIngredient` (schema.org-aligned) + EF migrations.
- [ ] **Ingredient parser** (LLM structured output, parse-once cache) → structured lines.
- [ ] Canonical `Ingredient` + alias resolution; unit/density conversion.
- [ ] **Computed per-serving nutrition** (sum ingredients → /yield).
- [ ] Recipe import from URL (schema.org/Recipe) + manual create.
- [ ] Scaling endpoint.
- [ ] Pantry + **shopping-list aggregation** (expand→parse→consolidate→subtract→categorize).
- [ ] SPA: recipe browser, scale control, aisle-grouped list.

**Exit criteria:** pick several recipes → one consolidated, aisle-grouped shopping list with pantry subtraction; recipe macros match a hand calculation.

---

## Phase 4 — Diet generation (the flagship)

**Goal:** desire → verified meal plan. Depends on Phases 0, 1, 3.

- [ ] `DietType` + `DietRule` seed data (vegan, vegetarian, keto, paleo, Mediterranean, gluten-free, halal, kosher…).
- [ ] **PARSE** (LLM → `DietIntent`, structured output; merges profile + desire constraints).
- [ ] **FILTER** (deterministic; allergen-safe candidate pool).
- [ ] **SELECT** (MAF agent over candidate pool → plan slots; variety/batching nudges).
- [ ] **VERIFY** (deterministic nutrition compute vs. target; allergen re-check).
- [ ] **REPAIR** (OR-Tools LP portion tuning; bounded re-select loop; honest infeasible response).
- [ ] **EXPLAIN** (LLM narration of verified plan).
- [ ] Async API (`202` + poll); `accept`; **`/diet-plans/{id}/shopping-list`** (closes the loop into Phase 3).
- [ ] SPA: desire input → generating state → plan view with adherence + "make shopping list".
- [ ] OpenTelemetry tracing of the full parse→verify→repair run.

**Exit criteria:** "high-protein vegan ~2000 kcal, no mushrooms" → a 7-day plan within ~5% of calories, all-vegan, mushroom-free, one click to a shopping list.

---

## Phase 5 — Polish & depth (post-MVP)

- [ ] Batch **cook schedule** (greedy appliance/temperature grouping, shelf-life aware).
- [ ] Commercial source enrichment (Nutritionix/FatSecret) for US branded/restaurant gaps — import-time only.
- [ ] pgvector fuzzy ingredient matching.
- [ ] Adherence feedback loop (actual logged intake → adjust next plan).
- [ ] Plan variety/history, favorites, weekly auto-regenerate.

---

## Critical-path dependency graph

```
Phase 0 (food core + targets)
   ├──▶ Phase 1 (tracking)  ──▶ Phase 2 (barcode/NL)
   └──▶ Phase 3 (recipes/batch) ──┐
                                  ▼
        Phase 1 + Phase 3 ──▶ Phase 4 (diet generation) ──▶ Phase 5 (polish)
```

**The one sequencing rule:** nothing in Phase 4 starts until Phase 3 produces recipes with *computed, trustworthy* nutrition — the diet generator is only as correct as the numbers it verifies against.

---

See [README](README.md) for the vision and [`docs/`](docs/) for the detailed research and design behind each phase.
