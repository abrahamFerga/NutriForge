# NutriForge — Product specification

> **Phase 2 artifact (synthesize-spec).** No `research/<industry>.md` or `workflow.json`
> was present; this spec is synthesized from the equivalent research already in the repo —
> [`docs/research/01-calorie-tracking.md`](docs/research/01-calorie-tracking.md),
> [`02-batch-cooking.md`](docs/research/02-batch-cooking.md),
> [`03-diet-generation.md`](docs/research/03-diet-generation.md),
> [`04-nutrition-data-sources.md`](docs/research/04-nutrition-data-sources.md) — plus
> [`README.md`](README.md) and [`ROADMAP.md`](ROADMAP.md). It is the *what/why*; the *how*
> lives in [`ARCH.md`](ARCH.md) and [`DECISIONS.md`](DECISIONS.md).

## In one sentence

NutriForge is a web nutrition platform for goal-driven home eaters that tracks what they
eat against a personalized target, turns the recipes they choose into one consolidated
shopping list, and generates meal plans from a plain-language desire — where every nutrition
number the user sees is computed and verified, never estimated.

## Primary jobs to be done

- **When I'm working toward a body-composition goal, I want to log what I eat against a daily
  calorie and macro target with minimal effort, so that I know if I'm on track without doing
  the math myself.**
- **When I set up my profile, I want my calorie and macro targets derived from my stats and
  goal, so that I don't have to guess the right numbers.**
- **When I'm logging a food, I want to find it instantly (search, barcode, or by typing
  "2 eggs and toast"), so that logging never becomes a chore I abandon.**
- **When I've chosen the recipes I want to cook this week, I want one consolidated,
  aisle-grouped shopping list with what's already in my pantry subtracted, so that a single
  shopping trip covers the week.**
- **When I describe the diet I want in plain language ("high-protein vegan around 2000 kcal,
  no mushrooms"), I want a 7-day plan that provably hits my targets and honors every
  restriction, so that I get a trustworthy plan without manual meal math.**
- **When I accept a generated plan, I want to turn it into a shopping list and then track how
  closely I followed it, so that planning, cooking, and tracking are one continuous flow.**

## Target personas

- **The Cutter** — someone in a calorie deficit trying to lose fat while preserving muscle.
  Top 3 tasks:
  1. Set their goal to a moderate cut and receive a daily calorie + protein target.
  2. Log every day's food by search and barcode against that target.
  3. Review the weekly trend to confirm they're holding the deficit.
- **The Builder** — someone gaining or maintaining on a high-protein, protein-anchored plan.
  Top 3 tasks:
  1. Choose a protein-anchored macro strategy and see the resulting macro split.
  2. Generate a high-protein meal plan that stays within their calorie target.
  3. Log intake and check macro adherence (especially protein) day to day.
- **The Meal-Prepper** — someone who batch-cooks components once and recombines across the week.
  Top 3 tasks:
  1. Pick recipes (or accept a generated plan) for the week and scale them to servings.
  2. Generate one consolidated, aisle-grouped shopping list with pantry stock subtracted.
  3. See trustworthy per-serving nutrition for each recipe, consistent with their diary.
- **The Restricted Eater** — someone with a fixed diet or allergy (vegan, keto, halal, nut-free…).
  Top 3 tasks:
  1. Set persistent diet type, allergens, and dislikes on their profile once.
  2. Generate plans and search recipes that are provably compliant and allergen-safe.
  3. Trust that no plan, recipe, or suggestion ever surfaces a declared allergen.

> The operational **admin / data steward** is a real role (see RBAC) but not a product
> persona — they keep the food catalog healthy; they are not an end user with jobs-to-be-done.

## Capabilities

### Must have (v1)

The smallest thing that delivers the primary job — a usable, low-friction food diary.

| Capability | One-line description | Personas |
|---|---|---|
| Trustworthy food search & logging | Fast search over a verification-tiered food catalog (curated > authoritative > community > user-submitted, never silently blended), logged by search-and-select with named portions. | Cutter, Builder, Restricted Eater |
| Personalized targets | Daily calorie and macro targets derived from the user's stats and goal (with a hard safety floor), recomputed on profile change. | Cutter, Builder, Restricted Eater |
| Daily diary & progress | Log meals against the day, see the day's total vs. target and a range/trend summary; historical days are immutable. | Cutter, Builder |
| Low-friction logging | Barcode scan and natural-language entry ("2 eggs and toast" → confirmable entries) in addition to search. | Cutter, Builder, Restricted Eater |

### Differentiators (v1)

The "why NutriForge" — and where the pillars compose into one loop.

| Capability | Why it matters | Personas |
|---|---|---|
| Desire-to-plan diet generation | Turns a free-text desire into a 7-day plan that *provably* lands within tolerance of calorie/macro targets and honors every hard restriction; allergens are enforced and double-checked, never estimated. No competitor pairs natural-language input with verified-correct numbers. | Builder, Restricted Eater |
| Computed-nutrition batch cooking | Recipe nutrition is computed from ingredients (never trusted from the source), and a chosen plan becomes one consolidated, aisle-grouped shopping list with pantry subtraction — the part most meal-prep apps get wrong. | Meal-Prepper |
| The closed loop | One click turns an accepted plan into a shopping list, and logged intake measures adherence back against it — plan → shop → cook → track is continuous, not three disconnected tools. | Meal-Prepper, Cutter, Builder |

### Explicitly out of scope (v1)

- **Cook scheduling** (appliance/temperature grouping, shelf-life-aware timing) — the shopping list delivers the value; scheduling is polish.
- **Commercial nutrition enrichment** (US branded/restaurant data from paid providers) — free public datasets cover v1; enrich only if real users hit gaps, import-time only.
- **Fuzzy ingredient matching via embeddings** — a canonical alias table is enough for v1.
- **Adherence-driven auto-regeneration** (next plan adapts to what you actually ate) — v2.
- **Exercise tracking, wearable integration, and "net"-calorie accounting** — NutriForge uses gross accounting; exercise is informational at most.
- **Social/community features** (sharing, feeds, leaderboards) — serves no primary job.
- **Native mobile apps** — web-first, responsive SPA only.
- **Multi-user / coaching (B2B)** — a coach managing many clients; v1 is single-user B2C (one person owns only their own data).

## RBAC model (initial)

NutriForge is single-user B2C; the role vocabulary is deliberately small.

- **user** — owns and manages only their own profile, diary, meal plans, pantry, and shopping
  lists; reads the shared food/recipe catalog. Cannot access any other user's data and cannot
  run imports or change a food's verification tier.
- **admin / data steward** — triggers and monitors the nightly food-catalog imports, reviews
  import status and errors, and curates food verification tiers. Cannot read end users'
  personal diary or plan data.

## Regulatory constraints

- **GDPR (Regulation (EU) 2016/679)** — profiles hold health-adjacent personal data (sex,
  birth date, weight, body-fat %, dietary/health restrictions). Implies: Art. 20 data-export
  endpoint, Art. 17 erasure as a per-user hard delete, PII explicitly tagged and carried
  through audit logging and export, and lawful-basis consent at signup.
- **Allergen safety (modeled on EU FIC Reg. 1169/2011, Annex II)** — declared allergens are
  safety-critical: the system must deterministically exclude and then re-verify them, and must
  never present a plan, recipe, or suggestion containing a declared allergen. Treated as a
  hard boundary, not a preference.
- **Calorie safety floor** — generated calorie targets must never fall below the documented
  floor (≈1500 kcal male / 1200 kcal female). A guardrail the plan generator may never breach.
- **Open-data licensing** — community nutrition datasets used to seed the catalog carry
  share-alike/attribution obligations (e.g. ODbL); the product must attribute them and respect
  redistribution terms. Government public-domain data carries none.
- **Informational, not medical** — NutriForge presents nutritional information, not medical or
  treatment advice; no therapeutic claims (general consumer-protection posture).

## Success metrics

Each is numeric and observable in the system's telemetry.

- **Day-1 activation:** ≥ 60% of new users log a complete day (entries in ≥ 3 meal slots, or
  reach within 10% of their target) within 24h of signup. *(diary events per new user)*
- **Logging friction:** median time from opening search to a logged entry < 15s, with ≥ 80% of
  logs via search or barcode rather than manual entry. *(client timing + per-log method tag)*
- **Plan correctness:** ≥ 95% of generated plans land within ±5% of the calorie target, and
  100% honor declared allergens and diet type. *(the verification-step result on each plan)*
- **Loop completion:** ≥ 40% of accepted diet plans are converted into a shopping list within
  48h of acceptance. *(plan-accepted → shopping-list-created event)*
- **Plan latency:** 95th-percentile end-to-end plan generation completes in < 30s. *(the
  end-to-end generation trace span)*

## Open questions for plan-system

1. **Scale target for v1** (concurrent users, plans generated per day) — drives whether diet
   generation stays in-process or is the first module extracted to a service.
2. **v1 diet-type seed set** — which of vegan, vegetarian, keto, paleo, Mediterranean,
   gluten-free, halal, kosher ship in v1 vs. v2?
3. **Sequencing of low-friction logging within v1** — ship search-only first as the demoable
   tracking MVP and fast-follow barcode + natural-language inside v1, or land all three in the
   first release? (The research ranks search P0, barcode P1, NL P2.)
4. **Recipe sourcing for v1** — URL import + manual create only, or a seeded recipe set? How
   many recipes must exist for the diet generator's candidate pool to feasibly fill a 7-day
   restricted plan (e.g. vegan, ≤ 20-min prep)?
5. **Data residency** — single EU region is assumed; is there any residency requirement beyond
   GDPR that would force multi-region or a specific region?
6. **Auth provider for v1** — confirm a managed consumer OIDC provider is acceptable for a
   portfolio build, or whether a simpler dev identity provider should stand in for v1.
