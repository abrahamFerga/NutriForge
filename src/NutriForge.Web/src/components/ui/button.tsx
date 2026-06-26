import * as React from "react";
import { cn } from "@/lib/utils";

type Variant = "primary" | "secondary" | "outline" | "ghost" | "destructive";
type Size = "sm" | "md" | "icon";

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
}

const base =
  "inline-flex items-center justify-center gap-2 rounded-xl font-medium transition-all duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/60 disabled:pointer-events-none disabled:opacity-50 active:translate-y-0 whitespace-nowrap";

const variants: Record<Variant, string> = {
  primary:
    "bg-gradient-to-b from-brand-400 to-brand-600 text-slate-950 shadow-lg shadow-brand-500/25 hover:from-brand-300 hover:to-brand-500 hover:-translate-y-0.5",
  secondary:
    "border border-slate-700/60 bg-slate-800 text-slate-100 hover:bg-slate-700 hover:-translate-y-0.5",
  outline:
    "border border-slate-700 bg-slate-900/40 text-slate-100 hover:bg-slate-800 hover:border-slate-600",
  ghost: "bg-transparent text-slate-300 hover:bg-slate-800 hover:text-slate-100",
  destructive:
    "bg-gradient-to-b from-red-500 to-red-600 text-white shadow-lg shadow-red-600/25 hover:from-red-400 hover:to-red-500 hover:-translate-y-0.5",
};

const sizes: Record<Size, string> = {
  sm: "h-8 px-3 text-sm",
  md: "h-10 px-4 text-sm",
  icon: "h-9 w-9 p-0",
};

/** Compose the button's class string — useful for styling links as buttons. */
export function buttonClasses(
  variant: Variant = "primary",
  size: Size = "md",
  className?: string,
): string {
  return cn(base, variants[variant], sizes[size], className);
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = "primary", size = "md", type, ...props }, ref) => (
    <button
      ref={ref}
      type={type ?? "button"}
      className={buttonClasses(variant, size, className)}
      {...props}
    />
  ),
);
Button.displayName = "Button";
