# Architecture: Tech Stack

The recommended stack and the reasoning behind each choice. This is deliberately aligned with an enterprise-.NET + modern-React profile — NutriForge is meant to be portfolio-grade, showing depth in both halves.

---

## At a glance

| Layer | Choice | Role |
|---|---|---|
| **Orchestration** | **.NET Aspire** | Local dev composition, service discovery, dashboard, deploy manifest |
| **API** | **ASP.NET Core 10** (Minimal APIs or controllers) | The modular-monolith host |
| **AI / agents** | **Microsoft Agent Framework (MAF)** over **Microsoft.Extensions.AI (MEAI)** | DietGen intent-parse, SELECT agent, structured output |
| **Optimization** | **Google OR-Tools** (or HiGHS) | LP portion-repair in meal-plan generation |
| **Data** | **PostgreSQL** + **EF Core 10** | Relational core, FTS, JSONB, migrations |
| **Cache** | **Redis** | Food search, parse cache, intent cache, sessions |
| **Vector (later)** | **pgvector** | Fuzzy ingredient entity resolution |
| **Front end** | **React + Vite + TypeScript + shadcn/ui + Tailwind** | The SPA |
| **Data fetching** | **TanStack Query** | Server-state, caching, the async plan-polling flow |
| **Charts** | **Recharts** (or visx) | Calorie/macro rings & trends |
| **Validation** | **FluentValidation** (BE) + **Zod** (FE) | Shared-shape input validation |
| **Telemetry** | **OpenTelemetry** via Aspire | Traces/metrics/logs, end-to-end DietGen tracing |
| **Auth** | ASP.NET Core Identity + JWT | SPA auth, per-user isolation |

---

## Why .NET 10 + Aspire

- **Aspire** gives a one-command local environment (API + Postgres + Redis + import worker), service discovery, and an observability dashboard for free — exactly what a multi-component system like this needs to stay debuggable. Its deployment manifest maps cleanly to Azure Container Apps later.
- **Modular monolith** in ASP.NET Core keeps the three pillars in one deployable with enforced module boundaries (see [system-design](system-design.md)) — the right altitude for this scale.
- EF Core 10 covers the relational core; Postgres FTS handles food search without a separate search engine at MVP.

## Why Microsoft Agent Framework for DietGen

The "generate diets based on desires" pillar is the one genuinely agentic part, and MAF is the natural fit on a .NET stack:

- **`IChatClient` (MEAI) abstraction** → provider-swappable: Azure OpenAI in prod, Anthropic, or **Ollama locally** for cheap/offline dev. The research shows even an 8B local model (Llama 3.1) hits ~1.5% calorie error when grounded — so local dev is viable.
- **Structured output** bound to C# types enforces the [AI boundary](system-design.md#3-the-ai-boundary): `parse()` returns a typed `DietIntent`, `select()` returns typed `PlanSlot[]` — the agent *cannot* return a free-form calorie number.
- **Function tools** let the SELECT agent query the candidate pool through a typed tool rather than being handed raw data, keeping it grounded.
- **OpenTelemetry built in** → trace every parse→select→verify→repair run in the Aspire dashboard.

> A project skill (`agent-framework-csharp`) already covers scaffolding MAF agents, tools, structured output, and DI hosting — use it when building the DietGen context.

**What MAF is *not* for here:** the nutrition math, allergen filtering, and LP repair are deterministic .NET code, not agent calls. MAF owns intent/creativity/explanation only.

## Why OR-Tools for repair

The REPAIR step ([meal-plan-generation](../algorithms/meal-plan-generation.md) §5) is a small linear program (portion multipliers minimizing macro deviation). OR-Tools' GLOP/CP-SAT (or HiGHS via a wrapper) solves it in milliseconds, deterministically, with no external service. This is what keeps plans *provably* on-target rather than LLM-approximately on-target.

## Why React + Vite + shadcn/ui

- **Vite** for fast dev/build; **TypeScript** end-to-end.
- **shadcn/ui + Tailwind** for a clean, ownable component layer (copy-in components, not a black-box library) — good for a distinctive, non-generic UI.
- **TanStack Query** is the right tool for the **async plan-generation flow**: `POST /diet-plans` → 202 → poll `GET /diet-plans/{id}` until `ready`, with built-in caching/retry. It also caches food search nicely.
- **Recharts** for the calorie ring, macro breakdown, and weekly trends.

---

## Deployment target

| Component | Azure (recommended) | Notes |
|---|---|---|
| API (Aspire app) | **Azure Container Apps** | Aspire emits the manifest |
| Postgres | **Azure Database for PostgreSQL** | managed, pgvector available |
| Redis | **Azure Cache for Redis** | |
| LLM | **Azure OpenAI** | or Anthropic via MEAI; Ollama for local |
| Static SPA | **Azure Static Web Apps** / CDN | |
| Import workers | Container Apps job (scheduled) | nightly USDA/OFF sync |

Cloud-agnostic fallback: any container host + managed Postgres/Redis + any MEAI-supported LLM provider.

---

## Notable explicit non-choices

- **No microservices at MVP** — modular monolith; extract only on a named bottleneck.
- **No separate search engine (Elastic) at MVP** — Postgres FTS + Redis is enough until proven otherwise.
- **No bespoke ML model for nutrition** — the math is deterministic formulas; the "AI" is an LLM for language, not a trained nutrition model.
- **No trusting LLM arithmetic** — every number is computed in .NET. (The single most important constraint in the whole stack.)

---

See also: [system-design](system-design.md) · [meal-plan-generation](../algorithms/meal-plan-generation.md).
