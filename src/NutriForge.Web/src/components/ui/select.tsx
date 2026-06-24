import * as React from "react";
import { cn } from "@/lib/utils";

export type SelectProps = React.SelectHTMLAttributes<HTMLSelectElement>;

export const Select = React.forwardRef<HTMLSelectElement, SelectProps>(
  ({ className, children, ...props }, ref) => (
    <select
      ref={ref}
      className={cn(
        "h-10 w-full appearance-none rounded-lg border border-slate-700 bg-slate-950/60 px-3 text-sm text-slate-100",
        // Render the native option popup in dark mode so it isn't unreadable light-on-light;
        // also set the option colors explicitly for browsers (Chrome/Edge on Windows) that honor them.
        "[color-scheme:dark] [&>option]:bg-slate-900 [&>option]:text-slate-100",
        "focus-visible:border-brand-500 focus-visible:ring-2 focus-visible:ring-brand-500/40 focus-visible:outline-none",
        "disabled:cursor-not-allowed disabled:opacity-50",
        // chevron
        "bg-[length:1rem] bg-[right_0.6rem_center] bg-no-repeat pr-9",
        "bg-[url('data:image/svg+xml;utf8,<svg xmlns=%22http://www.w3.org/2000/svg%22 fill=%22none%22 viewBox=%220 0 24 24%22 stroke=%22%2394a3b8%22 stroke-width=%222%22><path stroke-linecap=%22round%22 stroke-linejoin=%22round%22 d=%22M19 9l-7 7-7-7%22/></svg>')]",
        className,
      )}
      {...props}
    >
      {children}
    </select>
  ),
);
Select.displayName = "Select";
