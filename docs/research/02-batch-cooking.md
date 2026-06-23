# Research: Batch Cooking & Meal Prep

The batch-cooking pillar turns *a set of chosen recipes* into *a cook plan and a single shopping list*. This is where most meal-prep apps either shine or fall apart, and almost all of the difficulty is in **ingredient normalization**.

---

## 1. The feature set (from existing apps)

Surveying the current crop (Mealime, Paprika, Nutrola, Eat This Much, AnyList, MealBoard, MealPrepPro), the batch-cooking feature set converges on four capabilities:

| Feature | What it does | Difficulty |
|---|---|---|
| **Recipe scaling** | Adjust ingredient quantities proportionally to a target serving count; update per-serving nutrition | Easy *math*, hard *units* |
| **Ingredient consolidation** | Combine the same ingredient across recipes ("1 egg" + "2 eggs" = "3 eggs"); merge "chicken breast" appearing in 4 recipes | **Hard** — the core problem |
| **Shopping-list aggregation** | Produce one list, grouped by store section (produce / dairy / pantry) for aisle-flow | Medium |
| **Pantry / inventory** | Track what you already own; subtract it from the list; low-stock alerts | Medium (stateful) |

A signature batch-cooking feature worth copying: **Mealime's "Meal Prep Day"** — batch-cook proteins and grains once, then mix-and-match into bowls across the week. That's the actual *batch cooking* insight: don't cook 7 meals, cook 3 components × big batches and recombine.

---

## 2. Why ingredient consolidation is the whole ballgame

To merge "1 egg" + "2 eggs" you must parse a free-text ingredient line into a structured triple:

```
"2 large eggs, beaten"  ──parse──▶  { quantity: 2, unit: "each",  item: "egg",          note: "large; beaten" }
"1 cup whole milk"      ──parse──▶  { quantity: 1, unit: "cup",   item: "milk",         note: "whole" }
"200 g chicken breast"  ──parse──▶  { quantity: 200, unit: "g",   item: "chicken breast", note: null }
```

Then consolidation requires three things to line up:

1. **Canonical ingredient identity.** "chicken breast", "boneless skinless chicken breast", and "chicken breasts" must map to one canonical ingredient ID. This is an entity-resolution problem — solve it with a canonical ingredient table + alias list (and, later, embeddings for fuzzy matches).
2. **Unit compatibility & conversion.** You can only add quantities in compatible units. `cups` (volume) + `grams` (mass) requires a *density* for that ingredient. Maintain a unit graph (mass↔mass, volume↔volume always; volume↔mass only with density).
3. **Quantity arithmetic with sane display.** Sum in a base unit, then render in a human unit ("1500 g" → "1.5 kg"; "3 each" → "3").

```
consolidate(lines):
    grouped = group lines by canonicalIngredientId
    for each group:
        baseTotal = Σ toBaseUnit(line.qty, line.unit, ingredient.density)
        displayQty = fromBaseUnit(baseTotal, ingredient.preferredUnit)
    return one ShoppingItem per group
```

> **Build vs. buy the parser.** Ingredient-line parsing is a well-known NLP task (NYT's `ingredient-phrase-tagger`, the `zestful`/`ingredient-parser` libraries). For NutriForge, an LLM with a strict structured-output schema is the pragmatic v1 — the same NLP layer used elsewhere in the system. Cache parses keyed on the raw line so you parse each distinct line once, ever.

---

## 3. The recipe model

Recipes are the shared currency between batch cooking and diet generation. Anchor the model on **schema.org/Recipe** so you can ingest recipes from the web and emit SEO-friendly structured data:

| schema.org field | NutriForge use |
|---|---|
| `name`, `description`, `image` | Display |
| `recipeYield` | Base serving count — the denominator for scaling |
| `recipeIngredient[]` | Free-text lines → parsed into structured `RecipeIngredient` rows |
| `recipeInstructions[]` | Ordered steps |
| `prepTime` / `cookTime` / `totalTime` | ISO-8601 durations; used for "quick" filters & batch scheduling |
| `nutrition` (`NutritionInformation`) | Per-serving macros — **derived** from ingredients, not trusted from source |
| `recipeCuisine`, `keywords`, `suitableForDiet` | Diet filtering (`suitableForDiet` enumerates `VeganDiet`, `LowCarbDiet`, etc.) |

**Critical rule: nutrition is computed, not copied.** A recipe's stated calories are frequently wrong or absent. NutriForge computes per-serving nutrition by summing each parsed ingredient's contribution (ingredient → canonical food → nutrients × grams) and dividing by `recipeYield`. This makes recipe nutrition trustworthy and keeps it consistent with the calorie tracker. See [`docs/architecture/data-model.md`](../architecture/data-model.md).

---

## 4. Shopping-list aggregation

Once recipes are chosen and scaled to the week's plan:

```
1. expand    : for each planned recipe-instance, scale ingredients to its serving count
2. parse     : ensure every ingredient line is structured (cached)
3. consolidate: merge by canonical ingredient (§2)
4. subtract  : remove quantities the pantry already covers
5. categorize: assign each item an aisle/section (produce, dairy, meat, pantry, frozen…)
6. render    : group by section, sort within section
```

Aisle categorization maps each canonical ingredient → a store section (a static lookup on the ingredient table). "Grouped by aisle flow" is consistently called out as the feature that makes a list actually usable in a store.

---

## 5. Batch scheduling (the "when to cook" layer)

Beyond the list, batch cooking implies a **cook schedule**: which components to cook on prep day, in what order, respecting oven/stovetop contention and storage life. This is a lighter optimization than diet generation — a greedy scheduler is fine for v1:

- Group recipe steps by appliance and temperature (everything that roasts at 200°C can share the oven).
- Order by `totalTime` descending so long cooks start first.
- Flag components by fridge/freezer shelf-life so the plan doesn't tell you to make a 7-day-old salad.

Mark this **post-MVP** — the shopping list delivers most of the value; the schedule is polish.

---

## 6. What NutriForge will do differently

1. **Computed recipe nutrition** (never trust source calories) — ties batch cooking to the same nutrient core as tracking.
2. **One canonical ingredient table** with aliases + density + aisle, shared by parser, consolidator, and shopping list.
3. **Component-batch model** (Mealime's Prep Day) as a first-class concept, not an afterthought — a meal plan references *components*, and components are what get batched.
4. **Parse-once caching** keyed on raw ingredient text.

---

## Key sources

- App feature surveys: Nutrola, Mealime, Paprika, Eat This Much, AnyList, MealBoard, MealPrepPro (scaling, consolidation, aisle-grouped lists, Meal Prep Day).
- schema.org/Recipe and schema.org/NutritionInformation (recipe & nutrition modeling, `suitableForDiet`).
- Ingredient-line parsing prior art (NYT ingredient-phrase-tagger family).

Full URLs in [`/SOURCES.md`](../../SOURCES.md).
