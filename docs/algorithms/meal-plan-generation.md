# Algorithm: Meal-Plan Generation (Hybrid Generate-and-Check)

The concrete algorithm behind the "generate diets based on desires" pillar. It operationalizes the synthesis from [the diet-generation research](../research/03-diet-generation.md): **the LLM owns intent and creativity; deterministic code owns every number and every safety constraint.**

---

## Pipeline overview

```
desire (free text) ──▶ [1 PARSE] ──▶ DietIntent
                                         │
                       profile targets ──┤  (from tdee-and-macros.md)
                                         ▼
                                   [2 FILTER]  ──▶ candidate recipe pool (hard constraints)
                                         ▼
                                   [3 SELECT]  ──▶ proposed plan (recipe IDs + servings)
                                         ▼
                                   [4 VERIFY]  ──▶ computed nutrition vs. target
                                    accept? ──── yes ──▶ [6 EXPLAIN] ──▶ MealPlan
                                         │ no
                                         ▼
                                   [5 REPAIR]  ──▶ adjust portions (LP) or re-select (bounded loop)
```

---

## Step 1 — PARSE (LLM, structured output)

Input: `"I want a high-protein vegan plan around 2000 calories, I hate mushrooms and don't have much time to cook."`

The LLM is constrained to emit a typed `DietIntent`. With MAF / `Microsoft.Extensions.AI`, use **structured output** (a C# response type) so the model must return valid JSON:

```jsonc
{
  "kcalTarget": 2000,           // may be null → fall back to profile target
  "macroEmphasis": "high-protein",
  "dietType": "vegan",
  "excludeAllergens": [],
  "dislikes": ["mushroom"],
  "cuisines": [],
  "mealsPerDay": 3,
  "prepEffort": "low",          // → feeds batch cooking
  "variety": "normal",
  "horizonDays": 7
}
```

If `kcalTarget`/macros are absent, fill from the `NutritionTargets` service ([tdee-and-macros](tdee-and-macros.md)). Merge profile-level allergens/dislikes (persistent) with desire-level ones (this request).

Reported accuracy for this NL→structured-filter task is ~91% (MDPI 2025) — good, but **not safe enough for allergens.** Hence allergens are enforced again deterministically in steps 2 and 4.

---

## Step 2 — FILTER (deterministic, hard constraints)

Pure predicate query against the recipe store. **No LLM.** A recipe enters the candidate pool only if it passes *all*:

```
candidates = recipes where
    satisfiesDiet(recipe, intent.dietType)          # diet rule-set, §5 of diet-generation.md
  AND none(recipe.ingredients ∈ intent.excludeAllergens)   # SAFETY — hard
  AND none(recipe.ingredients ∈ intent.dislikes)
  AND (intent.maxPrepMinutes is null OR recipe.totalTime ≤ intent.maxPrepMinutes)
  AND recipe.nutrition is computed (not null)        # only verified-nutrition recipes
```

If the pool is too small to build a plan (e.g. < mealsPerDay × horizon / repeat-policy), return a **clear, honest failure** ("not enough vegan recipes under 20 min to fill a week — relax prep time or add recipes"). Never pad a plan with constraint-violating meals.

---

## Step 3 — SELECT (LLM, grounded in the candidate pool)

The LLM sees **only candidate recipes** (id, name, per-serving macros, cuisine, effort, tags) and composes the plan, optimizing the *soft* objectives: variety, cuisine balance, no-repeat policy, prep batching. It returns **recipe IDs + proposed servings only — never nutrition numbers.**

```
select(candidates, intent) ──▶
  [ { day:1, meal:"breakfast", recipeId:"r_812", servings:1 },
    { day:1, meal:"lunch",     recipeId:"r_309", servings:1.5 }, ... ]
```

Grounding the LLM in real candidates is what keeps NutriGen-style error rates low (~1.5–3.7% vs. calorie target): the model cannot invent a "0-calorie pizza" because it can only pick rows that exist.

**Variety / batching nudges** (in the prompt, scored — not hard): prefer ≤2 distinct proteins per week to enable batch cooking; avoid same recipe twice in 3 days unless `variety:"low"` (meal-prep repetition is desired).

---

## Step 4 — VERIFY (deterministic — the trust anchor)

Compute the plan's **real** nutrition from the store and compare to target. The LLM's opinion of the numbers is discarded.

```
for each day:
    dayMacros = Σ over meals: recipe.nutritionPerServing × servings
    Δkcal = dayMacros.kcal − target.kcal
    Δp, Δf, Δc likewise
accept day if  |Δkcal| ≤ kcalTol         (e.g. ±5%)
           and each macro within macroTol (e.g. ±10–15 g)
```

Re-assert the allergen exclusion here as a final gate (defense in depth). Accept → step 6. Otherwise → step 5.

---

## Step 5 — REPAIR (deterministic first, LLM loop as fallback)

Two repair tiers, cheapest first:

**5a. Portion tuning via linear program.** Hold the recipe *selection* fixed; solve for per-meal serving multipliers `mᵢ ∈ [min, max]` that minimize deviation from targets:

```
minimize   Σ_macro w_macro · |achieved_macro(m) − target_macro|
subject to mᵢ ∈ [0.5, 2.0]            # keep portions realistic
           (optional) per-meal kcal bounds
solve with OR-Tools / HiGHS (linear)  # fast, deterministic, optimal
```

This handles the common case ("plan is 150 kcal short → scale the carb side dish up 1.2×") without re-prompting the LLM. The LP literature (incl. the Gurobi "Perfect Meal" work) is exactly this: linear, additive nutrients, bounded weights.

**5b. Re-select.** If portion tuning can't close the gap within realistic bounds (e.g. the plan is fundamentally too low-protein), return the *quantified gap* to step 3 — "need ~40 g more protein, ~200 fewer kcal" — and let the LLM swap meals. **Bound the loop** (≤3 iterations) to cap latency and cost; if still infeasible, return the best-effort plan with an explicit "off-target by X" disclosure rather than silently shipping a bad plan.

---

## Step 6 — EXPLAIN (LLM)

Narrate the accepted plan in the user's language, surface the macro totals, and call out anything notable ("hit your 2000 kcal within 1.5%, protein landed at the top of your range, all vegan, no mushrooms, nothing over 20 min"). This is the only step where the LLM "talks" — and it's reporting verified facts, not generating them.

---

## Output: a plan that batch cooking consumes directly

The accepted `MealPlan` is a list of `(recipe, servings, day, meal-slot)`. That is *exactly* the input the shopping-list aggregator wants ([batch cooking research](../research/02-batch-cooking.md) §4): expand → parse → consolidate → categorize → list. The pillars compose with no glue layer — by design.

---

## Why this design (the one-paragraph defense)

Pure LP gives correct numbers but unappetizing food. Pure LLM gives appetizing food but unreliable numbers and unsafe allergen handling. The hybrid puts each where it's strong: **LLM for the fuzzy human parts (intent, taste, variety, explanation), deterministic code for the parts that must be exact and safe (filtering, nutrition math, allergen enforcement, portion optimization).** The LLM never owns a number that matters, and never owns a safety decision.

---

## Key sources

- NutriGen (arXiv 2502.20601) — grounded-generation error rates.
- Integrated AI Framework (MDPI Applied Sciences 2025) — NL→structured-filter ~91%.
- LP for diets (PMC review; arXiv 2501.04143 Gurobi) — portion optimization.
- Evolutionary menu planning (Springer 2023) — alternative SELECT strategy.

Full URLs in [`/SOURCES.md`](../../SOURCES.md).
