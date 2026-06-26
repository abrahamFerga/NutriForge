import { useEffect, useMemo, useRef, useState } from "react";
import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import {
  AlertTriangle,
  Bookmark,
  Check,
  ChefHat,
  CookingPot,
  FileDown,
  Flame,
  Plus,
  ShoppingCart,
  Sparkles,
  Users,
  X,
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
import { dietPlansApi, dietTemplatesApi, householdApi } from "@/lib/api";
import { queryKeys } from "@/lib/queryKeys";
import type {
  AdherencePoint,
  BatchCookPlanDto,
  BatchCookStep,
  CreateDietPlanRequest,
  DietPlanDto,
  DietPlanSlot,
  DietSlug,
  DietTemplate,
  HouseholdMember,
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

/** A row in the "Cooking for" editor — an additional person besides the owner ("You"). */
type MemberRow = {
  /** Set when this row mirrors a saved household member (#100). */
  id?: string;
  name: string;
  mode: "same" | "own";
  kcal: string;
  /** Carried through from a saved member so a save doesn't wipe it (the form doesn't edit it). */
  relationship?: string | null;
};

function memberToRow(m: HouseholdMember): MemberRow {
  return {
    id: m.id,
    name: m.name,
    mode: m.targetKcal != null ? "own" : "same",
    kcal: m.targetKcal != null ? String(m.targetKcal) : "",
    relationship: m.relationship,
  };
}

function PlanForm({ onCreated }: { onCreated: (id: string) => void }) {
  const queryClient = useQueryClient();
  const [dietSlug, setDietSlug] = useState<DietSlug | "">("");
  const [kcalTarget, setKcalTarget] = useState("");
  const [maxPrep, setMaxPrep] = useState("");
  const [days, setDays] = useState("7");
  const [blockSize, setBlockSize] = useState("1");
  const [members, setMembers] = useState<MemberRow[]>([]);
  const [desire, setDesire] = useState("");
  const [presetName, setPresetName] = useState("");

  // Saved diet presets (#102): the user keeps making the same kind of plan, so load/save the parameters.
  const templates = useQuery({ queryKey: queryKeys.dietTemplates, queryFn: dietTemplatesApi.list });

  function applyTemplate(t: DietTemplate) {
    setDietSlug((t.dietSlug as DietSlug | null) ?? "");
    setKcalTarget(t.kcalTarget != null ? String(t.kcalTarget) : "");
    setMaxPrep(t.maxPrepMinutes != null ? String(t.maxPrepMinutes) : "");
    setDays(String(t.horizonDays));
    setBlockSize(String(t.blockSize));
    setDesire(t.desire ?? "");
  }

  const saveTemplate = useMutation({
    mutationFn: (name: string) =>
      dietTemplatesApi.add({
        name,
        dietSlug: dietSlug || null,
        kcalTarget: kcalTarget.trim() ? Number(kcalTarget) : null,
        maxPrepMinutes: maxPrep.trim() ? Number(maxPrep) : null,
        horizonDays: Number(days) || 7,
        blockSize: Number(blockSize) || 1,
        desire: desire.trim() || null,
      }),
    onSuccess: () => {
      setPresetName("");
      queryClient.invalidateQueries({ queryKey: queryKeys.dietTemplates });
    },
  });

  const generateTemplate = useMutation({
    mutationFn: (id: string) => dietTemplatesApi.generate(id),
    onSuccess: (plan) => onCreated(plan.id),
  });

  const deleteTemplate = useMutation({
    mutationFn: (id: string) => dietTemplatesApi.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.dietTemplates }),
  });

  // Pre-fill "Cooking for" from the saved household so the partner/children the user always cooks for
  // are there without re-adding them (#100). Seed once, after the rows are still untouched-empty.
  const household = useQuery({ queryKey: queryKeys.household, queryFn: householdApi.list });
  const seeded = useRef(false);
  useEffect(() => {
    if (!seeded.current && household.data && household.data.length > 0) {
      seeded.current = true;
      setMembers(household.data.map(memberToRow));
    }
  }, [household.data]);

  function updateMember(i: number, patch: Partial<MemberRow>) {
    setMembers((prev) => prev.map((m, idx) => (idx === i ? { ...m, ...patch } : m)));
  }

  // Persist the current "Cooking for" rows as the user's saved household: add new ones, update changed
  // ones, and delete any saved member they removed from the form.
  const saveHousehold = useMutation({
    mutationFn: async () => {
      const saved = household.data ?? [];
      const named = members.filter((m) => m.name.trim().length > 0);
      const keptIds = new Set(named.map((m) => m.id).filter(Boolean));

      await Promise.all(
        saved
          .filter((s) => !keptIds.has(s.id))
          .map((s) => householdApi.remove(s.id)),
      );
      await Promise.all(
        named.map((m) => {
          const body = {
            name: m.name.trim(),
            relationship: m.relationship ?? null,
            targetKcal: m.mode === "own" ? Number(m.kcal) || null : null,
          };
          return m.id ? householdApi.update(m.id, body) : householdApi.add(body);
        }),
      );
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.household }),
  });

  function buildPlanBody(): CreateDietPlanRequest {
    const body: CreateDietPlanRequest = { horizonDays: Number(days) || 7 };
    // Always send the explicit set the form shows (even empty = just me). The pre-fill already
    // reflects the saved household, so this respects "remove a row to cook for just yourself".
    body.members = members
      .filter((m) => m.name.trim().length > 0)
      .map((m) => ({
        name: m.name.trim(),
        targetKcal: m.mode === "own" ? Number(m.kcal) || undefined : undefined,
      }));
    if (dietSlug) body.dietSlug = dietSlug;
    if (kcalTarget.trim()) body.kcalTarget = Number(kcalTarget);
    if (maxPrep.trim()) body.maxPrepMinutes = Number(maxPrep);
    if (Number(blockSize) > 1) body.blockSize = Number(blockSize);
    if (desire.trim()) body.desire = desire.trim();
    return body;
  }

  const create = useMutation({
    mutationFn: () => dietPlansApi.create(buildPlanBody()),
    onSuccess: (plan) => onCreated(plan.id),
  });

  // "Make me a full diet" (#101): the agent generates recipes to fill the pool, then plans.
  const [autoNote, setAutoNote] = useState<string | null>(null);
  const autoCreate = useMutation({
    mutationFn: () => dietPlansApi.auto(buildPlanBody()),
    onSuccess: (res) => {
      setAutoNote(
        res.recipesGenerated > 0
          ? `Generated ${res.recipesGenerated} new recipe${res.recipesGenerated === 1 ? "" : "s"} for your plan.`
          : "Used your existing recipes.",
      );
      queryClient.invalidateQueries({ queryKey: queryKeys.recipes });
      onCreated(res.plan.id);
    },
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Generate a plan</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Saved diet presets (#102): reuse the kind of plan you always make. */}
        <div className="space-y-2 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
          <Label>Saved diets</Label>
          {templates.data && templates.data.length > 0 ? (
            <div className="flex flex-wrap gap-2">
              {templates.data.map((t) => (
                <div
                  key={t.id}
                  className="flex items-center gap-1 rounded-full border border-slate-800 bg-slate-900/60 py-1 pr-1 pl-3 text-xs"
                >
                  <button
                    type="button"
                    className="font-medium text-slate-100 hover:text-brand-400"
                    onClick={() => applyTemplate(t)}
                    title="Load these settings into the form"
                  >
                    {t.name}
                  </button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-6 w-6"
                    title="Generate this diet now"
                    onClick={() => generateTemplate.mutate(t.id)}
                    disabled={generateTemplate.isPending}
                    aria-label={`Generate ${t.name}`}
                  >
                    <Sparkles className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-6 w-6"
                    title="Delete this preset"
                    onClick={() => deleteTemplate.mutate(t.id)}
                    aria-label={`Delete ${t.name}`}
                  >
                    <X className="h-3.5 w-3.5" />
                  </Button>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-xs text-slate-500">
              No saved diets yet — set up a plan below, then save the settings to reuse them in one tap.
            </p>
          )}
          <div className="flex items-center gap-2">
            <Input
              value={presetName}
              onChange={(e) => setPresetName(e.target.value)}
              placeholder="Name, e.g. 'My usual cut'"
              className="h-9 text-xs"
            />
            <Button
              variant="outline"
              size="sm"
              disabled={!presetName.trim() || saveTemplate.isPending}
              onClick={() => saveTemplate.mutate(presetName.trim())}
            >
              {saveTemplate.isPending ? <Spinner /> : <Bookmark className="h-4 w-4" />}
              Save settings
            </Button>
          </div>
        </div>

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
            <Label htmlFor="block-size">Cook every (days)</Label>
            <Input
              id="block-size"
              type="number"
              min={1}
              max={Number(days) || 30}
              value={blockSize}
              onChange={(e) => setBlockSize(e.target.value)}
            />
            <p className="text-[11px] text-slate-500">
              Batch-cook: 1 = fresh daily; 3 = cook once, eat 3 days.
            </p>
          </div>
        </div>

        <div className="space-y-2">
          <Label>Cooking for</Label>
          <div className="flex items-center gap-2 rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2 text-sm">
            <Users className="h-4 w-4 text-slate-500" />
            <span className="font-medium text-slate-100">You</span>
            <span className="text-xs text-slate-500">· your target</span>
          </div>

          {members.map((m, i) => (
            <div
              key={i}
              className="space-y-2 rounded-lg border border-slate-800 bg-slate-950/40 p-2"
            >
              <div className="flex items-center gap-2">
                <Input
                  value={m.name}
                  onChange={(e) => updateMember(i, { name: e.target.value })}
                  placeholder="e.g. Girlfriend"
                />
                <Button
                  variant="outline"
                  size="icon"
                  onClick={() => setMembers((prev) => prev.filter((_, idx) => idx !== i))}
                  aria-label="Remove person"
                >
                  <X className="h-4 w-4" />
                </Button>
              </div>
              <div className="flex items-center gap-2">
                <Select
                  value={m.mode}
                  onChange={(e) => updateMember(i, { mode: e.target.value as "same" | "own" })}
                >
                  <option value="same">Same as me</option>
                  <option value="own">Own calorie target</option>
                </Select>
                {m.mode === "own" ? (
                  <Input
                    type="number"
                    min={800}
                    max={6000}
                    value={m.kcal}
                    onChange={(e) => updateMember(i, { kcal: e.target.value })}
                    placeholder="kcal"
                    className="w-32"
                  />
                ) : null}
              </div>
            </div>
          ))}

          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setMembers((prev) => [...prev, { name: "", mode: "same", kcal: "" }])}
            >
              <Plus className="h-4 w-4" />
              Add person
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => saveHousehold.mutate()}
              disabled={saveHousehold.isPending}
              title="Save these people so they're added to every plan automatically"
            >
              {saveHousehold.isPending ? (
                <Spinner />
              ) : saveHousehold.isSuccess ? (
                <Check className="h-4 w-4 text-brand-400" />
              ) : (
                <Bookmark className="h-4 w-4" />
              )}
              {saveHousehold.isSuccess ? "Saved to household" : "Save as my household"}
            </Button>
          </div>
          <p className="text-xs text-slate-500">
            Everyone eats the same meals; each person&apos;s portion scales to their target.
            {household.data && household.data.length > 0
              ? " Your saved household is pre-filled here — edit freely, then re-save."
              : " Save your household once and we'll add them to every plan automatically."}
          </p>
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
        {autoCreate.isError ? <ErrorState error={autoCreate.error} /> : null}

        <Button
          onClick={() => create.mutate()}
          disabled={create.isPending || autoCreate.isPending}
          className="w-full"
        >
          {create.isPending ? <Spinner /> : <Sparkles className="h-4 w-4" />}
          Generate plan
        </Button>

        {/* The headline #101 action: let the agent author the recipes too. */}
        <Button
          variant="outline"
          onClick={() => {
            setAutoNote(null);
            autoCreate.mutate();
          }}
          disabled={create.isPending || autoCreate.isPending}
          className="w-full"
        >
          {autoCreate.isPending ? <Spinner /> : <ChefHat className="h-4 w-4" />}
          Make me a full diet with AI
        </Button>
        <p className="text-xs text-slate-500">
          No recipes to add yourself — the assistant writes the recipes and prep steps, then builds your
          plan. {autoNote ? <span className="text-brand-400">{autoNote}</span> : null}
        </p>
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
  const [batchCook, setBatchCook] = useState<BatchCookPlanDto | null>(null);

  // Each entry is one BLOCK (a distinct meal-set); with blockSize 1 a block is a single day.
  const days = useMemo(() => groupByDay(plan.slots), [plan.slots]);
  const eaters = plan.eaters ?? 1;
  const members = plan.members ?? [];
  // `portion` = Σ member factors (or eaters when no members) — scales cook totals + shopping only.
  const portion = plan.portionMultiplier ?? eaters;
  // achieved* are already PER-PRIMARY, PER-DAY averages from the server. Show them as-is: never
  // re-divide by the block/day count and never multiply by the headcount (they describe ONE person's
  // day; each member's portion = achieved × their factor).

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

  const genBatch = useMutation({
    mutationFn: () => dietPlansApi.batchCook(plan.id),
    onSuccess: (guide) => setBatchCook(guide),
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

          {members.length > 1 ? (
            <div className="rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2">
              <p className="mb-1.5 text-xs font-medium tracking-wide text-slate-500 uppercase">
                Portions per person
              </p>
              <ul className="space-y-1">
                {members.map((m, i) => (
                  <li key={i} className="flex items-center justify-between gap-2 text-sm">
                    <span className="truncate text-slate-100">{m.name}</span>
                    <span className="shrink-0 text-xs text-slate-400">
                      ~{round(plan.achievedKcal * m.portionFactor)} kcal/day ·{" "}
                      {round(m.portionFactor, 2)}× portions
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {plan.message ? (
            <p className="rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2 text-sm text-slate-300">
              {plan.message}
            </p>
          ) : null}

          {accept.isError ? <ErrorState error={accept.error} /> : null}
          {genList.isError ? <ErrorState error={genList.error} /> : null}
          {genBatch.isError ? <ErrorState error={genBatch.error} /> : null}
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
              onClick={() => genBatch.mutate()}
              disabled={genBatch.isPending}
            >
              {genBatch.isPending ? <Spinner /> : <CookingPot className="h-4 w-4" />}
              Batch-cook guide
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

      {batchCook ? <BatchCookGuide guide={batchCook} /> : null}

      <Card>
        <CardHeader>
          <CardTitle>Meal plan</CardTitle>
          {plan.blockSize > 1 ? (
            <p className="text-xs text-slate-400">
              Cooked as {plan.numBlocks} batches — cook each meal-set once, eat it
              for up to {plan.blockSize} days.
            </p>
          ) : null}
        </CardHeader>
        <CardContent className="space-y-5">
          {days.map(({ day, slots }) => {
            const span = blockSpan(day, plan.blockSize, plan.horizonDays);
            return (
            <div key={day}>
              <h3 className="mb-2 flex items-center gap-2 text-xs font-semibold tracking-wide text-slate-400 uppercase">
                {plan.blockSize > 1 ? (
                  <>
                    <span className="rounded bg-amber-500/15 px-1.5 py-0.5 text-amber-300">
                      Block {day}
                    </span>
                    {span.label}
                  </>
                ) : (
                  <>{span.label}</>
                )}
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
                            {portion > 1 || span.days > 1 ? "/person/day" : ""}
                            {portion > 1 || span.days > 1
                              ? ` · cook ${round(slot.servings * portion * span.days, 2)} for this block`
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
            );
          })}
        </CardContent>
      </Card>

      {shoppingList ? (
        <Card>
          <CardHeader>
            <CardTitle>Shopping list</CardTitle>
            <p className="text-xs text-slate-500">
              Totals for {plan.horizonDays}{" "}
              {plan.horizonDays === 1 ? "day" : "days"} ·{" "}
              {eaters} {eaters === 1 ? "person" : "people"}
              {plan.blockSize > 1 ? ` · ${plan.numBlocks} batches` : ""}
              {members.length > 1 ? ` (${round(portion, 2)}× portions)` : ""}
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

// -------------------- Batch-cook guide (#86) --------------------

function BatchCookGuide({ guide }: { guide: BatchCookPlanDto }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Batch-cook guide</CardTitle>
        <p className="text-xs text-slate-400">
          Cook once per block in RAW weight for {guide.eaters}{" "}
          {guide.eaters === 1 ? "person" : "people"}, then split into portions.
        </p>
      </CardHeader>
      <CardContent className="space-y-5">
        {guide.blocks.map((block) => {
          const byMethod = new Map<string, BatchCookStep[]>();
          for (const s of block.steps) {
            const arr = byMethod.get(s.method);
            if (arr) arr.push(s);
            else byMethod.set(s.method, [s]);
          }
          return (
            <div key={block.block}>
              <h3 className="mb-2 flex flex-wrap items-center gap-2 text-xs font-semibold tracking-wide text-slate-400 uppercase">
                <span className="rounded bg-amber-500/15 px-1.5 py-0.5 text-amber-300">
                  Block {block.block}
                </span>
                {block.days} · portion into {block.portions}
              </h3>
              <div className="space-y-3">
                {[...byMethod.entries()].map(([method, steps]) => (
                  <div
                    key={method}
                    className="rounded-lg border border-slate-800 bg-slate-950/40 p-3"
                  >
                    <p className="mb-2 flex items-center gap-1.5 text-xs font-medium text-slate-300">
                      <Flame className="h-3.5 w-3.5 text-orange-400" />
                      {method}
                    </p>
                    {steps.map((s, i) => (
                      <div key={i} className="mb-2 last:mb-0">
                        <p className="text-sm text-slate-100">
                          {s.recipeName}
                          <span className="ml-1.5 text-xs text-slate-500">
                            → cook {round(s.cookServings, 1)} servings · portion
                            into {s.portionInto}
                          </span>
                        </p>
                        <ul className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-slate-400">
                          {s.ingredients.map((ing, j) => (
                            <li key={j}>
                              {round(ing.rawGrams)}g {ing.name}
                            </li>
                          ))}
                        </ul>
                      </div>
                    ))}
                  </div>
                ))}
              </div>
            </div>
          );
        })}
      </CardContent>
    </Card>
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

/**
 * The calendar-day span a block (1-indexed) covers, plus a label. With blockSize 1 this is a single
 * day ("Day N"); otherwise a range ("Days 1–3"), the last block shorter when the horizon isn't a
 * whole multiple. `days` is how many days the block feeds — the batch-cook multiplier.
 */
function blockSpan(
  blockIndex: number,
  blockSize: number,
  horizonDays: number,
): { label: string; days: number } {
  const size = Math.max(blockSize, 1);
  const start = (blockIndex - 1) * size + 1;
  const end = Math.min(blockIndex * size, horizonDays);
  const days = Math.max(end - start + 1, 1);
  return { label: start === end ? `Day ${start}` : `Days ${start}–${end}`, days };
}
