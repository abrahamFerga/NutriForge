# NutriForge.Web

The frontend SPA for **NutriForge**, an enterprise nutrition app. Built with
Vite + React 18 + TypeScript, Tailwind CSS v4, React Router v6, TanStack Query v5,
Recharts, and lucide-react.

## Prerequisites

- Node 20+ (developed against Node 24 / npm 11)
- The NutriForge .NET backend running and reachable at `VITE_API_BASE`.

## Getting started

```bash
npm install        # install dependencies
npm run dev        # start the Vite dev server (http://localhost:5173)
npm run build      # type-check (tsc -b) + production build into dist/
npm run preview    # preview the production build
```

## Configuration

The API base URL is read from `VITE_API_BASE`.

```bash
cp .env.example .env   # then edit if needed
```

- Unset → defaults to `http://localhost:5000`.
- Set to an empty string → relative paths (use behind a reverse proxy serving
  both the SPA and the API on the same origin).

### Dev auth

For local development the backend uses a dev-auth scheme. The API client
(`src/lib/api.ts`) sends these headers on **every** request:

- `X-Debug-Subject: demo-user`
- `X-Debug-Role: user`

Writes (`POST`/`PUT`/`DELETE`) additionally send `Content-Type: application/json`
and a random `Idempotency-Key` (`crypto.randomUUID()`).

## Pages / routes

| Route      | Page      | Description                                                   |
| ---------- | --------- | ------------------------------------------------------------ |
| `/`        | Dashboard | Calorie ring, macro bars, 7-day trend chart.                 |
| `/diary`   | Diary     | Debounced food search, log form, entries grouped by meal.    |
| `/profile` | Profile   | Profile form; saving recomputes targets and refreshes diary. |

A floating **NutritionAssistant** chatbot drawer is present on every route
(placeholder until the assistant backend exists).

## Project structure

```
src/
  components/        AppShell, AssistantPanel, MacroBar, StateMessage, ui/*
  components/ui/     shadcn-style primitives: button, card, input, label, select, spinner
  hooks/             TanStack Query hooks + useDebounced
  lib/               api.ts (typed fetch client), types.ts (DTOs/enums), queryKeys.ts, utils.ts
  pages/             Dashboard, Diary, Profile
```
