import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

/**
 * The shared page header (#103) — a gradient icon chip, title, and subtitle, with an optional
 * right-aligned action slot. Used across pages so headings look consistent and modern.
 */
export function PageHeading({
  title,
  subtitle,
  icon: Icon,
  action,
}: {
  title: string;
  subtitle?: string;
  icon?: LucideIcon;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div className="flex items-center gap-3">
        {Icon ? (
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-brand-400/25 to-teal-500/10 text-brand-300 ring-1 ring-inset ring-brand-500/25">
            <Icon className="h-5 w-5" aria-hidden="true" />
          </span>
        ) : null}
        <div className="min-w-0">
          <h1 className="text-2xl font-bold tracking-tight text-slate-100">{title}</h1>
          {subtitle ? <p className="text-sm text-slate-400">{subtitle}</p> : null}
        </div>
      </div>
      {action ? <div className="flex items-center gap-2">{action}</div> : null}
    </div>
  );
}

/** A compact KPI tile for dashboard-style stat rows. */
export function StatTile({
  label,
  value,
  unit,
  icon: Icon,
  accent = false,
}: {
  label: string;
  value: string | number;
  unit?: string;
  icon: LucideIcon;
  accent?: boolean;
}) {
  return (
    <div className="nf-card nf-hover p-4">
      <div className="flex items-center justify-between">
        <span className="text-[0.7rem] font-medium tracking-wide text-slate-500 uppercase">
          {label}
        </span>
        <Icon
          className={accent ? "h-4 w-4 text-brand-400" : "h-4 w-4 text-slate-500"}
          aria-hidden="true"
        />
      </div>
      <div className="mt-2 flex items-baseline gap-1">
        <span
          className={
            accent
              ? "text-2xl font-bold nf-gradient-text"
              : "text-2xl font-bold text-slate-100"
          }
        >
          {value}
        </span>
        {unit ? <span className="text-xs text-slate-500">{unit}</span> : null}
      </div>
    </div>
  );
}
