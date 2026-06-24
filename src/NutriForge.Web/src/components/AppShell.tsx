import { useState } from "react";
import { NavLink, Outlet } from "react-router-dom";
import {
  BookOpen,
  CalendarRange,
  ChefHat,
  LayoutDashboard,
  MessageCircle,
  User,
  Utensils,
} from "lucide-react";
import { AssistantPanel } from "@/components/AssistantPanel";
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

  return (
    <div className="flex min-h-screen bg-slate-950 text-slate-200">
      {/* Sidebar */}
      <aside className="sticky top-0 hidden h-screen w-60 shrink-0 flex-col border-r border-slate-800 bg-slate-900/40 px-3 py-5 md:flex">
        <Brand />
        <nav className="mt-8 flex flex-col gap-1">
          {NAV.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                cn(
                  "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                  isActive
                    ? "bg-brand-500/15 text-brand-300"
                    : "text-slate-400 hover:bg-slate-800 hover:text-slate-100",
                )
              }
            >
              <Icon className="h-5 w-5" />
              {label}
            </NavLink>
          ))}
        </nav>
        <div className="mt-auto px-3 text-xs text-slate-600">
          NutriForge · v0.1
        </div>
      </aside>

      {/* Main column */}
      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar />
        {/* Extra bottom padding on mobile leaves room for the fixed bottom tab bar + FAB. */}
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 pt-6 pb-28 sm:px-6 md:pb-8 lg:px-8">
          <Outlet />
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
        className="fixed right-5 bottom-20 z-40 flex h-14 w-14 items-center justify-center rounded-full bg-brand-500 text-slate-950 shadow-lg shadow-brand-500/30 transition-transform hover:scale-105 hover:bg-brand-400 md:bottom-5"
      >
        <MessageCircle className="h-6 w-6" />
      </button>

      <AssistantPanel open={assistantOpen} onClose={() => setAssistantOpen(false)} />
    </div>
  );
}

function Brand() {
  return (
    <div className="flex items-center gap-2 px-3">
      <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-brand-500 text-slate-950">
        <Utensils className="h-5 w-5" />
      </span>
      <div className="leading-tight">
        <p className="text-base font-bold text-slate-100">NutriForge</p>
        <p className="text-xs text-slate-500">Enterprise Nutrition</p>
      </div>
    </div>
  );
}

function TopBar() {
  return (
    <header className="sticky top-0 z-20 flex h-14 items-center justify-between border-b border-slate-800 bg-slate-950/80 px-4 backdrop-blur sm:px-6">
      <div className="flex items-center gap-2 md:hidden">
        <span className="flex h-7 w-7 items-center justify-center rounded-md bg-brand-500 text-slate-950">
          <Utensils className="h-4 w-4" />
        </span>
        <span className="font-bold text-slate-100">NutriForge</span>
      </div>
      <div className="hidden md:block" />
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-2 rounded-lg border border-slate-800 bg-slate-900/60 px-3 py-1.5">
          <span className="flex h-6 w-6 items-center justify-center rounded-full bg-brand-500/20 text-xs font-bold text-brand-300">
            D
          </span>
          <span className="text-sm text-slate-300">demo-user</span>
        </div>
      </div>
    </header>
  );
}
