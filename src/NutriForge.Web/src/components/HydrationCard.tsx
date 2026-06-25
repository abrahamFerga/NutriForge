import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, Droplets, Minus, Pencil } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/StateMessage";
import { hydrationApi } from "@/lib/api";
import type { HydrationDay } from "@/lib/types";

const KEY = ["hydration", "today"] as const;
const QUICK_ADDS = [250, 500, 750] as const;

/** Log today's water against a daily goal (#72). */
export function HydrationCard() {
  const qc = useQueryClient();
  const day = useQuery({ queryKey: KEY, queryFn: () => hydrationApi.get() });

  const [editingGoal, setEditingGoal] = useState(false);
  const [goalDraft, setGoalDraft] = useState("");

  // The POST/PUT both return the updated day, so write it straight into the cache (no refetch).
  const onDay = (d: HydrationDay) => qc.setQueryData(KEY, d);

  const add = useMutation({ mutationFn: (ml: number) => hydrationApi.add(ml), onSuccess: onDay });
  const saveGoal = useMutation({
    mutationFn: (goalMl: number) => hydrationApi.setGoal(goalMl),
    onSuccess: (d) => {
      onDay(d);
      setEditingGoal(false);
    },
  });

  const ml = day.data?.ml ?? 0;
  const goalMl = day.data?.goalMl ?? 2500;
  const pct = goalMl > 0 ? Math.min(100, Math.round((ml / goalMl) * 100)) : 0;
  const reached = ml >= goalMl && goalMl > 0;
  const busy = add.isPending || day.isLoading;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Droplets className="h-4 w-4 text-sky-400" />
          Water
        </CardTitle>
        <p className="text-xs text-slate-400">
          <span className={reached ? "text-emerald-400" : "text-slate-200"}>
            {(ml / 1000).toFixed(2)} L
          </span>{" "}
          of {(goalMl / 1000).toFixed(2)} L goal
          {reached ? " · goal reached 🎉" : ` · ${pct}%`}
        </p>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Progress */}
        <div
          className="h-3 w-full overflow-hidden rounded-full bg-slate-800"
          role="progressbar"
          aria-valuenow={pct}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label="Hydration progress"
        >
          <div
            className={`h-full rounded-full transition-all ${reached ? "bg-emerald-500" : "bg-sky-500"}`}
            style={{ width: `${pct}%` }}
          />
        </div>

        {/* Quick add + undo */}
        <div className="flex flex-wrap items-center gap-2">
          {QUICK_ADDS.map((amount) => (
            <Button
              key={amount}
              variant="outline"
              size="sm"
              disabled={busy}
              onClick={() => add.mutate(amount)}
            >
              +{amount} ml
            </Button>
          ))}
          <Button
            variant="ghost"
            size="sm"
            disabled={busy || ml === 0}
            onClick={() => add.mutate(-250)}
            aria-label="Undo 250 ml"
            className="text-slate-400"
          >
            <Minus className="h-4 w-4" />
            250
          </Button>
          {add.isPending ? <Spinner /> : null}
        </div>

        {add.isError ? <ErrorState error={add.error} /> : null}

        {/* Goal editor */}
        {editingGoal ? (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              const g = Number(goalDraft);
              if (Number.isFinite(g) && g > 0) saveGoal.mutate(Math.round(g));
            }}
            className="flex items-end gap-2"
          >
            <div className="space-y-1">
              <label className="text-xs text-slate-400" htmlFor="hydration-goal">
                Daily goal (ml)
              </label>
              <Input
                id="hydration-goal"
                type="number"
                inputMode="numeric"
                min={250}
                step={250}
                autoFocus
                value={goalDraft}
                onChange={(e) => setGoalDraft(e.target.value)}
                placeholder={String(goalMl)}
                className="w-32"
              />
            </div>
            <Button type="submit" size="sm" disabled={saveGoal.isPending || goalDraft.trim() === ""}>
              {saveGoal.isPending ? <Spinner /> : <Check className="h-4 w-4" />}
              Save
            </Button>
            <Button type="button" variant="ghost" size="sm" onClick={() => setEditingGoal(false)}>
              Cancel
            </Button>
          </form>
        ) : (
          <button
            onClick={() => {
              setGoalDraft(String(goalMl));
              setEditingGoal(true);
            }}
            className="flex items-center gap-1 text-xs text-slate-400 hover:text-sky-300"
          >
            <Pencil className="h-3 w-3" />
            Edit goal
          </button>
        )}

        {saveGoal.isError ? <ErrorState error={saveGoal.error} /> : null}
      </CardContent>
    </Card>
  );
}
