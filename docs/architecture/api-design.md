# Architecture: API Design

The REST surface, organized by bounded context. JSON over HTTPS, JWT auth, all `/api/v1`. Endpoints mirror the [data model](data-model.md) and feed the [React front end](tech-stack.md).

---

## Conventions

- **Auth:** `Authorization: Bearer <jwt>`; every resource is scoped to the authenticated user except the shared food/recipe catalog (read-only public-ish).
- **Errors:** RFC 9457 `application/problem+json`.
- **Pagination:** cursor-based (`?cursor=&limit=`) for search.
- **Idempotency:** mutating long-running ops (plan generation, import) accept `Idempotency-Key`.

---

## Food core

```
GET    /api/v1/foods/search?q=banana&limit=20      → ranked by verificationStatus + popularity
GET    /api/v1/foods/{id}                           → food + nutrientProfile + portions
GET    /api/v1/foods/barcode/{gtin}                 → food by UPC (Open Food Facts / Branded)
POST   /api/v1/foods                                → create user-submitted food (verificationStatus=user-submitted)
GET    /api/v1/ingredients/{id}                     → canonical ingredient + aliases
```

Search is the #1 hot path (~86% of logging) — Redis-cached, Postgres FTS fallback.

---

## Tracking (calorie diary)

```
GET    /api/v1/profile                              → profile + derived Target
PUT    /api/v1/profile                              → update stats/goal → recompute Target
GET    /api/v1/targets                              → { kcal, protein, fat, carb, formula }

GET    /api/v1/diary?date=2026-06-22                → day's entries + rollup vs. target
POST   /api/v1/diary                                → log entry { foodId, portionId, quantity, mealSlot, date }
PATCH  /api/v1/diary/{entryId}                      → edit quantity/portion
DELETE /api/v1/diary/{entryId}
GET    /api/v1/diary/summary?from=&to=              → trend (kcal/macros over range)
```

`POST /diary` snapshots computed macros onto the entry (see data-model §5).

---

## Recipes & batch cooking

```
GET    /api/v1/recipes?diet=vegan&maxTime=20&q=     → filtered recipe search
GET    /api/v1/recipes/{id}                         → recipe + parsed ingredients + computed nutrition
POST   /api/v1/recipes                              → create recipe (ingredients auto-parsed + nutrition computed)
POST   /api/v1/recipes/import                       → import from URL (schema.org/Recipe) or raw text
POST   /api/v1/recipes/{id}/scale?servings=6        → scaled ingredient quantities + nutrition

GET    /api/v1/pantry                               → user's pantry items
PUT    /api/v1/pantry                               → upsert pantry stock

POST   /api/v1/shopping-lists                       → from { planId } OR { recipeIds[] + servings }
GET    /api/v1/shopping-lists/{id}                  → consolidated, aisle-grouped, pantry-subtracted
```

`POST /shopping-lists` runs expand → parse → consolidate → subtract pantry → categorize (batch-cooking research §4).

---

## Diet generation

The flagship endpoint. Generation is async (LLM + verify + repair loop can take seconds) — return a job, poll for the plan.

```
POST   /api/v1/diet-plans                           → body: { desire: "high-protein vegan ~2000kcal, no mushrooms" }
                                                       OR structured DietIntent
                                                    → 202 Accepted { planId, status: "generating" }
GET    /api/v1/diet-plans/{id}                       → plan (status: generating|ready|infeasible)
                                                       when ready: slots + per-day macros + adherence to target
                                                       + LLM explanation
POST   /api/v1/diet-plans/{id}/regenerate            → re-run with tweaks (e.g. relax a constraint)
POST   /api/v1/diet-plans/{id}/accept                → promote to active plan
DELETE /api/v1/diet-plans/{id}

# convenience: turn an accepted plan straight into a shopping list
POST   /api/v1/diet-plans/{id}/shopping-list         → 201 → ShoppingList
```

**Response on `infeasible`** carries the honest reason ("not enough vegan recipes under 20 min for a week") + suggested relaxations — never a padded, constraint-violating plan. Mirrors the [algorithm's](../algorithms/meal-plan-generation.md) FILTER/REPAIR failure modes.

---

## NL food logging (bridges tracking ↔ diet gen)

```
POST   /api/v1/diary/parse                          → body: { text: "2 eggs and a slice of toast" }
                                                    → candidate entries [{ food, qty, confidence }] for user confirm
```

Same parse layer as `diet-plans` intent parsing — built once.

---

## Admin / import (not user-facing)

```
POST   /internal/import/usda      → trigger USDA FDC sync (background)
POST   /internal/import/off       → trigger Open Food Facts sync
GET    /internal/import/status    → last run, counts, errors
```

Behind internal auth; runs the offline import pipeline ([nutrition-data-sources](../research/04-nutrition-data-sources.md) §3). Never invoked by the SPA.

---

## Example: generate-then-shop flow

```
POST /api/v1/diet-plans { desire: "..." }     → 202 { planId: "p_1", status: "generating" }
GET  /api/v1/diet-plans/p_1                    → { status: "ready", days:[...], adherence:{kcal:"+1.4%"} }
POST /api/v1/diet-plans/p_1/accept             → 200
POST /api/v1/diet-plans/p_1/shopping-list      → 201 { id:"s_9", items:[...aisle-grouped...] }
```

Three pillars, one continuous flow — exactly the loop in the [README](../../README.md).

---

See also: [system-design](system-design.md) · [data-model](data-model.md).
