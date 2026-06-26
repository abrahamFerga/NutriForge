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
