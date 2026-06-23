# Research: Nutrition Data Sources

Every pillar depends on one thing: a trustworthy mapping from *food* → *nutrients per gram*. This document compares the credible data sources and lands the single most important infrastructure decision in the system.

---

## 1. The shootout

| Source | Size | Barcode/UPC | Cost | Strength | Watch-out |
|---|---|---|---|---|---|
| **USDA FoodData Central** | ~600k across 4 datasets | partial (Branded has GTIN) | **Free** (API key) | Authoritative, government-backed, portion data, no licensing strings | US-centric; branded coverage trails commercial DBs; 1,000 req/hr/key |
| **Open Food Facts** | ~2.5M+ products | **Yes** (community) | **Free** (open data) | Huge barcode coverage, global, allergen tags | Crowd-sourced → uneven accuracy; gaps in micronutrients |
| **Nutritionix** | ~1.9M items | Yes | Enterprise (~$1,850/mo) | Strong US branded + restaurant; NLP endpoint; powers Apple Health/MFP-class apps | Expensive; vendor lock-in |
| **Edamam** | ~900k foods, 680k UPCs, 2.3M recipes | limited | Free → ~$999/mo | **Recipe NLP** (free text → nutrition), recipe DB | Weak native barcode; recipe focus |
| **FatSecret** | large | **Yes** | Free → enterprise | Barcode + FDA-compliant; deep app integrations | Tiered access; commercial terms |
| **Chomp** | branded | **Yes** | Commercial | Allergen + nutrient per product | Paid; narrower than Open Food Facts |

(Figures are from 2025–2026 API comparison roundups — see sources. Treat pricing as directional; verify before committing.)

---

## 2. USDA FoodData Central in detail (the free anchor)

FDC is four datasets behind one API:

| Dataset | ~Count | What it is | NutriForge use |
|---|---|---|---|
| **Foundation Foods** | small, growing | Deeply analyzed whole foods with provenance | Highest-trust base ingredients |
| **SR Legacy** | ~7,000 | Standard Reference, **frozen 2018** | Broad generic-food fallback |
| **Branded** | ~380,000 | Label data from the Global Branded Foods DB, has GTIN/UPC | Barcode-scannable packaged foods |
| **Survey (FNDDS)** | ~8,000 | "Foods as consumed" — e.g. *chicken breast, grilled, no skin* | Realistic logged-meal entries |

- **Two endpoints:** `/foods/search` (search) and `/food/{fdcId}` (details).
- **Auth:** free API key. `DEMO_KEY` exists for trial but is throttled to **30 req/hr**; a real key gives ~1,000 req/hr.
- **License:** US government public domain — no attribution or redistribution restrictions. This is rare and valuable.

This is why FDC is the seed: free, authoritative, redistributable, and it ships portion records and the four trust-tiered datasets that map cleanly onto NutriForge's `verificationStatus` model.

---

## 3. The decision: own a local nutrient store; APIs are importers

**Do not call third-party nutrition APIs on the hot path.** Instead:

```
                ┌─────────────── import pipeline (offline / async) ───────────────┐
USDA FDC ──────▶│                                                                  │
Open Food Facts▶│  fetch → normalize → de-dupe → tag verificationStatus → upsert   │──▶ NutriForge
(barcode) ─────▶│                                                                  │    Food/Nutrient store
commercial ────▶│                                                                  │    (owned, indexed)
(optional) ─────└──────────────────────────────────────────────────────────────────┘        │
                                                                                              ▼
                          read path: search / barcode / solver  ◀── never blocks on an external API
```

**Why this is non-negotiable:**

1. **Latency & reliability.** A meal-plan solver iterates over thousands of foods; a barcode scan must feel instant. Neither can wait on an external rate-limited call.
2. **Rate limits.** FDC ~1,000 req/hr would be exhausted by a single solver run. Local store removes the ceiling.
3. **Consistency.** Recipe nutrition is *computed* from your store (see [`02-batch-cooking.md`](02-batch-cooking.md)). The solver, the tracker, and the recipe nutrition must all read the *same* numbers — only possible if you own them.
4. **Normalization.** Every source has a different shape. Normalize once at import into a canonical per-100g nutrient vector; everything downstream sees one model.
5. **Cost control.** Free USDA covers the base; pay for a commercial source only to enrich gaps (branded/restaurant), and only the import job pays the per-call cost — not every user request.

This mirrors what NutriGen and the production apps actually do: build a *personalized/local nutrition database* and ground everything in it.

---

## 4. Recommended sourcing strategy by phase

| Phase | Source(s) | Why |
|---|---|---|
| **MVP** | USDA FDC (all 4 datasets) | Free, authoritative, redistributable, has portions — enough to build & demo every pillar |
| **v1 (barcode)** | + Open Food Facts | Free, huge UPC coverage for packaged-food scanning |
| **v2 (enrich)** | + one commercial (Nutritionix or FatSecret) | Fill US branded/restaurant gaps *if* real users need them — import-time only |
| **Recipe NLP** | Edamam (optional) | If you want hosted recipe→nutrition parsing instead of building it |

---

## 5. The canonical nutrient vector

Normalize every imported food to this shape (per 100 g / 100 ml), regardless of source:

```
NutrientProfile (per 100g):
  energyKcal, protein_g, fat_g, saturatedFat_g, carbohydrate_g, sugars_g,
  fiber_g, sodium_mg, ... (micros optional, nullable)
  sourceRef: { provider, providerId }      ← provenance, for audit & re-import
  verificationStatus: foundation | authoritative | community | user-submitted
```

Nullable micronutrients matter: USDA Foundation foods have rich micros; Open Food Facts often has only macros. Don't drop a food for missing micros — null them and degrade gracefully (the solver constrains on what's present).

---

## Key sources

- 2025–2026 nutrition-API comparison roundups (CalorieAPI, Suggestic, GreenChoice, Spike, EatFresh) — sizes, pricing, barcode support.
- USDA FoodData Central API Guide & data documentation (datasets, endpoints, DEMO_KEY 30/hr, public-domain license).
- NutriGen (arXiv 2502.20601) — local/personalized nutrition DB grounding pattern.

Full URLs in [`/SOURCES.md`](../../SOURCES.md).
