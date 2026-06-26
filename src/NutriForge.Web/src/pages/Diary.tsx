import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Barcode,
  BookOpen,
  Camera,
  ChevronLeft,
  ChevronRight,
  Check,
  Copy,
  Plus,
  Search,
  Sparkles,
  Trash2,
} from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeading } from "@/components/PageHeading";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState, LoadingState } from "@/components/StateMessage";
import {
  useDebounced,
  useDeleteDiaryEntry,
  useDiary,
} from "@/hooks/useQueries";
import { ApiError, diaryApi, foodsApi } from "@/lib/api";
import { QuickAddCard } from "@/components/QuickAddCard";
import { MealTemplatesCard } from "@/components/MealTemplatesCard";
import { queryKeys } from "@/lib/queryKeys";
import {
  MEAL_SLOTS,
  type DiaryParseResult,
  type FoodDetail,
  type FoodSummary,
  type MealSlot,
  type ParseCandidate,
  type PhotoAnalysisResult,
  type PhotoFoodCandidate,
} from "@/lib/types";
import { cn, round, toIsoDate, today } from "@/lib/utils";

/** Guess the meal the user most likely wants to log, from the current hour. */
function slotForHour(): MealSlot {
  const h = new Date().getHours();
  if (h < 11) return "Breakfast";
  if (h < 15) return "Lunch";
  if (h < 21) return "Dinner";
  return "Snack";
}

export function Diary() {
  const qc = useQueryClient();
  const [date, setDate] = useState<string>(today());
  const [mealSlot, setMealSlot] = useState<MealSlot>(slotForHour);

  const diary = useDiary(date);
  const deleteEntry = useDeleteDiaryEntry(date);

  function shiftDate(deltaDays: number) {
    const d = new Date(date + "T00:00:00");
    d.setDate(d.getDate() + deltaDays);
    setDate(toIsoDate(d));
  }

  // Copy the previous day's entries onto this day in one server call (#69): it re-resolves + re-snapshots
  // each entry's current nutrition and skips foods no longer in the catalog.
  const copyYesterday = useMutation({
    mutationFn: () => {
      const y = new Date(date + "T00:00:00");
      y.setDate(y.getDate() - 1);
      return diaryApi.copyDay(toIsoDate(y), date);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.diary(date) });
      qc.invalidateQueries({ queryKey: queryKeys.diaryAll });
      qc.invalidateQueries({ queryKey: queryKeys.targets });
    },
  });

  function invalidateDay() {
    qc.invalidateQueries({ queryKey: queryKeys.diary(date) });
    qc.invalidateQueries({ queryKey: queryKeys.diaryAll });
    qc.invalidateQueries({ queryKey: queryKeys.targets });
  }

  return (
    <div className="space-y-6">
      <PageHeading
        title="Diary"
        subtitle="Log and review your day"
        icon={BookOpen}
        action={
          <>
            <Button
              variant="outline"
              size="sm"
              onClick={() => copyYesterday.mutate()}
              disabled={copyYesterday.isPending}
              title="Copy yesterday's entries onto this day"
            >
              {copyYesterday.isPending ? <Spinner /> : <Copy className="h-4 w-4" />}
              Copy yesterday
            </Button>
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
          </>
        }
      />

      {copyYesterday.isError ? <ErrorState error={copyYesterday.error} /> : null}

      <QuickAddCard date={date} mealSlot={mealSlot} onLogged={invalidateDay} />

      <MealTemplatesCard date={date} mealSlot={mealSlot} onLogged={invalidateDay} />

      <div className="grid gap-6 lg:grid-cols-5">
        {/* Add food: Search | Describe | Barcode */}
        <div className="lg:col-span-2">
          <AddFood
            date={date}
            mealSlot={mealSlot}
            onMealSlotChange={setMealSlot}
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

// -------------------- Add food (tabbed) --------------------

type AddTab = "search" | "describe" | "photo" | "barcode";

const TABS: { id: AddTab; label: string; icon: typeof Search }[] = [
  { id: "search", label: "Search", icon: Search },
  { id: "describe", label: "Describe", icon: Sparkles },
  { id: "photo", label: "Photo", icon: Camera },
  { id: "barcode", label: "Barcode", icon: Barcode },
];

/**
 * Shared logging mutation used by every tab. Invalidates the day diary plus
 * the day-independent `diaryAll` and `targets` keys after each successful log.
 */
function useLogEntry() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      date: string;
      mealSlot: string;
      foodId: string;
      portionId: string | null;
      quantity: number;
    }) =>
      diaryApi.create({
        date: body.date,
        mealSlot: body.mealSlot as MealSlot,
        foodId: body.foodId,
        portionId: body.portionId,
        quantity: body.quantity,
      }),
    onSuccess: (entry) => {
      qc.invalidateQueries({ queryKey: queryKeys.diary(entry.date) });
      qc.invalidateQueries({ queryKey: queryKeys.diaryAll });
      qc.invalidateQueries({ queryKey: queryKeys.targets });
    },
  });
}

function AddFood({
  date,
  mealSlot,
  onMealSlotChange,
}: {
  date: string;
  mealSlot: MealSlot;
  onMealSlotChange: (slot: MealSlot) => void;
}) {
  const [tab, setTab] = useState<AddTab>("search");

  return (
    <Card>
      <CardHeader>
        <CardTitle>Add food</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div
          role="tablist"
          aria-label="Add food method"
          className="grid grid-cols-2 gap-1 rounded-lg border border-slate-800 bg-slate-950/40 p-1 sm:grid-cols-4"
        >
          {TABS.map(({ id, label, icon: Icon }) => (
            <button
              key={id}
              role="tab"
              aria-selected={tab === id}
              onClick={() => setTab(id)}
              className={cn(
                "flex items-center justify-center gap-1.5 rounded-md px-2 py-1.5 text-sm font-medium transition-colors",
                tab === id
                  ? "bg-slate-800 text-slate-100"
                  : "text-slate-400 hover:text-slate-200",
              )}
            >
              <Icon className="h-4 w-4" />
              {label}
            </button>
          ))}
        </div>

        {tab === "search" ? (
          <SearchTab date={date} />
        ) : tab === "describe" ? (
          <DescribeTab
            date={date}
            mealSlot={mealSlot}
            onMealSlotChange={onMealSlotChange}
          />
        ) : tab === "photo" ? (
          <PhotoTab
            date={date}
            mealSlot={mealSlot}
            onMealSlotChange={onMealSlotChange}
          />
        ) : (
          <BarcodeTab
            date={date}
            mealSlot={mealSlot}
            onMealSlotChange={onMealSlotChange}
          />
        )}
      </CardContent>
    </Card>
  );
}

// -------------------- Search tab --------------------

function SearchTab({ date }: { date: string }) {
  const [selectedFood, setSelectedFood] = useState<FoodSummary | null>(null);
  const [term, setTerm] = useState("");
  const debounced = useDebounced(term, 350);
  const enabled = debounced.trim().length >= 2;

  const search = useQuery({
    queryKey: queryKeys.foodSearch(debounced.trim()),
    queryFn: ({ signal }) => foodsApi.search(debounced.trim(), signal),
    enabled,
  });

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-slate-500" />
        <Input
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="Search foods…"
          className="pl-9"
          aria-label="Search foods"
        />
      </div>

      {selectedFood ? (
        <LogForm
          food={selectedFood}
          date={date}
          onCancel={() => setSelectedFood(null)}
          onLogged={() => setSelectedFood(null)}
        />
      ) : (
        <SearchResults
          enabled={enabled}
          loading={search.isLoading && enabled}
          error={search.error}
          results={search.data}
          onPick={setSelectedFood}
        />
      )}
    </div>
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

function MealSlotSelect({
  id,
  value,
  onChange,
}: {
  id: string;
  value: MealSlot;
  onChange: (slot: MealSlot) => void;
}) {
  return (
    <div className="space-y-1">
      <Label htmlFor={id}>Meal</Label>
      <Select
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value as MealSlot)}
      >
        {MEAL_SLOTS.map((slot) => (
          <option key={slot} value={slot}>
            {slot}
          </option>
        ))}
      </Select>
    </div>
  );
}

// -------------------- Describe (natural-language) tab --------------------

function DescribeTab({
  date,
  mealSlot,
  onMealSlotChange,
}: {
  date: string;
  mealSlot: MealSlot;
  onMealSlotChange: (slot: MealSlot) => void;
}) {
  const [text, setText] = useState("");
  // Tracks which candidate rows have been logged, keyed by index.
  const [logged, setLogged] = useState<Record<number, boolean>>({});

  const parse = useMutation<DiaryParseResult, unknown, void>({
    mutationFn: () => diaryApi.parse(text.trim(), mealSlot, date),
    onSuccess: () => setLogged({}),
  });

  const noProvider =
    parse.error instanceof ApiError && parse.error.status === 503;

  function submit() {
    if (text.trim().length === 0) return;
    parse.mutate();
  }

  return (
    <div className="space-y-3">
      <div className="space-y-1">
        <Label htmlFor="nl-text">Describe your meal</Label>
        <textarea
          id="nl-text"
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="e.g. '2 eggs and toast'"
          rows={3}
          className="flex w-full rounded-lg border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus-visible:border-brand-500 focus-visible:ring-2 focus-visible:ring-brand-500/30 focus-visible:outline-none"
        />
      </div>

      <MealSlotSelect id="nl-meal" value={mealSlot} onChange={onMealSlotChange} />

      <Button
        onClick={submit}
        disabled={parse.isPending || text.trim().length === 0}
        className="w-full"
      >
        {parse.isPending ? <Spinner /> : <Sparkles className="h-4 w-4" />}
        Add these foods
      </Button>

      {noProvider ? (
        <p className="rounded-lg border border-amber-900/60 bg-amber-950/40 px-4 py-3 text-sm text-amber-200">
          Natural-language logging needs an LLM provider — set OPENAI_API_KEY.
        </p>
      ) : parse.isError ? (
        <ErrorState error={parse.error} />
      ) : null}

      {parse.data ? (
        <ParseResults
          result={parse.data}
          logged={logged}
          onLogged={(i) => setLogged((prev) => ({ ...prev, [i]: true }))}
        />
      ) : null}
    </div>
  );
}

function ParseResults({
  result,
  logged,
  onLogged,
}: {
  result: DiaryParseResult;
  logged: Record<number, boolean>;
  onLogged: (index: number) => void;
}) {
  const { candidates, unresolved } = result;

  if (candidates.length === 0 && unresolved.length === 0) {
    return (
      <p className="py-4 text-center text-sm text-slate-500">
        Nothing found in that description.
      </p>
    );
  }

  return (
    <div className="space-y-3">
      {candidates.length > 0 ? (
        <ul className="divide-y divide-slate-800 overflow-hidden rounded-lg border border-slate-800">
          {candidates.map((candidate, i) => (
            <li key={`${candidate.foodId}-${i}`}>
              <CandidateRow
                candidate={candidate}
                logged={logged[i] ?? false}
                onLogged={() => onLogged(i)}
              />
            </li>
          ))}
        </ul>
      ) : null}

      {unresolved.length > 0 ? (
        <p className="rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2 text-xs text-slate-400">
          Couldn&apos;t find: {unresolved.join(", ")}
        </p>
      ) : null}
    </div>
  );
}

function CandidateRow({
  candidate,
  logged,
  onLogged,
}: {
  candidate: ParseCandidate;
  logged: boolean;
  onLogged: () => void;
}) {
  const log = useLogEntry();

  function submit() {
    log.mutate(
      {
        date: candidate.date,
        mealSlot: candidate.mealSlot,
        foodId: candidate.foodId,
        portionId: candidate.portionId,
        quantity: candidate.quantity,
      },
      { onSuccess: onLogged },
    );
  }

  return (
    <div className="flex items-center justify-between gap-3 px-3 py-2.5">
      <div className="min-w-0">
        <p className="truncate text-sm font-medium text-slate-100">
          {round(candidate.quantity, 2)} {candidate.portionName} ·{" "}
          {round(candidate.kcal)} kcal
        </p>
        <p className="truncate text-xs text-slate-500">
          {candidate.foodName}
          <span className="mx-1.5 text-slate-700">·</span>
          {candidate.mealSlot}
        </p>
      </div>
      {logged ? (
        <span className="flex shrink-0 items-center gap-1 text-sm font-medium text-emerald-300">
          <Check className="h-4 w-4" />
          Logged
        </span>
      ) : (
        <Button
          size="sm"
          onClick={submit}
          disabled={log.isPending}
          className="shrink-0"
        >
          {log.isPending ? <Spinner /> : <Plus className="h-4 w-4" />}
          Log
        </Button>
      )}
    </div>
  );
}

// -------------------- Photo tab --------------------

function PhotoTab({
  date,
  mealSlot,
  onMealSlotChange,
}: {
  date: string;
  mealSlot: MealSlot;
  onMealSlotChange: (slot: MealSlot) => void;
}) {
  const [logged, setLogged] = useState<Record<number, boolean>>({});

  const analyze = useMutation<PhotoAnalysisResult, unknown, File>({
    mutationFn: (file) => diaryApi.parsePhoto(file, mealSlot, date),
    onSuccess: () => setLogged({}),
  });

  const noProvider =
    analyze.error instanceof ApiError && analyze.error.status === 503;

  function onPick(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (file) analyze.mutate(file);
    e.target.value = ""; // allow re-picking the same file
  }

  return (
    <div className="space-y-3">
      <MealSlotSelect id="photo-meal" value={mealSlot} onChange={onMealSlotChange} />

      <label className="flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border border-dashed border-slate-700 bg-slate-950/40 px-4 py-6 text-sm font-medium text-slate-300 hover:border-brand-500 hover:text-slate-100">
        {analyze.isPending ? <Spinner /> : <Camera className="h-6 w-6" />}
        {analyze.isPending ? "Analyzing photo…" : "Take or upload a meal photo"}
        <input
          type="file"
          accept="image/*"
          capture="environment"
          className="hidden"
          onChange={onPick}
          disabled={analyze.isPending}
        />
      </label>

      <p className="text-xs text-slate-500">
        A vision model guesses the foods &amp; portions. Matched foods use catalog
        nutrition; others show an AI estimate. Nothing is logged until you confirm.
      </p>

      {noProvider ? (
        <p className="rounded-lg border border-amber-900/60 bg-amber-950/40 px-4 py-3 text-sm text-amber-200">
          Photo logging needs a vision provider — set OPENAI_API_KEY.
        </p>
      ) : analyze.isError ? (
        <ErrorState error={analyze.error} />
      ) : null}

      {analyze.data ? (
        <PhotoResults
          result={analyze.data}
          date={date}
          mealSlot={mealSlot}
          logged={logged}
          onLogged={(i) => setLogged((prev) => ({ ...prev, [i]: true }))}
        />
      ) : null}
    </div>
  );
}

function PhotoResults({
  result,
  date,
  mealSlot,
  logged,
  onLogged,
}: {
  result: PhotoAnalysisResult;
  date: string;
  mealSlot: MealSlot;
  logged: Record<number, boolean>;
  onLogged: (index: number) => void;
}) {
  if (result.items.length === 0) {
    return (
      <p className="py-4 text-center text-sm text-slate-500">
        No foods recognized in that photo.
      </p>
    );
  }

  return (
    <ul className="divide-y divide-slate-800 overflow-hidden rounded-lg border border-slate-800">
      {result.items.map((item, i) => (
        <li key={i}>
          <PhotoCandidateRow
            item={item}
            date={date}
            mealSlot={mealSlot}
            logged={logged[i] ?? false}
            onLogged={() => onLogged(i)}
          />
        </li>
      ))}
    </ul>
  );
}

function PhotoCandidateRow({
  item,
  date,
  mealSlot,
  logged,
  onLogged,
}: {
  item: PhotoFoodCandidate;
  date: string;
  mealSlot: MealSlot;
  logged: boolean;
  onLogged: () => void;
}) {
  const log = useLogEntry();
  const canLog = item.source === "catalog" && item.foodId !== null;

  function submit() {
    if (!canLog || !item.foodId) return;
    log.mutate(
      {
        date,
        mealSlot,
        foodId: item.foodId,
        portionId: item.portionId,
        quantity: item.quantity,
      },
      { onSuccess: onLogged },
    );
  }

  return (
    <div className="flex items-center justify-between gap-3 px-3 py-2.5">
      <div className="min-w-0">
        <p className="flex items-center gap-2 truncate text-sm font-medium text-slate-100">
          <span className="truncate">{item.label}</span>
          {item.source === "estimate" ? (
            <span className="shrink-0 rounded bg-amber-500/15 px-1.5 py-0.5 text-[10px] font-medium text-amber-300">
              AI estimate
            </span>
          ) : null}
        </p>
        <p className="truncate text-xs text-slate-500">
          ~{round(item.grams)} g · {round(item.kcal)} kcal
        </p>
      </div>
      {canLog ? (
        logged ? (
          <span className="flex shrink-0 items-center gap-1 text-sm font-medium text-emerald-300">
            <Check className="h-4 w-4" />
            Logged
          </span>
        ) : (
          <Button size="sm" onClick={submit} disabled={log.isPending} className="shrink-0">
            {log.isPending ? <Spinner /> : <Plus className="h-4 w-4" />}
            Log
          </Button>
        )
      ) : (
        <span className="shrink-0 text-xs text-slate-500">estimate only</span>
      )}
    </div>
  );
}

// -------------------- Barcode tab --------------------

function BarcodeTab({
  date,
  mealSlot,
  onMealSlotChange,
}: {
  date: string;
  mealSlot: MealSlot;
  onMealSlotChange: (slot: MealSlot) => void;
}) {
  const [gtin, setGtin] = useState("");

  const lookup = useMutation<FoodDetail | null, unknown, void>({
    mutationFn: () => foodsApi.barcode(gtin.trim()),
  });

  function submit() {
    if (gtin.trim().length === 0) return;
    lookup.mutate();
  }

  return (
    <div className="space-y-3">
      <div className="space-y-1">
        <Label htmlFor="gtin">Barcode number</Label>
        <Input
          id="gtin"
          inputMode="numeric"
          pattern="[0-9]*"
          value={gtin}
          onChange={(e) => setGtin(e.target.value.replace(/[^0-9]/g, ""))}
          placeholder="e.g. 5000159484695"
        />
      </div>

      <Button
        onClick={submit}
        disabled={lookup.isPending || gtin.trim().length === 0}
        className="w-full"
      >
        {lookup.isPending ? <Spinner /> : <Barcode className="h-4 w-4" />}
        Look up
      </Button>

      {lookup.isError ? <ErrorState error={lookup.error} /> : null}

      {lookup.isSuccess ? (
        lookup.data ? (
          <LogForm
            food={lookup.data}
            date={date}
            mealSlot={mealSlot}
            onMealSlotChange={onMealSlotChange}
            onCancel={() => lookup.reset()}
            onLogged={() => lookup.reset()}
          />
        ) : (
          <p className="py-4 text-center text-sm text-slate-500">
            No product found for that barcode.
          </p>
        )
      ) : null}
    </div>
  );
}

const GRAMS_OPTION = "__grams__";

function LogForm({
  food,
  date,
  mealSlot: controlledMealSlot,
  onMealSlotChange,
  onCancel,
  onLogged,
}: {
  food: FoodSummary;
  date: string;
  mealSlot?: MealSlot;
  onMealSlotChange?: (slot: MealSlot) => void;
  onCancel: () => void;
  onLogged: () => void;
}) {
  // Use controlled meal-slot when provided (barcode tab), else local state.
  const [localMealSlot, setLocalMealSlot] = useState<MealSlot>("Breakfast");
  const mealSlot = controlledMealSlot ?? localMealSlot;
  const setMealSlot = onMealSlotChange ?? setLocalMealSlot;
  const [portionId, setPortionId] = useState<string>(
    food.portions[0]?.id ?? GRAMS_OPTION,
  );
  const [quantity, setQuantity] = useState<string>(
    food.portions.length > 0 ? "1" : "100",
  );

  const add = useLogEntry();
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
        <MealSlotSelect id="meal" value={mealSlot} onChange={setMealSlot} />
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
                <div className="overflow-x-auto rounded-lg border border-slate-800">
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
    <div className="overflow-x-auto rounded-lg border border-brand-500/30 bg-brand-500/5">
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
