# Threat model — the allergen-safety gate (#60)

The allergen gate is the most safety-critical path in NutriForge: a declared allergen surfacing in a
**system-recommended** meal plan is a potential health hazard. This document states what the gate
guarantees, how it is enforced, the adversarial review that hardened it, and the residual risks.

## Asset & harm

- **Asset:** the user's declared allergens (`Profile.Allergens`, plus per-request `excludeAllergens`).
- **Harm:** a generated diet plan containing a recipe whose ingredients include a declared allergen.
- **Out of scope for "harm":** the user *manually* logging or searching an allergen-containing food. Search
  and manual logging are user-initiated choices, not system recommendations — see *Residual risks*.

## Where the gate runs (defense in depth)

The plan generator (`DietGenPipeline`) enforces allergens in **two independent places**, both driven by the
same expanded keyword set:

1. **FILTER** (`RecipeFilter.Filter`) — before any selection, every recipe whose name or any ingredient
   name contains an excluded keyword is removed from the candidate pool. The LLM SELECT agent (#36) only
   ever sees the already-filtered pool.
2. **Re-check** (`PlanVerifier.AllergensClear`) — after selection (greedy *or* LLM), the final plan is
   re-asserted against the same keyword set. A plan that trips it is rejected (`Feasible = false`),
   regardless of who chose the recipes. The LLM can never widen the pool or bypass this.

Matching is case-insensitive substring containment against the recipe name and each ingredient name.

## The adversarial finding

Substring matching on the **literal** declared word is unsafe on its own: declaring `milk` never matches
`butter`, `cheese`, `cream`, or `whey`; `nuts` never matches `almond` or `cashew`; `egg` never matches
`mayonnaise`. Derived ingredients slip straight through the gate.

### Mitigation — `AllergenOntology.Expand`

Declared allergens are expanded into the common derivative/synonym terms that signal their presence before
they reach the gate (`DietPlanService` builds `intent.ExcludeKeywords` from the expansion). Covered groups:
dairy, egg, peanut, tree nuts, soy, wheat/gluten, fish, shellfish, sesame.

**Safety bias — expansion only ever *adds* exclusions** (and always keeps the literal declaration). It can
over-exclude (declaring `coconut` also drops tree nuts; `egg` drops `eggplant`-free dishes are unaffected
but `aioli` is dropped) — over-exclusion costs plan variety, never safety. It can never let a *covered*
derivative through.

Allergens are expanded; **dislikes are not** — dislikes are a preference, excluded literally only.

## What is proven (tests)

`AllergenSafetyTests` locks the gate in:

- **Targeted derivatives** — 14 (declared → derivative) pairs (`milk→butter`, `soy→tofu`, `fish→anchovy`,
  …), each asserted to (a) *not* be caught by the bare substring gate and (b) be removed by the expanded
  FILTER and rejected by the re-check.
- **Literal preservation** — an allergen outside the ontology (`mango`) still excludes itself.
- **No over-trigger on blanks** — empty/whitespace declarations exclude nothing (never "exclude
  everything").
- **Fuzz** (seeded, 4 seeds × 200 iterations) — across randomized pools mixing safe foods and unsafe
  derivatives, *every* recipe containing an excluded term is removed and the surviving pool is always clean
  under the defense-in-depth re-check.

## Residual risks (accepted, with the long-term fix)

1. **Coverage is bounded by the ontology table and by free-text ingredient names.** A cheese the table
   doesn't name as a member (e.g. a recipe ingredient literally "Gruyère" with no "cheese" in the string)
   would not be caught. The honest long-term fix is **structured allergen tags on catalog foods /
   ingredients** (a typed `Allergen` set resolved at import time) instead of substring matching on names —
   tracked as a data-model follow-up, not solvable in the gate alone.
2. **Search and manual logging are not allergen-filtered.** This is by design — they are user-initiated and
   must be able to surface any food (e.g. to log what someone actually ate). The safety guarantee is scoped
   to *system-generated recommendations* (the plan). A future enhancement could surface a non-blocking
   "contains a declared allergen" warning badge in search.
3. **Imported/user-authored recipes** are the user's own content; the gate still applies when such a recipe
   is considered for a *generated plan*.

## Invariant

> For any set of declared allergens, no recipe containing a declared allergen **or a covered derivative**
> appears in a generated plan — enforced at FILTER and re-asserted post-selection, and never bypassable by
> the LLM.
