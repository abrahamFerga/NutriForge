import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { CheckCircle2, ChevronDown, User } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeading } from "@/components/PageHeading";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState, LoadingState } from "@/components/StateMessage";
import { NotificationsPanel } from "@/components/NotificationsPanel";
import { ConsentPanel } from "@/components/ConsentPanel";
import { useProfile, useSaveProfile } from "@/hooks/useQueries";
import {
  ACTIVITY_LEVELS,
  GOALS,
  SEXES,
  type ActivityLevel,
  type Goal,
  type MacroStrategy,
  type ProfileDto,
  type Sex,
  type UpdateProfileRequest,
} from "@/lib/types";
import { cn } from "@/lib/utils";

interface FormState {
  sex: Sex;
  birthDate: string;
  heightCm: string;
  weightKg: string;
  bodyFatPct: string;
  activity: ActivityLevel;
  goal: Goal;
  macroStrategy: MacroStrategy;
  allergens: string;
  dislikes: string;
  preferredDiets: string;
}

const EMPTY: FormState = {
  sex: "Male",
  birthDate: "1990-01-01",
  heightCm: "175",
  weightKg: "75",
  bodyFatPct: "",
  activity: "ModeratelyActive",
  goal: "Maintain",
  macroStrategy: "ProteinAnchored",
  allergens: "",
  dislikes: "",
  preferredDiets: "",
};

function fromDto(p: ProfileDto): FormState {
  return {
    sex: p.sex,
    birthDate: p.birthDate,
    heightCm: String(p.heightCm),
    weightKg: String(p.weightKg),
    bodyFatPct: p.bodyFatPct === null ? "" : String(p.bodyFatPct),
    activity: p.activity,
    goal: p.goal,
    macroStrategy: p.macroStrategy,
    allergens: p.allergens.join(", "),
    dislikes: p.dislikes.join(", "),
    preferredDiets: p.preferredDiets.join(", "),
  };
}

function splitList(value: string): string[] {
  return value
    .split(",")
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

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

export function Profile() {
  const profileQuery = useProfile();
  const save = useSaveProfile();
  const [form, setForm] = useState<FormState>(EMPTY);
  const [prefsOpen, setPrefsOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);

  // Prefill once the profile resolves.
  useEffect(() => {
    if (profileQuery.data) {
      setForm(fromDto(profileQuery.data));
    }
  }, [profileQuery.data]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  function submit(e: React.FormEvent) {
    e.preventDefault();
    const bodyFat = form.bodyFatPct.trim();
    const body: UpdateProfileRequest = {
      sex: form.sex,
      birthDate: form.birthDate,
      heightCm: Number(form.heightCm),
      weightKg: Number(form.weightKg),
      bodyFatPct: bodyFat === "" ? null : Number(bodyFat),
      activity: form.activity,
      goal: form.goal,
      macroStrategy: form.macroStrategy,
      allergens: splitList(form.allergens),
      dislikes: splitList(form.dislikes),
      preferredDiets: splitList(form.preferredDiets),
    };
    save.mutate(body);
  }

  if (profileQuery.isLoading) return <LoadingState label="Loading profile…" />;
  if (profileQuery.isError)
    return <ErrorState error={profileQuery.error} onRetry={() => profileQuery.refetch()} />;

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <PageHeading
        title="Profile"
        subtitle="The basics — these set your daily calorie and protein goals."
        icon={User}
      />

      <form onSubmit={submit} className="space-y-6">
        {/* The essentials — everything needed to compute a target. */}
        <Card>
          <CardHeader>
            <CardTitle>About you</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <Field label="Sex">
              <Select
                value={form.sex}
                onChange={(e) => set("sex", e.target.value as Sex)}
              >
                {SEXES.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Birth date">
              <Input
                type="date"
                value={form.birthDate}
                onChange={(e) => set("birthDate", e.target.value)}
                required
              />
            </Field>

            <Field label="Height (cm)">
              <Input
                type="number"
                min={50}
                max={272}
                step={0.5}
                value={form.heightCm}
                onChange={(e) => set("heightCm", e.target.value)}
                required
              />
            </Field>

            <Field label="Weight (kg)">
              <Input
                type="number"
                min={20}
                max={500}
                step={0.1}
                value={form.weightKg}
                onChange={(e) => set("weightKg", e.target.value)}
                required
              />
            </Field>

            <Field label="How active are you?">
              <Select
                value={form.activity}
                onChange={(e) =>
                  set("activity", e.target.value as ActivityLevel)
                }
              >
                {ACTIVITY_LEVELS.map((a) => (
                  <option key={a} value={a}>
                    {ACTIVITY_LABELS[a]}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="What's your goal?">
              <Select
                value={form.goal}
                onChange={(e) => set("goal", e.target.value as Goal)}
              >
                {GOALS.map((g) => (
                  <option key={g} value={g}>
                    {GOAL_LABELS[g]}
                  </option>
                ))}
              </Select>
            </Field>
          </CardContent>
        </Card>

        {save.isError ? <ErrorState error={save.error} /> : null}

        <div className="flex flex-wrap items-center gap-3">
          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? <Spinner /> : null}
            Save profile
          </Button>
          {save.isSuccess ? (
            <>
              <span className="flex items-center gap-1.5 text-sm text-emerald-400">
                <CheckCircle2 className="h-4 w-4" />
                Saved — targets recomputed
              </span>
              <Link to="/" className="text-sm font-medium text-brand-400 hover:text-brand-300">
                View your targets →
              </Link>
            </>
          ) : null}
        </div>

        {/* Food preferences — most people never touch these. */}
        <Disclosure
          open={prefsOpen}
          onToggle={() => setPrefsOpen((v) => !v)}
          label="Allergens & food preferences"
        >
          <div className="mt-3 space-y-4">
            <Field label="Allergens">
              <Input
                value={form.allergens}
                onChange={(e) => set("allergens", e.target.value)}
                placeholder="peanuts, shellfish"
              />
            </Field>
            <Field label="Dislikes">
              <Input
                value={form.dislikes}
                onChange={(e) => set("dislikes", e.target.value)}
                placeholder="cilantro, liver"
              />
            </Field>
            <Field label="Preferred diets">
              <Input
                value={form.preferredDiets}
                onChange={(e) => set("preferredDiets", e.target.value)}
                placeholder="high-protein, mediterranean"
              />
            </Field>
          </div>
        </Disclosure>
      </form>

      {/* Account-level settings, out of the way. */}
      <Disclosure
        open={settingsOpen}
        onToggle={() => setSettingsOpen((v) => !v)}
        label="Notifications & privacy"
      >
        <div className="mt-4 space-y-6">
          <NotificationsPanel />
          <ConsentPanel />
        </div>
      </Disclosure>
    </div>
  );
}

function Disclosure({
  open,
  onToggle,
  label,
  children,
}: {
  open: boolean;
  onToggle: () => void;
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="border-t border-slate-800 pt-3">
      <button
        type="button"
        onClick={onToggle}
        className="flex w-full items-center justify-between text-sm font-medium text-slate-300 hover:text-slate-100"
        aria-expanded={open}
      >
        {label}
        <ChevronDown className={cn("h-4 w-4 transition-transform", open && "rotate-180")} />
      </button>
      {open ? children : null}
    </div>
  );
}

function Field({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <Label>{label}</Label>
      {children}
    </div>
  );
}
