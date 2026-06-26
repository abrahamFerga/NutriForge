import { useState } from "react";
import { NavLink, Navigate, Outlet, useLocation } from "react-router-dom";
import {
  BookOpen,
  CalendarRange,
  ChefHat,
  LayoutDashboard,
  LogOut,
  MessageCircle,
  User,
  Utensils,
} from "lucide-react";
import { AssistantPanel } from "@/components/AssistantPanel";
import { LanguageSwitcher } from "@/components/LanguageSwitcher";
import { ThemeToggle } from "@/components/ThemeToggle";
import { Spinner } from "@/components/ui/spinner";
import { useProfile } from "@/hooks/useQueries";
import { authEnabled, logout } from "@/lib/auth";
import { isOnboardingDismissed } from "@/lib/onboarding";
import { cn } from "@/lib/utils";

const NAV = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard, end: true },
  { to: "/diary", label: "Diary", icon: BookOpen, end: false },
  { to: "/recipes", label: "Recipes", icon: ChefHat, end: false },
  { to: "/plan", label: "Diet Plan", icon: CalendarRange, end: false },
  { to: "/profile", label: "Profile", icon: User, end: false },
];

export function AppShell() {
  const [assistantOpen, setAssistantOpen] = useState(false);
  const profile = useProfile();
  const location = useLocation();

  // First-run gate (#68): a brand-new user (no profile, hasn't skipped) is sent to the welcome wizard.
  // We wait for the profile query so we don't flash the empty app first. An error falls through to the
  // shell rather than trapping the user.
  if (profile.isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Spinner />
      </div>
    );
  }
  if (profile.data === null && !isOnboardingDismissed()) {
    return <Navigate to="/welcome" replace />;
  }

  return (
    <div className="flex min-h-screen text-slate-200">
      {/* Skip to main content — WCAG 2.4.1 */}
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded focus:bg-brand-500 focus:px-3 focus:py-2 focus:text-sm focus:font-medium focus:text-slate-950"
      >
        Skip to main content
      </a>

      {/* Sidebar */}
      <aside
        aria-label="Primary navigation"
        className="sticky top-0 hidden h-screen w-64 shrink-0 flex-col border-r border-slate-800/70 bg-slate-950/40 px-3 py-5 backdrop-blur-xl md:flex"
      >
        <Brand />
        <nav aria-label="Main menu" className="mt-8 flex flex-col gap-1">
          {NAV.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                cn(
                  "group relative flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-all duration-150",
                  isActive
                    ? "bg-gradient-to-r from-brand-500/20 to-teal-500/5 text-brand-200 ring-1 ring-inset ring-brand-500/25 shadow-sm shadow-brand-500/10"
                    : "text-slate-400 hover:bg-slate-800/60 hover:text-slate-100",
                )
              }
              aria-current={undefined}
            >
              {({ isActive }) => (
                <>
                  <span
                    className={cn(
                      "absolute top-1.5 bottom-1.5 -left-3 w-1 rounded-r-full bg-brand-400 transition-opacity",
                      isActive ? "opacity-100" : "opacity-0",
                    )}
                    aria-hidden="true"
                  />
                  <Icon className="h-5 w-5 shrink-0" aria-hidden="true" />
                  {label}
                </>
              )}
            </NavLink>
          ))}
        </nav>
      </aside>

      {/* Main column */}
      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar />
        {/* Extra bottom padding on mobile leaves room for the fixed bottom tab bar + FAB. */}
        <main
          id="main-content"
          className="mx-auto w-full max-w-6xl flex-1 px-4 pt-6 pb-28 sm:px-6 md:pb-8 lg:px-8"
        >
          {/* Key on the path so each route gets a subtle entrance animation. */}
          <div key={location.pathname} className="nf-animate-in">
            <Outlet />
          </div>
        </main>
      </div>

      {/* Mobile bottom tab bar — thumb-reachable primary navigation. */}
      <nav className="fixed inset-x-0 bottom-0 z-30 flex border-t border-slate-800 bg-slate-950/95 pb-[env(safe-area-inset-bottom)] backdrop-blur md:hidden">
        {NAV.map(({ to, label, icon: Icon, end }) => (
          <NavLink
            key={to}
            to={to}
            end={end}
            className={({ isActive }) =>
              cn(
                "flex flex-1 flex-col items-center gap-1 px-1 py-2 text-[0.65rem] font-medium transition-colors",
                isActive ? "text-brand-300" : "text-slate-400 hover:text-slate-100",
              )
            }
          >
            <Icon className="h-5 w-5 shrink-0" />
            <span className="max-w-full truncate">{label}</span>
          </NavLink>
        ))}
      </nav>

      {/* Floating assistant button — present on every route. Sits above the bottom tab bar on mobile. */}
      <button
        onClick={() => setAssistantOpen(true)}
        aria-label="Open NutritionAssistant"
        className="group fixed right-5 bottom-20 z-40 flex h-14 w-14 items-center justify-center rounded-2xl bg-gradient-to-br from-brand-400 to-teal-500 text-slate-950 shadow-xl shadow-brand-500/40 ring-1 ring-white/10 transition-all hover:-translate-y-0.5 hover:shadow-brand-500/60 md:bottom-5"
      >
        <MessageCircle className="h-6 w-6 transition-transform group-hover:scale-110" />
      </button>

      <AssistantPanel open={assistantOpen} onClose={() => setAssistantOpen(false)} />
    </div>
  );
}

function Brand() {
  return (
    <div className="flex items-center gap-2.5 px-2">
      <span className="flex h-10 w-10 items-center justify-center rounded-2xl bg-gradient-to-br from-brand-400 to-teal-500 text-slate-950 shadow-lg shadow-brand-500/30 ring-1 ring-white/10">
        <Utensils className="h-5 w-5" />
      </span>
      <p className="text-lg font-bold tracking-tight">
        <span className="nf-gradient-text">Nutri</span>
        <span className="text-slate-100">Forge</span>
      </p>
    </div>
  );
}

function TopBar() {
  return (
    <header className="sticky top-0 z-20 flex h-14 items-center justify-between border-b border-slate-800/70 bg-slate-950/60 px-4 backdrop-blur-xl sm:px-6">
      <div className="flex items-center gap-2 md:hidden">
        <span className="flex h-8 w-8 items-center justify-center rounded-xl bg-gradient-to-br from-brand-400 to-teal-500 text-slate-950 shadow-md shadow-brand-500/30">
          <Utensils className="h-4 w-4" />
        </span>
        <span className="font-bold tracking-tight">
          <span className="nf-gradient-text">Nutri</span>
          <span className="text-slate-100">Forge</span>
        </span>
      </div>
      <div className="hidden md:block" />
      <div className="flex items-center gap-2">
        <LanguageSwitcher />
        <ThemeToggle />
        {authEnabled ? (
          <button
            onClick={() => logout()}
            aria-label="Sign out"
            className="flex h-9 w-9 items-center justify-center rounded-xl border border-slate-800/80 bg-slate-900/60 text-slate-400 transition-colors hover:border-slate-700 hover:text-slate-100"
          >
            <LogOut className="h-4 w-4" />
          </button>
        ) : null}
      </div>
    </header>
  );
}
