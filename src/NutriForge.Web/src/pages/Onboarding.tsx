import { useState } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ArrowRight, Check, ChevronDown, Search, Sparkles, Utensils } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/StateMessage";
import { useProfile, useSaveProfile, useTargets } from "@/hooks/useQueries";
import { consentApi, diaryApi, foodsApi } from "@/lib/api";
import { useDebounced } from "@/hooks/useQueries";
import { queryKeys } from "@/lib/queryKeys";
import {
  ACTIVITY_LEVELS,
  GOALS,
  SEXES,
  type ActivityLevel,
  type FoodSummary,
  type Goal,
  type Sex,
} from "@/lib/types";
import { dismissOnboarding } from "@/lib/onboarding";
import { cn, round, today } from "@/lib/utils";

/**
 * First-run onboarding (#68): profile → first target → first logged food, so a new user reaches value
 * in under two minutes. Reachable at /welcome; the AppShell routes profile-less users here.
 */
export function Onboarding() {
  const navigate = useNavigate();
  const profile = useProfile();
  // Capture, once, whether the user already had a profile when they arrived — so saving the profile in
  // step 1 (which flips profile.data to non-null) doesn't bounce us out of the wizard mid-flow.
  const [hadProfileOnEntry] = useState(() => profile.data != null);
  const [step, setStep] = useState(0);

  // An existing user who lands here directly (and didn't just start the flow) goes straight to the app.
  if (hadProfileOnEntry && step === 0) {
    return <Navigate to="/" replace />;
  }

  function finish() {
    dismissOnboarding();
    navigate("/", { replace: true });
  }

  return (
    <div className="min-h-screen bg-slate-950 px-4 py-10 text-slate-200">
      <div className="mx-auto max-w-xl space-y-6">
        <header className="space-y-2 text-center">
          <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-xl bg-brand-500 text-slate-950">
            <Utensils className="h-6 w-6" />
          </span>
          <h1 className="text-2xl font-bold text-slate-100">Welcome to NutriForge</h1>
          <p className="text-sm text-slate-400">Three quick steps to your personalized targets.</p>
        </header>

        <Stepper step={step} />

        {step === 0 ? (
          <ProfileStep onDone={() => setStep(1)} />
        ) : step === 1 ? (
          <TargetStep onBack={() => setStep(0)} onDone={() => setStep(2)} />
        ) : (
          <FirstFoodStep onFinish={finish} />
        )}
      </div>
    </div>
  );
}

const STEP_LABELS = ["Profile", "Target", "First food"] as const;

function Stepper({ step }: { step: number }) {
  return (
    <ol className="flex items-center justify-center gap-2" aria-label="Onboarding progress">
      {STEP_LABELS.map((label, i) => {
        const done = i < step;
        const active = i === step;
        return (
          <li key={label} className="flex items-center gap-2">
            <span
              className={`flex h-7 w-7 items-center justify-center rounded-full text-xs font-semibold ${
                done
                  ? "bg-emerald-500 text-slate-950"
                  : active
                    ? "bg-brand-500 text-slate-950"
                    : "bg-slate-800 text-slate-400"
              }`}
            >
              {done ? <Check className="h-4 w-4" /> : i + 1}
            </span>
            <span className={`text-xs ${active ? "text-slate-100" : "text-slate-500"}`}>{label}</span>
            {i < STEP_LABELS.length - 1 ? <span className="h-px w-5 bg-slate-700" /> : null}
          </li>
        );
      })}
    </ol>
  );
}

// -------------------- Step 1: profile essentials --------------------

const ACTIVITY_LABELS: Record<ActivityLevel, string> = {
  Sedentary: "Sedentary",
  LightlyActive: "Lightly active",
  ModeratelyActive: "Moderately active",
  Active: "Active",
  VeryActive: "Very active",
};

const GOAL_LABELS: Record<Goal, string> = {
  AggressiveCut: "Lose weight fast",
  ModerateCut: "Lose weight",
  Maintain: "Maintain weight",
  LeanBulk: "Gain muscle",
  Bulk: "Gain weight",
};

function ProfileStep({ onDone }: { onDone: () => void }) {
  const save = useSaveProfile();
  const [sex, setSex] = useState<Sex>("Male");
  const [birthDate, setBirthDate] = useState("1990-01-01");
  const [heightCm, setHeightCm] = useState("175");
  const [weightKg, setWeightKg] = useState("75");
  const [activity, setActivity] = useState<ActivityLevel>("ModeratelyActive");
  const [goal, setGoal] = useState<Goal>("Maintain");
  const [personalize, setPersonalize] = useState(false);

  function submit(e: React.FormEvent) {
    e.preventDefault();
    save.mutate(
      {
        sex,
        birthDate,
        heightCm: Number(heightCm),
        weightKg: Number(weightKg),
        bodyFatPct: null,
        activity,
        goal,
        // Sensible defaults — the full Profile page exposes these later.
        macroStrategy: "ProteinAnchored",
        allergens: [],
        dislikes: [],
        preferredDiets: [],
      },
      {
        onSuccess: async () => {
          try {
            // Consent is given implicitly by continuing (#58). Non-fatal if recording fails.
            await Promise.all([
              consentApi.record("TermsOfService", true),
              consentApi.record("PrivacyPolicy", true),
              consentApi.record("HealthDataProcessing", true),
            ]);
          } catch {
            /* consent can be managed later under Profile → Privacy & consent */
          }
          onDone();
        },
      },
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>About you</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={submit} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Sex">
              <Select value={sex} onChange={(e) => setSex(e.target.value as Sex)}>
                {SEXES.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Birth date">
              <Input type="date" value={birthDate} onChange={(e) => setBirthDate(e.target.value)} required />
            </Field>
            <Field label="Height (cm)">
              <Input
                type="number"
                min={50}
                max={272}
                step={0.5}
                value={heightCm}
                onChange={(e) => setHeightCm(e.target.value)}
                required
              />
            </Field>
            <Field label="Weight (kg)">
              <Input
                type="number"
                min={20}
                max={500}
                step={0.1}
                value={weightKg}
                onChange={(e) => setWeightKg(e.target.value)}
                required
              />
            </Field>
          </div>

          {/* Activity + Goal default sensibly; only the curious need to touch them. */}
          <div>
            <button
              type="button"
              onClick={() => setPersonalize((v) => !v)}
              className="flex w-full items-center justify-between text-sm font-medium text-slate-300 hover:text-slate-100"
              aria-expanded={personalize}
            >
              Personalize my target (optional)
              <ChevronDown
                className={cn("h-4 w-4 transition-transform", personalize && "rotate-180")}
              />
            </button>
            {personalize ? (
              <div className="mt-3 grid gap-4 sm:grid-cols-2">
                <Field label="How active are you?">
                  <Select value={activity} onChange={(e) => setActivity(e.target.value as ActivityLevel)}>
                    {ACTIVITY_LEVELS.map((a) => (
                      <option key={a} value={a}>
                        {ACTIVITY_LABELS[a]}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label="What's your goal?">
                  <Select value={goal} onChange={(e) => setGoal(e.target.value as Goal)}>
                    {GOALS.map((g) => (
                      <option key={g} value={g}>
                        {GOAL_LABELS[g]}
                      </option>
                    ))}
                  </Select>
                </Field>
              </div>
            ) : null}
          </div>

          {save.isError ? <ErrorState error={save.error} /> : null}

          <Button type="submit" disabled={save.isPending} className="w-full">
            {save.isPending ? <Spinner /> : <ArrowRight className="h-4 w-4" />}
            Calculate my target
          </Button>
          <p className="text-center text-xs text-slate-500">
            By continuing you agree to our Terms and Privacy Policy.
          </p>
        </form>
      </CardContent>
    </Card>
  );
}

// -------------------- Step 2: the computed target --------------------

function TargetStep({ onBack, onDone }: { onBack: () => void; onDone: () => void }) {
  const targets = useTargets();

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Sparkles className="h-4 w-4 text-brand-400" />
          Your daily target
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {targets.isLoading ? (
          <div className="flex items-center justify-center gap-2 py-8 text-slate-400">
            <Spinner />
            <span className="text-sm">Computing your target…</span>
          </div>
        ) : targets.isError ? (
          <ErrorState error={targets.error} />
        ) : targets.data ? (
          <>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Metric label="Calories" value={`${round(targets.data.kcal)}`} unit="kcal" highlight />
              <Metric label="Protein" value={`${round(targets.data.proteinG)}`} unit="g" />
              <Metric label="Fat" value={`${round(targets.data.fatG)}`} unit="g" />
              <Metric label="Carbs" value={`${round(targets.data.carbG)}`} unit="g" />
            </div>
            <p className="text-center text-xs text-slate-500">
              Computed just for you. You can fine-tune anytime in Profile.
            </p>
          </>
        ) : (
          <p className="py-6 text-center text-sm text-slate-500">No target yet — go back and save your profile.</p>
        )}

        <div className="flex gap-2">
          <Button variant="outline" onClick={onBack} className="flex-1">
            Back
          </Button>
          <Button onClick={onDone} disabled={!targets.data} className="flex-1">
            Log my first food
            <ArrowRight className="h-4 w-4" />
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function Metric({
  label,
  value,
  unit,
  highlight,
}: {
  label: string;
  value: string;
  unit: string;
  highlight?: boolean;
}) {
  return (
    <div
      className={`rounded-lg border px-3 py-2 text-center ${
        highlight ? "border-brand-500/40 bg-brand-500/10" : "border-slate-800 bg-slate-900/40"
      }`}
    >
      <p className="text-xs text-slate-400">{label}</p>
      <p className="text-lg font-bold text-slate-100">{value}</p>
      <p className="text-[0.65rem] text-slate-500">{unit}</p>
    </div>
  );
}

// -------------------- Step 3: log the first food --------------------

function FirstFoodStep({ onFinish }: { onFinish: () => void }) {
  const [term, setTerm] = useState("");
  const [loggedName, setLoggedName] = useState<string | null>(null);
  const debounced = useDebounced(term, 350);
  const enabled = debounced.trim().length >= 2;

  const search = useQuery({
    queryKey: queryKeys.foodSearch(debounced.trim()),
    queryFn: ({ signal }) => foodsApi.search(debounced.trim(), signal),
    enabled,
  });

  const log = useMutation({
    mutationFn: (food: FoodSummary) =>
      diaryApi.create({
        date: today(),
        mealSlot: "Breakfast",
        foodId: food.id,
        portionId: food.portions[0]?.id ?? null,
        quantity: food.portions.length > 0 ? 1 : 100,
      }),
    onSuccess: (_entry, food) => setLoggedName(food.name),
  });

  if (loggedName) {
    return (
      <Card>
        <CardContent className="space-y-4 py-8 text-center">
          <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-emerald-500 text-slate-950">
            <Check className="h-6 w-6" />
          </span>
          <div>
            <p className="font-semibold text-slate-100">Logged {loggedName} 🎉</p>
            <p className="text-sm text-slate-400">You&apos;re all set — here&apos;s your dashboard.</p>
          </div>
          <Button onClick={onFinish} className="w-full">
            Go to my dashboard
            <ArrowRight className="h-4 w-4" />
          </Button>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Log your first food</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-slate-500" />
          <Input
            value={term}
            onChange={(e) => setTerm(e.target.value)}
            placeholder="Search foods… (e.g. egg, banana)"
            className="pl-9"
            aria-label="Search foods"
            autoFocus
          />
        </div>

        {log.isError ? <ErrorState error={log.error} /> : null}

        {!enabled ? (
          <p className="py-4 text-center text-sm text-slate-500">Type at least 2 characters to search.</p>
        ) : search.isLoading ? (
          <div className="flex items-center justify-center gap-2 py-4 text-slate-400">
            <Spinner />
            <span className="text-sm">Searching…</span>
          </div>
        ) : search.data && search.data.length > 0 ? (
          <ul className="max-h-64 divide-y divide-slate-800 overflow-y-auto rounded-lg border border-slate-800">
            {search.data.slice(0, 8).map((food) => (
              <li key={food.id}>
                <button
                  onClick={() => log.mutate(food)}
                  disabled={log.isPending}
                  className="flex w-full items-center justify-between gap-3 px-3 py-2.5 text-left hover:bg-slate-800 disabled:opacity-50"
                >
                  <span className="truncate text-sm font-medium text-slate-100">{food.name}</span>
                  <span className="shrink-0 text-xs text-slate-500">{round(food.kcalPer100g)} kcal/100g</span>
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="py-4 text-center text-sm text-slate-500">No foods found — try another search.</p>
        )}

        <button onClick={onFinish} className="block w-full text-center text-xs text-slate-500 hover:text-slate-300">
          I&apos;ll log later
        </button>
      </CardContent>
    </Card>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <Label>{label}</Label>
      {children}
    </div>
  );
}
