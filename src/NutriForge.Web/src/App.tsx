import { lazy, Suspense } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/components/AppShell";
import { RouteErrorBoundary } from "@/components/RouteErrorBoundary";
import { Spinner } from "@/components/ui/spinner";

// Code-split every route page so the initial download is just the shell + the landing route; the other
// pages — and their heavy dependencies (e.g. the charts bundle, the recipe import/AI forms) — load on
// demand. This keeps first paint fast on mobile networks. Pages use named exports, hence the `.then` map.
const Dashboard = lazy(() => import("@/pages/Dashboard").then((m) => ({ default: m.Dashboard })));
const Diary = lazy(() => import("@/pages/Diary").then((m) => ({ default: m.Diary })));
const Onboarding = lazy(() => import("@/pages/Onboarding").then((m) => ({ default: m.Onboarding })));
const Plan = lazy(() => import("@/pages/Plan").then((m) => ({ default: m.Plan })));
const Profile = lazy(() => import("@/pages/Profile").then((m) => ({ default: m.Profile })));
const Recipes = lazy(() => import("@/pages/Recipes").then((m) => ({ default: m.Recipes })));

function FullScreenSpinner() {
  return (
    <div className="flex min-h-screen items-center justify-center">
      <Spinner />
    </div>
  );
}

export default function App() {
  return (
    <Routes>
      {/* First-run wizard (#68) lives outside the shell so it's full-screen and un-gated. */}
      <Route
        path="/welcome"
        element={
          <RouteErrorBoundary>
            <Suspense fallback={<FullScreenSpinner />}>
              <Onboarding />
            </Suspense>
          </RouteErrorBoundary>
        }
      />
      {/* The shell wraps its Outlet in Suspense, so nav + top bar stay put while a page chunk loads. */}
      <Route element={<AppShell />}>
        <Route index element={<Dashboard />} />
        <Route path="diary" element={<Diary />} />
        <Route path="recipes" element={<Recipes />} />
        <Route path="plan" element={<Plan />} />
        <Route path="profile" element={<Profile />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
