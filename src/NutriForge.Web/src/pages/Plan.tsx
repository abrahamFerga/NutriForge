import { useEffect, useMemo, useRef, useState } from "react";
import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import {
  AlertTriangle,
  Check,
  ChefHat,
  FileDown,
  ShoppingCart,
  Sparkles,
} from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/StateMessage";
import { ShoppingList } from "@/components/ShoppingList";
import { PantryPanel } from "@/components/PantryPanel";
import { dietPlansApi } from "@/lib/api";
import { queryKeys } from "@/lib/queryKeys";
import type {
  AdherencePoint,
  CreateDietPlanRequest,
  DietPlanDto,
  DietPlanSlot,
  DietSlug,
  ShoppingListDto,
} from "@/lib/types";
import { round } from "@/lib/utils";

const DIET_SLUGS: { value: DietSlug | ""; label: string }[] = [
  { value: "", label: "No preference" },
  { value: "vegan", label: "Vegan" },
  { value: "vegetarian", label: "Vegetarian" },
  { value: "high-protein", label: "High protein" },
];

export function Plan() {
  const [planId, setPlanId] = useState<string | null>(null);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-100">Diet plan</h1>
        <p className="text-sm text-slate-400">
          Generate a meal plan, then turn it into a shopping list
        </p>
      </div>

      <div className="grid gap-6 lg:grid-cols-5">
        <div className="space-y-6 lg:col-span-2">
          <PlanForm onCreated={setPlanId} />
          <PantryPanel />
        </div>

        <div className="lg:col-span-3">
          {planId ? (
            <PlanResult key={planId} planId={planId} />
          ) : (
            <Card>
              <CardContent className="py-16 text-center text-sm text-slate-500">
                Configure your plan and generate to see results here.
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}

// -------------------- Generation form --------------------

function PlanForm({ onCreated }: { onCreated: (id: string) => void }) {
  const [dietSlug, setDietSlug] = useState<DietSlug | "">("");
  const [kcalTarget, setKcalTarget] = useState("");
  const [maxPrep, setMaxPrep] = useState("");
  const [days, setDays] = useState("7");
  const [eaters, setEaters] = useState("1");
  const [desire, setDesire] = useState("");

  const create = useMutation({
    mutationFn: () => {
      const body: CreateDietPlanRequest = {
        horizonDays: Number(days) || 7,
        eaters: Math.min(9, Math.max(1, Number(eaters) || 1)),
      };
      if (dietSlug) body.dietSlug = dietSlug;
      if (kcalTarget.trim()) body.kcalTarget = Number(kcalTarget);
      if (maxPrep.trim()) body.maxPrepMinutes = Number(maxPrep);
      if (desire.trim()) body.desire = desire.trim();
      return dietPlansApi.create(body);
    },
    onSuccess: (plan) => onCreated(plan.id),
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Generate a plan</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-1">
          <Label htmlFor="diet-slug">Diet</Label>
          <Select
            id="diet-slug"
            value={dietSlug}
            onChange={(e) => setDietSlug(e.target.value as DietSlug | "")}
          >
            {DIET_SLUGS.map((d) => (
              <option key={d.value} value={d.value}>
                {d.label}
              </option>
            ))}
          </Select>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor="kcal-target">Calorie target (optional)</Label>
            <Input
              id="kcal-target"
              type="number"
              min={0}
              value={kcalTarget}
              onChange={(e) => setKcalTarget(e.target.value)}
              placeholder="e.g. 2200"
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="max-prep">Max prep min (optional)</Label>
            <Input
              id="max-prep"
              type="number"
              min={0}
              value={maxPrep}
              onChange={(e) => setMaxPrep(e.target.value)}
              placeholder="e.g. 30"
            />
          </div>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor="days">Days</Label>
            <Input
              id="days"
              type="number"
              min={1}
              max={30}
              value={days}
              onChange={(e) => setDays(e.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="eaters">People</Label>
            <Input
              id="eaters"
              type="number"
              min={1}
              max={9}
              value={eaters}
              onChange={(e) => setEaters(e.target.value)}
            />
            <p className="text-xs text-slate-500">Cook &amp; shop for this many.</p>
          </div>
        </div>

        <div className="space-y-1">
          <Label htmlFor="desire">Free-text desire (optional)</Label>
          <textarea
            id="desire"
            value={desire}
            onChange={(e) => setDesire(e.target.value)}
            rows={2}
            placeholder="e.g. 'Mediterranean, lots of fish'"
            className="flex w-full rounded-lg border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus-visible:border-brand-500 focus-visible:ring-2 focus-visible:ring-brand-500/30 focus-visible:outline-none"
          />
          <p className="text-xs text-slate-500">
            Free-text needs an LLM key; the structured fields always work.
          </p>
        </div>

        {create.isError ? <ErrorState error={create.error} /> : null}

        <Button
          onClick={() => create.mutate()}
          disabled={create.isPending}
          className="w-full"
        >
          {create.isPending ? <Spinner /> : <Sparkles className="h-4 w-4" />}
          Generate plan
        </Button>
      </CardContent>
    </Card>
  );
}

// -------------------- Result: poll until ready --------------------

const POLL_INTERVAL_MS = 1000;
const MAX_POLLS = 30;

function PlanResult({ planId }: { planId: string }) {
  // Count settled fetches so we can cap polling at MAX_POLLS.
  const pollsRef = useRef(0);
  const [pollsCapped, setPollsCapped] = useState(false);

  const plan = useQuery({
    queryKey: queryKeys.dietPlan(planId),
    queryFn: ({ signal }) => dietPlansApi.get(planId, signal),
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      if (status && status !== "Generating") return false;
      if (pollsRef.current >= MAX_POLLS) return false;
      return POLL_INTERVAL_MS;
    },
  });

  // Increment the poll counter each time a fetch settles while generating.
  useEffect(() => {
    if (
      plan.dataUpdatedAt > 0 &&
      plan.data?.status === "Generating" &&
      pollsRef.current < MAX_POLLS
    ) {
      pollsRef.current += 1;
      if (pollsRef.current >= MAX_POLLS) setPollsCapped(true);
    }
  }, [plan.dataUpdatedAt, plan.data?.status]);

  if (plan.isLoading) {
    return <GeneratingCard />;
  }
  if (plan.isError) {
    return (
      <Card>
        <CardContent className="py-8">
          <ErrorState error={plan.error} />
        </CardContent>
      </Card>
    );
  }

  const data = plan.data;
  if (!data) return null;

  if (data.status === "Generating") {
    return <GeneratingCard timedOut={pollsCapped} />;
  }

  if (data.status === "Infeasible") {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Plan infeasible</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex items-start gap-3 rounded-lg border border-amber-900/60 bg-amber-950/40 px-4 py-3 text-sm text-amber-200">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>
              {data.message ??
                "Couldn't build a plan. Add recipes or relax constraints."}
            </span>
          </div>
        </CardContent>
      </Card>
    );
  }

  return <ReadyPlan plan={data} />;
}

function GeneratingCard({ timedOut }: { timedOut?: boolean }) {
  return (
    <Card>
      <CardContent className="flex flex-col items-center justify-center gap-3 py-16 text-center">
        {timedOut ? (
          <p className="text-sm text-amber-300">
            Still generating — this is taking longer than expected. Try
            refreshing in a moment.
          </p>
        ) : (
          <>
            <Spinner className="h-6 w-6 text-brand-400" />
            <p className="text-sm text-slate-400">Generating your plan…</p>
          </>
        )}
      </CardContent>
    </Card>
  );
}

// -------------------- Ready plan view --------------------

function ReadyPlan({ plan }: { plan: DietPlanDto }) {
  const qc = useQueryClient();
  const [shoppingList, setShoppingList] = useState<ShoppingListDto | null>(null);

  const days = useMemo(() => groupByDay(plan.slots), [plan.slots]);
  const dayCount = days.length;
  const eaters = plan.eaters ?? 1;
  // achieved* are already PER-EATER, PER-DAY averages from the server. Show them as-is: do NOT
  // divide by dayCount again (that was a bug — it showed ~target/days), and NEVER multiply by
  // eaters (these describe one person's day; only cook totals + the shopping list scale by people).

  const accept = useMutation({
    mutationFn: () => dietPlansApi.accept(plan.id),
    onSuccess: (updated) => {
      qc.setQueryData(queryKeys.dietPlan(plan.id), updated);
    },
  });

  const genList = useMutation({
    mutationFn: () => dietPlansApi.shoppingList(plan.id),
    onSuccess: (list) => setShoppingList(list),
  });

  const downloadPdf = useMutation({
    mutationFn: () => dietPlansApi.pdf(plan.id),
    onSuccess: (blob) => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "nutriforge-meal-plan.pdf";
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    },
  });

  const adherence = useQuery({
    queryKey: queryKeys.dietPlanAdherence(plan.id),
    queryFn: () => dietPlansApi.adherence(plan.id),
    enabled: plan.status === "Accepted",
  });

  const isAccepted = plan.status === "Accepted";

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>
            Plan ready
            {isAccepted ? (
              <span className="ml-2 rounded bg-emerald-500/15 px-1.5 py-0.5 text-[10px] font-medium text-emerald-300">
                accepted
              </span>
            ) : null}
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex items-center justify-between">
            <p className="text-xs font-medium tracking-wide text-slate-500 uppercase">
              Per person · per day
            </p>
            {eaters > 1 ? (
              <span className="rounded bg-brand-500/15 px-2 py-0.5 text-xs font-medium text-brand-300">
                {eaters} people
              </span>
            ) : null}
          </div>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Stat
              label="Daily avg kcal"
              value={round(plan.achievedKcal)}
              sub={`target ${round(plan.targetKcal)}`}
            />
            <Stat label="Protein/day" value={`${round(plan.achievedProteinG)}g`} />
            <Stat label="Fat/day" value={`${round(plan.achievedFatG)}g`} />
            <Stat label="Carbs/day" value={`${round(plan.achievedCarbG)}g`} />
          </div>

          {plan.message ? (
            <p className="rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2 text-sm text-slate-300">
              {plan.message}
            </p>
          ) : null}

          {accept.isError ? <ErrorState error={accept.error} /> : null}
          {genList.isError ? <ErrorState error={genList.error} /> : null}
          {downloadPdf.isError ? <ErrorState error={downloadPdf.error} /> : null}

          <div className="flex flex-wrap gap-2">
            {!isAccepted ? (
              <Button onClick={() => accept.mutate()} disabled={accept.isPending}>
                {accept.isPending ? <Spinner /> : <Check className="h-4 w-4" />}
                Accept plan
              </Button>
            ) : null}
            <Button
              variant={isAccepted ? "primary" : "outline"}
              onClick={() => genList.mutate()}
              disabled={genList.isPending}
            >
              {genList.isPending ? (
                <Spinner />
              ) : (
                <ShoppingCart className="h-4 w-4" />
              )}
              Generate shopping list
            </Button>
            <Button
              variant="outline"
              onClick={() => downloadPdf.mutate()}
              disabled={downloadPdf.isPending}
            >
              {downloadPdf.isPending ? <Spinner /> : <FileDown className="h-4 w-4" />}
              Download PDF
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Meal plan</CardTitle>
        </CardHeader>
        <CardContent className="space-y-5">
          {days.map(({ day, slots }) => (
            <div key={day}>
              <h3 className="mb-2 text-xs font-semibold tracking-wide text-slate-400 uppercase">
                Day {day}
              </h3>
              <div className="overflow-x-auto rounded-lg border border-slate-800">
                <table className="w-full text-sm">
                  <tbody className="divide-y divide-slate-800">
                    {slots.map((slot, i) => (
                      <tr key={`${slot.mealSlot}-${i}`}>
                        <td className="px-3 py-2 text-xs font-medium tracking-wide text-slate-500 uppercase">
                          {slot.mealSlot}
                        </td>
                        <td className="px-3 py-2">
                          <span className="flex items-center gap-1.5 text-slate-100">
                            <ChefHat className="h-3.5 w-3.5 text-slate-500" />
                            {slot.recipeName}
                          </span>
                          <span className="text-xs text-slate-500">
                            {round(slot.servings, 2)} serving
                            {slot.servings === 1 ? "" : "s"}
                            {eaters > 1 ? "/person" : ""}
                            {eaters > 1
                              ? ` · cook ${round(slot.servings * eaters, 2)} total`
                              : ""}
                          </span>
                        </td>
                        <td className="px-3 py-2 text-right whitespace-nowrap text-slate-300">
                          <span className="font-semibold text-slate-100">
                            {round(slot.kcal)}
                          </span>{" "}
                          kcal
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ))}
        </CardContent>
      </Card>

      {shoppingList ? (
        <Card>
          <CardHeader>
            <CardTitle>Shopping list</CardTitle>
            <p className="text-xs text-slate-500">
              Totals for {dayCount} {dayCount === 1 ? "day" : "days"} ×{" "}
              {eaters} {eaters === 1 ? "person" : "people"} ={" "}
              {dayCount * eaters} person-days
            </p>
          </CardHeader>
          <CardContent>
            <ShoppingList list={shoppingList} />
          </CardContent>
        </Card>
      ) : null}

      {isAccepted ? (
        <Card>
          <CardHeader>
            <CardTitle>Adherence</CardTitle>
          </CardHeader>
          <CardContent>
            {adherence.isLoading ? (
              <div className="flex items-center justify-center gap-2 py-4 text-slate-400">
                <Spinner />
                <span className="text-sm">Loading adherence…</span>
              </div>
            ) : adherence.isError ? (
              <ErrorState error={adherence.error} />
            ) : (
              <AdherenceReadout points={adherence.data ?? []} />
            )}
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
}

function AdherenceReadout({ points }: { points: AdherencePoint[] }) {
  if (points.length === 0) {
    return (
      <p className="py-2 text-center text-sm text-slate-500">
        No adherence data yet — log some meals to track it.
      </p>
    );
  }
  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-xs text-slate-500">
            <th className="px-3 py-2 text-left font-medium">Date</th>
            <th className="px-3 py-2 text-right font-medium">Planned</th>
            <th className="px-3 py-2 text-right font-medium">Logged</th>
            <th className="px-3 py-2 text-right font-medium">Adherence</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-800">
          {points.map((p) => (
            <tr key={p.date}>
              <td className="px-3 py-2 text-slate-300">{p.date}</td>
              <td className="px-3 py-2 text-right text-slate-400">
                {round(p.plannedKcal)}
              </td>
              <td className="px-3 py-2 text-right text-slate-400">
                {round(p.loggedKcal)}
              </td>
              <td className="px-3 py-2 text-right font-semibold text-brand-300">
                {round(p.adherencePct)}%
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Stat({
  label,
  value,
  sub,
}: {
  label: string;
  value: string | number;
  sub?: string;
}) {
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2">
      <p className="text-xs text-slate-500">{label}</p>
      <p className="text-lg font-bold text-slate-100">{value}</p>
      {sub ? <p className="text-[11px] text-slate-500">{sub}</p> : null}
    </div>
  );
}

function groupByDay(
  slots: DietPlanSlot[],
): { day: number; slots: DietPlanSlot[] }[] {
  const map = new Map<number, DietPlanSlot[]>();
  for (const slot of slots) {
    const list = map.get(slot.day);
    if (list) list.push(slot);
    else map.set(slot.day, [slot]);
  }
  return [...map.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([day, daySlots]) => ({ day, slots: daySlots }));
}
