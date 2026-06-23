import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Plus, Search, Trash2 } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState, LoadingState } from "@/components/StateMessage";
import {
  useAddDiaryEntry,
  useDebounced,
  useDeleteDiaryEntry,
  useDiary,
} from "@/hooks/useQueries";
import { foodsApi } from "@/lib/api";
import { queryKeys } from "@/lib/queryKeys";
import { MEAL_SLOTS, type FoodSummary, type MealSlot } from "@/lib/types";
import { cn, round, toIsoDate, today } from "@/lib/utils";

export function Diary() {
  const [date, setDate] = useState<string>(today());
  const [selectedFood, setSelectedFood] = useState<FoodSummary | null>(null);

  const diary = useDiary(date);
  const deleteEntry = useDeleteDiaryEntry(date);

  function shiftDate(deltaDays: number) {
    const d = new Date(date + "T00:00:00");
    d.setDate(d.getDate() + deltaDays);
    setDate(toIsoDate(d));
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-slate-100">Diary</h1>
          <p className="text-sm text-slate-400">Log and review your day</p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="icon" onClick={() => shiftDate(-1)} aria-label="Previous day">
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <Input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value || today())}
            className="w-44"
          />
          <Button variant="outline" size="icon" onClick={() => shiftDate(1)} aria-label="Next day">
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-5">
        {/* Search + log */}
        <div className="lg:col-span-2">
          <FoodSearch
            onPick={setSelectedFood}
            selectedFood={selectedFood}
            date={date}
            onLogged={() => setSelectedFood(null)}
          />
        </div>

        {/* Day entries */}
        <div className="space-y-4 lg:col-span-3">
          {diary.isLoading ? (
            <LoadingState label="Loading day…" />
          ) : diary.isError ? (
            <ErrorState error={diary.error} />
          ) : (
            <DayEntries
              entries={diary.data?.entries ?? []}
              consumed={diary.data?.consumed ?? null}
              target={diary.data?.target ?? null}
              onDelete={(id) => deleteEntry.mutate(id)}
              deletingId={deleteEntry.isPending ? deleteEntry.variables : undefined}
            />
          )}
        </div>
      </div>
    </div>
  );
}

// -------------------- Food search + log form --------------------

function FoodSearch({
  onPick,
  selectedFood,
  date,
  onLogged,
}: {
  onPick: (food: FoodSummary | null) => void;
  selectedFood: FoodSummary | null;
  date: string;
  onLogged: () => void;
}) {
  const [term, setTerm] = useState("");
  const debounced = useDebounced(term, 350);
  const enabled = debounced.trim().length >= 2;

  const search = useQuery({
    queryKey: queryKeys.foodSearch(debounced.trim()),
    queryFn: ({ signal }) => foodsApi.search(debounced.trim(), signal),
    enabled,
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Add food</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-slate-500" />
          <Input
            value={term}
            onChange={(e) => setTerm(e.target.value)}
            placeholder="Search foods…"
            className="pl-9"
          />
        </div>

        {selectedFood ? (
          <LogForm
            food={selectedFood}
            date={date}
            onCancel={() => onPick(null)}
            onLogged={onLogged}
          />
        ) : (
          <SearchResults
            enabled={enabled}
            loading={search.isLoading && enabled}
            error={search.error}
            results={search.data}
            onPick={onPick}
          />
        )}
      </CardContent>
    </Card>
  );
}

function SearchResults({
  enabled,
  loading,
  error,
  results,
  onPick,
}: {
  enabled: boolean;
  loading: boolean;
  error: unknown;
  results: FoodSummary[] | undefined;
  onPick: (food: FoodSummary) => void;
}) {
  if (!enabled) {
    return (
      <p className="py-6 text-center text-sm text-slate-500">
        Type at least 2 characters to search.
      </p>
    );
  }
  if (loading) {
    return (
      <div className="flex items-center justify-center gap-2 py-6 text-slate-400">
        <Spinner />
        <span className="text-sm">Searching…</span>
      </div>
    );
  }
  if (error) return <ErrorState error={error} />;
  if (!results || results.length === 0) {
    return (
      <p className="py-6 text-center text-sm text-slate-500">No foods found.</p>
    );
  }

  return (
    <ul className="max-h-96 divide-y divide-slate-800 overflow-y-auto rounded-lg border border-slate-800">
      {results.map((food) => (
        <li key={food.id}>
          <button
            onClick={() => onPick(food)}
            className="flex w-full items-center justify-between gap-3 px-3 py-2.5 text-left transition-colors hover:bg-slate-800"
          >
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-slate-100">
                {food.name}
              </p>
              <p className="truncate text-xs text-slate-500">
                {food.brand ?? "Generic"}
                <span className="mx-1.5 text-slate-700">·</span>
                <VerificationTag status={food.verificationStatus} />
              </p>
            </div>
            <div className="shrink-0 text-right">
              <p className="text-sm font-semibold text-slate-200">
                {round(food.kcalPer100g)} kcal
              </p>
              <p className="text-xs text-slate-500">per 100g</p>
            </div>
          </button>
        </li>
      ))}
    </ul>
  );
}

function VerificationTag({ status }: { status: string }) {
  const verified = /verif/i.test(status);
  return (
    <span
      className={cn(
        "rounded px-1.5 py-0.5 text-[10px] font-medium uppercase",
        verified
          ? "bg-emerald-500/15 text-emerald-300"
          : "bg-slate-700/60 text-slate-300",
      )}
    >
      {status}
    </span>
  );
}

const GRAMS_OPTION = "__grams__";

function LogForm({
  food,
  date,
  onCancel,
  onLogged,
}: {
  food: FoodSummary;
  date: string;
  onCancel: () => void;
  onLogged: () => void;
}) {
  const [mealSlot, setMealSlot] = useState<MealSlot>("Breakfast");
  const [portionId, setPortionId] = useState<string>(
    food.portions[0]?.id ?? GRAMS_OPTION,
  );
  const [quantity, setQuantity] = useState<string>(
    food.portions.length > 0 ? "1" : "100",
  );

  const add = useAddDiaryEntry();
  const isGrams = portionId === GRAMS_OPTION;

  const grams = useMemo(() => {
    const q = Number(quantity) || 0;
    if (isGrams) return q;
    const portion = food.portions.find((p) => p.id === portionId);
    return portion ? q * portion.grams : 0;
  }, [quantity, isGrams, portionId, food.portions]);

  const estKcal = round((grams / 100) * food.kcalPer100g);

  function submit() {
    const q = Number(quantity);
    if (!Number.isFinite(q) || q <= 0) return;
    add.mutate(
      {
        date,
        mealSlot,
        foodId: food.id,
        portionId: isGrams ? null : portionId,
        quantity: q,
      },
      { onSuccess: onLogged },
    );
  }

  return (
    <div className="space-y-3 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
      <div>
        <p className="text-sm font-semibold text-slate-100">{food.name}</p>
        <p className="text-xs text-slate-500">
          {food.brand ?? "Generic"} · {round(food.kcalPer100g)} kcal / 100g
        </p>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label htmlFor="meal">Meal</Label>
          <Select
            id="meal"
            value={mealSlot}
            onChange={(e) => setMealSlot(e.target.value as MealSlot)}
          >
            {MEAL_SLOTS.map((slot) => (
              <option key={slot} value={slot}>
                {slot}
              </option>
            ))}
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="portion">Portion</Label>
          <Select
            id="portion"
            value={portionId}
            onChange={(e) => {
              setPortionId(e.target.value);
              setQuantity(e.target.value === GRAMS_OPTION ? "100" : "1");
            }}
          >
            {food.portions.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name} ({p.grams}g)
              </option>
            ))}
            <option value={GRAMS_OPTION}>Grams</option>
          </Select>
        </div>
      </div>

      <div className="space-y-1">
        <Label htmlFor="qty">{isGrams ? "Grams" : "Quantity"}</Label>
        <Input
          id="qty"
          type="number"
          min={0}
          step={isGrams ? 10 : 0.25}
          value={quantity}
          onChange={(e) => setQuantity(e.target.value)}
        />
      </div>

      <div className="flex items-center justify-between text-xs text-slate-400">
        <span>≈ {round(grams)}g</span>
        <span className="font-medium text-brand-300">{estKcal} kcal</span>
      </div>

      {add.isError ? <ErrorState error={add.error} /> : null}

      <div className="flex gap-2">
        <Button onClick={submit} disabled={add.isPending} className="flex-1">
          {add.isPending ? <Spinner /> : <Plus className="h-4 w-4" />}
          Log
        </Button>
        <Button variant="outline" onClick={onCancel} disabled={add.isPending}>
          Cancel
        </Button>
      </div>
    </div>
  );
}

// -------------------- Day entries --------------------

interface MacroTotals {
  kcal: number;
  proteinG: number;
  fatG: number;
  carbG: number;
}

import type { DiaryEntry, TargetsDto } from "@/lib/types";

function DayEntries({
  entries,
  consumed,
  target,
  onDelete,
  deletingId,
}: {
  entries: DiaryEntry[];
  consumed: MacroTotals | null;
  target: TargetsDto | null;
  onDelete: (id: string) => void;
  deletingId?: string;
}) {
  const grouped = useMemo(() => {
    const map = new Map<MealSlot, DiaryEntry[]>();
    for (const slot of MEAL_SLOTS) map.set(slot, []);
    for (const e of entries) {
      const list = map.get(e.mealSlot);
      if (list) list.push(e);
    }
    for (const list of map.values()) {
      list.sort((a, b) => a.sequence - b.sequence);
    }
    return map;
  }, [entries]);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Today&apos;s entries</CardTitle>
      </CardHeader>
      <CardContent className="space-y-5">
        {entries.length === 0 ? (
          <p className="py-8 text-center text-sm text-slate-500">
            Nothing logged yet. Search for a food to get started.
          </p>
        ) : (
          MEAL_SLOTS.map((slot) => {
            const list = grouped.get(slot) ?? [];
            if (list.length === 0) return null;
            return (
              <div key={slot}>
                <h3 className="mb-2 text-xs font-semibold tracking-wide text-slate-400 uppercase">
                  {slot}
                </h3>
                <div className="overflow-hidden rounded-lg border border-slate-800">
                  <table className="w-full text-sm">
                    <tbody className="divide-y divide-slate-800">
                      {list.map((e) => (
                        <tr key={e.id} className="hover:bg-slate-800/40">
                          <td className="px-3 py-2">
                            <p className="font-medium text-slate-100">
                              {e.foodName}
                            </p>
                            <p className="text-xs text-slate-500">
                              {round(e.quantity, 2)} × {e.portionName} ·{" "}
                              {round(e.grams)}g
                            </p>
                          </td>
                          <td className="px-3 py-2 text-right whitespace-nowrap text-slate-300">
                            <span className="font-semibold text-slate-100">
                              {round(e.kcal)}
                            </span>{" "}
                            kcal
                            <p className="text-xs text-slate-500">
                              P{round(e.proteinG)} · F{round(e.fatG)} · C
                              {round(e.carbG)}
                            </p>
                          </td>
                          <td className="w-10 px-2 py-2 text-right">
                            <Button
                              variant="ghost"
                              size="icon"
                              aria-label="Delete entry"
                              disabled={deletingId === e.id}
                              onClick={() => onDelete(e.id)}
                              className="text-slate-500 hover:text-red-400"
                            >
                              {deletingId === e.id ? (
                                <Spinner />
                              ) : (
                                <Trash2 className="h-4 w-4" />
                              )}
                            </Button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            );
          })
        )}

        {consumed ? <TotalsRow consumed={consumed} target={target} /> : null}
      </CardContent>
    </Card>
  );
}

function TotalsRow({
  consumed,
  target,
}: {
  consumed: MacroTotals;
  target: TargetsDto | null;
}) {
  const cell = (consumedV: number, targetV: number | undefined, unit: string) => (
    <td className="px-3 py-2 text-right">
      <span className="font-semibold text-slate-100">{round(consumedV)}</span>
      {target ? (
        <span className="text-slate-500">
          {" / "}
          {round(targetV ?? 0)}
        </span>
      ) : null}
      <span className="text-slate-500">{unit}</span>
    </td>
  );

  return (
    <div className="overflow-hidden rounded-lg border border-brand-500/30 bg-brand-500/5">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-xs text-slate-500">
            <th className="px-3 py-1.5 text-left font-medium">Day total</th>
            <th className="px-3 py-1.5 text-right font-medium">kcal</th>
            <th className="px-3 py-1.5 text-right font-medium">Protein</th>
            <th className="px-3 py-1.5 text-right font-medium">Fat</th>
            <th className="px-3 py-1.5 text-right font-medium">Carbs</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td className="px-3 py-2 text-left font-medium text-slate-300">
              {target ? "vs target" : "logged"}
            </td>
            {cell(consumed.kcal, target?.kcal, "")}
            {cell(consumed.proteinG, target?.proteinG, "g")}
            {cell(consumed.fatG, target?.fatG, "g")}
            {cell(consumed.carbG, target?.carbG, "g")}
          </tr>
        </tbody>
      </table>
    </div>
  );
}
