import { clamp, round } from "@/lib/utils";
import { cn } from "@/lib/utils";

interface MacroBarProps {
  label: string;
  consumed: number;
  target: number;
  unit?: string;
  colorClass?: string;
}

export function MacroBar({
  label,
  consumed,
  target,
  unit = "g",
  colorClass = "bg-brand-500",
}: MacroBarProps) {
  const pct = target > 0 ? clamp((consumed / target) * 100, 0, 100) : 0;
  const over = target > 0 && consumed > target;

  return (
    <div>
      <div className="mb-1.5 flex items-baseline justify-between">
        <span className="text-sm font-medium text-slate-300">{label}</span>
        <span className="text-sm text-slate-400">
          <span className="font-semibold text-slate-100">
            {round(consumed)}
          </span>
          {" / "}
          {round(target)}
          {unit}
        </span>
      </div>
      <div className="h-2.5 w-full overflow-hidden rounded-full bg-slate-800">
        <div
          className={cn(
            "h-full rounded-full transition-all",
            over ? "bg-amber-500" : colorClass,
          )}
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  );
}
