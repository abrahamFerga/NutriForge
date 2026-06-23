# Algorithm: BMR → TDEE → Calorie Target → Macro Split

This is the deterministic math that turns a user's body stats + goal into the numbers every other subsystem consumes. It is pure arithmetic — no ML, no LLM. Get it exactly right; everything downstream trusts it.

---

## Step 1 — BMR (Basal Metabolic Rate)

Energy burned at complete rest. **Use Mifflin-St Jeor** (1990) — research consistently finds it more accurate than Harris-Benedict for modern adults (Harris-Benedict, from 1919/1984, overestimates BMR by ~5–15% in sedentary people).

```
BMR = 10 × weight_kg + 6.25 × height_cm − 5 × age_years + s

  where s = +5   for male
            −161 for female
```

**Optional — Katch-McArdle** (more accurate when body-fat % is known, e.g. lean/athletic users):

```
BMR = 370 + 21.6 × leanBodyMass_kg
  where leanBodyMass_kg = weight_kg × (1 − bodyFatPct/100)
```

NutriForge: default to Mifflin-St Jeor; switch to Katch-McArdle automatically when the user has entered a body-fat percentage.

---

## Step 2 — TDEE (Total Daily Energy Expenditure)

Multiply BMR by an **activity factor**:

| Activity level | Factor | Description |
|---|---|---|
| Sedentary | **1.2** | Little/no exercise |
| Lightly active | **1.375** | Light exercise 1–3 days/wk |
| Moderately active | **1.55** | Moderate exercise 3–5 days/wk |
| Active | **1.725** | Hard exercise 6–7 days/wk |
| Very active | **1.9** | Very hard exercise + physical job |

```
TDEE = BMR × activityFactor
```

> TDEE already includes the Thermic Effect of Food (~10% of intake) and activity. NutriForge uses **gross accounting** (see [calorie-tracking research](../research/01-calorie-tracking.md)): TDEE is the maintenance target; exercise is *not* logged back as extra calories.

---

## Step 3 — Goal-adjusted calorie target

Apply the goal as a delta to TDEE. Rule of thumb: ~7,700 kcal ≈ 1 kg of body fat, so 0.5 kg/week ≈ a 550 kcal/day deficit.

| Goal | Adjustment | Notes |
|---|---|---|
| Aggressive cut | −20% (or −500…−750 kcal) | Cap the rate; never below a safety floor |
| Moderate cut | −15% (≈ −500 kcal) | ~0.45 kg/week |
| Maintain | ±0 | |
| Lean bulk | +10% (≈ +250…+300 kcal) | Minimize fat gain |
| Bulk | +20% | |

```
calorieTarget = TDEE × (1 + goalAdjustmentPct)
calorieTarget = max(calorieTarget, safetyFloor)
  safetyFloor ≈ 1500 (male) / 1200 (female)   ← hard guardrail; never generate below this
```

**The safety floor is a guardrail, not a suggestion.** The diet generator must never be handed a target below it.

---

## Step 4 — Macro split

Two strategies; NutriForge supports both and defaults to **protein-anchored** because it produces better body-composition outcomes than naive percentages.

### Strategy A — Protein-anchored (recommended)

1. **Protein** from bodyweight (evidence-based ranges):
   - Maintenance / bulk: **~2.2 g/kg** (≈1 g/lb)
   - Cut (preserve muscle in a deficit): **~1.8–2.7 g/kg** (conservative band); aggressive up to ~3.1 g/kg
2. **Fat** as a floor for hormonal health: **0.8–1.0 g/kg** (≈20–35% of calories — the AMDR fat range).
3. **Carbs** fill the remainder.

```
protein_g = bodyweight_kg × proteinPerKg          # e.g. 2.0
fat_g     = bodyweight_kg × fatPerKg              # e.g. 0.9
protein_kcal = protein_g × 4
fat_kcal     = fat_g × 9
carb_kcal    = calorieTarget − protein_kcal − fat_kcal
carb_g       = max(carb_kcal, 0) / 4
```

Atwater factors: **protein 4, carbohydrate 4, fat 9 kcal/g** (alcohol 7, if ever modeled).

### Strategy B — Percentage / AMDR

Use the Acceptable Macronutrient Distribution Ranges (Institute of Medicine) directly:

| Macro | AMDR | kcal/g |
|---|---|---|
| Protein | 10–35% | 4 |
| Carbohydrate | 45–65% | 4 |
| Fat | 20–35% | 9 |

```
macro_g = (calorieTarget × percent) / kcalPerGram
```

Diet types override the split — **keto** clamps carbs to ≤~10% and pushes fat up; **high-protein** pins protein to the top of its band. These overrides live in the diet-type rule data (see [diet generation](../research/03-diet-generation.md) §5).

---

## Worked example

> Male, 30 y, 80 kg, 180 cm, moderately active, moderate cut, protein-anchored.

```
BMR  = 10×80 + 6.25×180 − 5×30 + 5
     = 800 + 1125 − 150 + 5 = 1780 kcal
TDEE = 1780 × 1.55 = 2759 kcal
target = 2759 × 0.85 ≈ 2345 kcal           (moderate cut, −15%)

protein = 80 × 2.2 = 176 g  → 704 kcal
fat     = 80 × 0.9 = 72 g   → 648 kcal
carbs   = (2345 − 704 − 648)/4 = 248 g → 993 kcal
check: 704 + 648 + 993 = 2345 ✓ (and 2345 > 1500 floor ✓)
```

That tuple — `{ kcal: 2345, P: 176, F: 72, Cb: 248 }` — is exactly what the meal-plan generator's VERIFY step checks against.

---

## Where this lives in the system

A single deterministic service (`NutritionTargets`) exposes `ComputeTargets(profile, goal, strategy) → TargetTuple`. It is called by:
- **Calorie tracking** — to set the daily ring/budget.
- **Diet generation** — as the hard target for steps 4–5 of the pipeline.

Pure function, fully unit-testable, no I/O. The worked example above is a golden test.

---

## Key sources

- Mifflin-St Jeor equation (1990) & comparison vs. Harris-Benedict; activity factors 1.2–1.9.
- AMDR (protein 10–35%, carbs 45–65%, fat 20–35%).
- Protein-by-goal g/kg ranges (cut 1.8–2.7+, bulk/maintenance ~2.2).
- Atwater factors (4/4/9).

Full URLs in [`/SOURCES.md`](../../SOURCES.md).
