# Research: Calorie Tracking Systems

How a calorie-tracking system actually works under the hood, and what NutriForge should copy vs. avoid.

---

## 1. The core loop

A calorie tracker is, mechanically, a **food diary**: the user records food entries against a day, the system multiplies each entry by its per-unit nutrition, and rolls everything up against a target.

```
log entry = (food, serving size, quantity, meal slot, date)
entry nutrition = food.nutrientsPer100g × (servingGrams × quantity / 100)
day total = Σ entries for that date
remaining = dailyTarget − dayTotal
```

The intelligence is almost entirely in two places:
1. **The food database** — coverage and accuracy of the underlying nutrition data.
2. **Logging friction** — how fast a user can find the right food and record it.

### What MyFitnessPal's scale teaches us

MyFitnessPal runs a database of ~10M foods, a mix of staff-curated entries and user-submitted ones. They tier trust explicitly: "Best match" / green-checkmark entries are reviewed by registered dietitians; member-submitted entries without a checkmark are unverified. **~86% of food logs are entered via search-and-select; only ~14% are entered manually.** Barcode scanning pulls nutrition straight off the product label.

**Takeaways for NutriForge:**
- **Search-and-select is the dominant path** — invest there first. Manual entry and barcode are secondary.
- **Model a trust/verification tier on every food row.** Curated > imported-from-authoritative-source > user-submitted. Never silently blend them.
- A crowd-sourced long tail is what gets you to 10M foods, but it is also where accuracy goes to die. Gate user submissions behind a `verificationStatus` field.

---

## 2. The food data model (observed)

From the MyFitnessPal food-diary research datasets, the logging schema is essentially:

| Concept | Fields |
|---|---|
| **User** | demographics, activity data, goal weight, weekly weight-loss goal, daily net-calorie goal |
| **Meal** | `meal_id`, `user_id`, `date`, `meal_sequence` (1 = first meal of day), `food_ids[]` |
| **Food entry** | calories, macronutrients, vitamins, minerals — per logged item |

Three static goals per user: (1) goal weight, (2) weekly rate of loss, (3) daily net-calorie goal. "Net" matters — exercise calories are added back in some models. NutriForge should decide explicitly whether it uses **net** (TDEE − food + exercise) or **gross** (food vs. fixed target) accounting and stick to one; mixing them is the #1 source of user confusion in these apps.

See [`docs/architecture/data-model.md`](../architecture/data-model.md) for the NutriForge entity design that generalizes this.

---

## 3. The four ways a user logs food

| Method | Share of logs (MFP) | What you build | NutriForge priority |
|---|---|---|---|
| **Search + select** | ~86% | Fast full-text search over your food store with ranking by verification tier + popularity | **P0** |
| **Barcode scan** | (part of search) | UPC lookup → food row. Needs a UPC-indexed source (Open Food Facts free; Nutritionix/FatSecret commercial) | **P1** |
| **Manual entry** | ~14% | A form that creates a user-submitted food row | **P1** |
| **Natural-language ("2 eggs and toast")** | emerging | LLM parses free text → list of (food, qty) candidates → user confirms | **P2** |

The NLP path is the bridge to the diet-generation pillar — the same parsing layer that reads "2 eggs and toast" reads "I want a high-protein vegan plan." Build the parser once. See [`docs/algorithms/meal-plan-generation.md`](../algorithms/meal-plan-generation.md).

---

## 4. Serving sizes & units — the quiet hard problem

Nutrition is stored per 100 g (or per 100 ml) for sanity, but users think in *servings* ("1 slice", "1 cup", "1 medium banana"). Each food therefore needs a set of **portion definitions** mapping a named portion → grams:

```
Food: "Banana, raw"
  nutrientsPer100g: { kcal: 89, protein: 1.1, carb: 22.8, fat: 0.3, ... }
  portions:
    - { name: "1 medium (7-8 in)", grams: 118 }
    - { name: "1 cup, mashed",     grams: 225 }
    - { name: "100 g",             grams: 100 }
```

USDA FoodData Central ships these portion/`foodPortion` records; preserve them on import. Without portions, every log forces the user to weigh food in grams — the single biggest adoption killer.

---

## 5. Targets come from the user profile

The daily calorie target is not entered by hand in a good system — it's *derived* from the user's stats and goal, then optionally overridden. That derivation (BMR → TDEE → goal-adjusted target → macro split) is its own subsystem and is documented separately in [`docs/algorithms/tdee-and-macros.md`](../algorithms/tdee-and-macros.md). The tracker just consumes the numbers it produces.

---

## 6. What NutriForge will do differently

1. **Local-first nutrient store, seeded from USDA.** No runtime dependency on a rate-limited third party for the read path. (Rationale in [`04-nutrition-data-sources.md`](04-nutrition-data-sources.md).)
2. **Explicit verification tiers** instead of MFP's noisy free-for-all.
3. **One NLP entry point** shared with diet generation.
4. **Net-vs-gross accounting decided up front** (NutriForge uses gross: food logged against a fixed daily target; exercise is informational, not added back — simpler and harder to game).

---

## Key sources

- MyFitnessPal — *How the food database works* / *Where MFP gets its data* (database scale, verification tiers, barcode).
- MyFitnessPal Food Diary research datasets (SMU LARC; ResearchGate Food Logging Dataset) — meal/entry schema, `meal_sequence`, 86/14 logging split.
- USDA FoodData Central — `foodPortion` records for serving sizes.

Full URLs in [`/SOURCES.md`](../../SOURCES.md).
