import * as React from "react";
import { cn } from "@/lib/utils";

export type InputProps = React.InputHTMLAttributes<HTMLInputElement>;

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, type = "text", inputMode, ...props }, ref) => (
    <input
      ref={ref}
      type={type}
      // Number fields get the mobile decimal keypad by default (more reliable than type=number
      // alone across mobile browsers); callers can still override via the inputMode prop.
      inputMode={inputMode ?? (type === "number" ? "decimal" : undefined)}
      className={cn(
        "h-10 w-full rounded-lg border border-slate-700 bg-slate-950/60 px-3 text-sm text-slate-100 placeholder:text-slate-500",
        "focus-visible:border-brand-500 focus-visible:ring-2 focus-visible:ring-brand-500/40 focus-visible:outline-none",
        "disabled:cursor-not-allowed disabled:opacity-50",
        className,
      )}
      {...props}
    />
  ),
);
Input.displayName = "Input";
