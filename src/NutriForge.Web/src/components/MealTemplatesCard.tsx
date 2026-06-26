import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookmarkPlus, UtensilsCrossed, X } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/StateMessage";
import { mealTemplatesApi } from "@/lib/api";
import type { MealSlot } from "@/lib/types";
import { round } from "@/lib/utils";

const TEMPLATES_KEY = ["mealTemplates"] as const;

/** Saved meals (#70): one-tap log a whole combination of foods, and save the current meal as one. */
export function MealTemplatesCard({
  date,
  mealSlot,
  onLogged,
}: {
  date: string;
  mealSlot: MealSlot;
  onLogged: () => void;
}) {
  const qc = useQueryClient();
  const [naming, setNaming] = useState(false);
  const [name, setName] = useState("");

  const templates = useQuery({ queryKey: TEMPLATES_KEY, queryFn: () => mealTemplatesApi.list() });
  const invalidate = () => qc.invalidateQueries({ queryKey: TEMPLATES_KEY });

  const log = useMutation({
    mutationFn: (id: string) => mealTemplatesApi.log(id, date, mealSlot),
    onSuccess: onLogged,
  });
  const save = useMutation({
    mutationFn: () => mealTemplatesApi.saveFromDay(name.trim(), date, mealSlot),
    onSuccess: () => {
      setNaming(false);
      setName("");
      invalidate();
    },
  });
  const remove = useMutation({
    mutationFn: (id: string) => mealTemplatesApi.remove(id),
    onSuccess: invalidate,
  });

  const items = templates.data ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <UtensilsCrossed className="h-4 w-4 text-brand-400" />
          Saved meals
          <span className="text-xs font-normal text-slate-500">→ {mealSlot}</span>
          <button
            onClick={() => setNaming((v) => !v)}
            className="ml-auto flex items-center gap-1 text-xs font-normal text-slate-400 hover:text-brand-300"
          >
            <BookmarkPlus className="h-3.5 w-3.5" />
            Save this {mealSlot.toLowerCase()}
          </button>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {naming ? (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              if (name.trim().length > 0) save.mutate();
            }}
            className="flex gap-2"
          >
            <Input
              autoFocus
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={`Name this ${mealSlot.toLowerCase()} (e.g. "Usual breakfast")`}
              maxLength={120}
            />
            <Button type="submit" size="sm" disabled={save.isPending || name.trim().length === 0}>
              {save.isPending ? <Spinner /> : "Save"}
            </Button>
          </form>
        ) : null}

        {save.isError ? <ErrorState error={save.error} /> : null}
        {log.isError ? <ErrorState error={log.error} /> : null}

        {items.length === 0 ? (
          <p className="text-sm text-slate-500">
            No saved meals yet. Log a meal, then “Save this {mealSlot.toLowerCase()}” to reuse it in one tap.
          </p>
        ) : (
          <div className="flex flex-wrap gap-2">
            {items.map((t) => (
              <div
                key={t.id}
                className="flex items-center gap-1 rounded-full border border-slate-700 bg-slate-900/60 pr-1 text-sm"
              >
                <button
                  onClick={() => log.mutate(t.id)}
                  disabled={log.isPending}
                  className="rounded-full py-1 pl-3 pr-1 text-slate-100 hover:text-brand-300 disabled:opacity-50"
                  title={`Log ${t.items.length} food${t.items.length === 1 ? "" : "s"} to ${mealSlot}`}
                >
                  {t.name}
                  <span className="ml-1.5 text-xs text-slate-500">{round(t.kcal)} kcal</span>
                </button>
                <button
                  onClick={() => remove.mutate(t.id)}
                  disabled={remove.isPending}
                  aria-label={`Delete ${t.name}`}
                  className="rounded-full p-1 text-slate-500 hover:text-red-400"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
