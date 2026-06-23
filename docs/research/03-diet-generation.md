# Research: Diet / Meal-Plan Generation

"Generate diets based on desires" is the flagship pillar and the hardest. It is fundamentally a **constrained optimization problem with a natural-language front door**: turn a fuzzy human desire into a meal plan that *provably* hits calorie and macro targets while honoring hard restrictions and soft preferences.

---

## 1. Frame the problem precisely

> Given a daily calorie target `C`, macro targets `(P, F, Cb)`, a set of hard constraints (allergens to exclude, diet type, disliked ingredients) and soft preferences (cuisines, variety, prep effort), select meals/recipes for each day such that the day's totals land within tolerance of `(C, P, F, Cb)` and all hard constraints hold.

This is the classic **Diet Problem** — originally "find the cheapest diet meeting a soldier's nutritional needs," solved with linear programming since the 1940s. Modern variants add cost, acceptability (taste), and ecological constraints. The literature splits cleanly into three solver families.

---

## 2. Three solver families

### A. Linear / integer programming (exact, classic)

Model food as a linearly additive system: choose quantities `xᵢ ≥ 0` of foods to **minimize/satisfy** an objective subject to nutrient constraints.

```
minimize    Σ cᵢ·xᵢ                    (cost, or deviation from targets)
subject to  Σ nutrientⱼ(i)·xᵢ ≥ RDAⱼ   for each nutrient j
            Σ kcal(i)·xᵢ  ∈ [C−ε, C+ε]
            xᵢ = 0  for excluded foods
```

- **Strengths:** provably optimal, fast, mature solvers (Gurobi, OR-Tools, HiGHS). The 2025 arXiv "Linear Optimization for the Perfect Meal" (Gurobi) shows fractional weights + nutrient-ratio constraints work well.
- **Weaknesses:** pure LP gives you *grams of foods*, not *appetizing meals*. It happily tells you to eat 600 g of spinach. Needs "repair" heuristics and a recipe layer to be palatable. Integer constraints (whole eggs, discrete portions) push you to MILP, which is slower.
- **Verdict for NutriForge:** the right tool for the **verification & adjustment** step — given candidate recipes, solve for portion multipliers that hit targets. Not the right tool for *creativity*.

### B. Genetic / evolutionary algorithms (flexible, approximate)

Represent a weekly plan as a "chromosome" (a vector of recipe choices); evolve a population with a fitness function that scores nutritional fit + variety + preference match; mutate/crossover toward better plans.

- **Strengths:** handles messy, non-linear objectives (variety, "don't repeat a cuisine two days running") naturally. Springer 2023 and earlier evolutionary-menu-planning work show good real-world plans.
- **Weaknesses:** no optimality guarantee, tuning-heavy, can produce slightly different plans each run (sometimes a feature).
- **Verdict for NutriForge:** strong candidate for the **selection** step (which recipes, across a week, balancing variety) — but operationally heavier than needed for v1.

### C. LLM-driven generation (2024–2025 state of the art for *desires*)

This is the answer to the "based on desires" part. Recent frameworks:

- **NutriGen** (arXiv 2502.20601): builds a personalized nutrition database, then uses prompt engineering grounded in **USDA data** to generate plans matching user-defined preferences and calorie targets. Reported error vs. target calories: **Llama 3.1 8B ≈ 1.55%, GPT-3.5 Turbo ≈ 3.68%** — i.e., an LLM grounded in a real nutrient DB lands within a few percent of the calorie goal.
- **Integrated AI Framework** (MDPI 2025): a local Ollama LLM parses "low-carb vegan breakfast" into structured filters (diet type, allergens, meal time) with **~91% classification accuracy**, then filters a structured meal DB.
- **Compound-ingredient decomposition** (MDPI Nutrients 2025): GPT-4o / Llama-3-70B / Mixtral decompose dishes into base ingredients for accurate nutritional analysis.

- **Strengths:** unmatched at the *desire → structured intent* translation and at proposing *appetizing, varied, culturally coherent* meals. Natural language in, natural language (or JSON) out.
- **Weaknesses:** LLMs **cannot be trusted to do the arithmetic.** Left alone they hallucinate calorie counts. They must be grounded in your nutrient DB and **verified** by a deterministic check.

---

## 3. The NutriForge synthesis: hybrid generate-and-check

No single family wins. NutriForge combines them in a pipeline — **LLM for intent + creativity, deterministic math for correctness:**

```
1. PARSE      LLM: free-text desire ──▶ structured DietIntent
                   { kcalTarget, macroSplit, dietType, excludeAllergens[],
                     dislikes[], cuisines[], mealsPerDay, prepEffort, variety }
                   (calorie/macro targets may also come from the profile — see tdee-and-macros.md)

2. FILTER     Deterministic: query recipe store for candidates satisfying ALL hard
                   constraints (dietType, allergen exclusion, dislikes). Pure SQL/predicate.

3. SELECT     LLM (grounded): from the candidate pool, compose a day/week of meals
                   optimizing soft preferences (variety, cuisine, effort). Returns recipe IDs
                   + proposed servings — NOT nutrition numbers.

4. VERIFY     Deterministic: compute the plan's real nutrition from the DB. If within
                   tolerance of targets → accept. Else compute the gap.

5. REPAIR     Deterministic (LP) OR loop: adjust portion multipliers via linear program to
                   hit targets; if infeasible, return gap to step 3 and re-select (bounded retries).

6. EXPLAIN    LLM: narrate the plan back to the user in their terms.
```

This is the key architectural idea of the whole product: **the LLM never owns a number that matters.** It proposes; deterministic code disposes. NutriGen's low error rates come precisely from grounding generation in a real nutrient DB and checking against the target — NutriForge formalizes that as steps 4–5.

---

## 4. Inputs the generator needs

| Input | Source | Hard or soft? |
|---|---|---|
| Calorie target | profile (TDEE × goal) or explicit in desire | hard (± tolerance) |
| Macro split | profile or desire | hard (± tolerance) |
| Diet type (vegan, keto, …) | parsed from desire | **hard** |
| Allergen exclusions | profile + desire | **hard (safety-critical)** |
| Disliked ingredients | profile + desire | hard (exclude) |
| Cuisine / flavor wants | desire | soft (optimize) |
| Variety / no-repeat | policy | soft |
| Prep effort / time | desire | soft → feeds batch cooking |
| Budget / ecological | optional | soft |

Allergen exclusion is **safety-critical** — it must be enforced deterministically at the FILTER step and re-checked at VERIFY, never left to the LLM. Treat it like a security boundary.

---

## 5. Diet types as data, not code

Diet types (vegan, vegetarian, keto, paleo, Mediterranean, Whole30, gluten-free, halal, kosher, …) are best modeled as **rule sets** in a mapping table, not branches in code:

```
DietType "Vegan":
    excludeCategories: [meat, poultry, fish, dairy, egg, honey]
DietType "Keto":
    macroConstraint: { carbPercent: { max: 10 } }
DietType "Halal":
    excludeIngredients: [pork, alcohol, ...non-halal-gelatin...]
    requireCertification: optional
```

A recipe satisfies a diet if it violates none of the diet's rules. schema.org's `suitableForDiet` enumeration (`VeganDiet`, `LowCarbDiet`, `HalalDiet`, …) gives a ready vocabulary. Storing diets as data means adding a new diet is a row, not a deploy.

---

## 6. What NutriForge will do differently

1. **Hybrid pipeline** — LLM owns intent + creativity, deterministic code owns every number and every safety constraint.
2. **Grounded generation** — the LLM only ever picks from recipes that already exist in the store with verified nutrition; it cannot invent a 0-calorie pizza.
3. **Allergens as a hard, double-checked boundary.**
4. **Diets as data** (rule sets), so the catalog grows without code changes.
5. **Plan output is directly consumable by batch cooking** — a generated plan *is* a set of recipe instances with servings, which is exactly the input the shopping-list aggregator wants.

---

## Key sources

- The Diet Problem & LP reviews (PMC review of LP for diet optimization; arXiv 2501.04143 Gurobi "Perfect Meal").
- Evolutionary menu planning (Springer Soft Computing 2023; earlier evolutionary-algorithm menu work).
- LLM meal planning: **NutriGen** (arXiv 2502.20601), Integrated AI Framework (MDPI Applied Sciences 2025), compound-ingredient decomposition (MDPI Nutrients 2025).
- schema.org `suitableForDiet`.

Full URLs in [`/SOURCES.md`](../../SOURCES.md).
