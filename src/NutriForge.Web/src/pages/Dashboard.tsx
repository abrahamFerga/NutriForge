import { Link } from "react-router-dom";
import {
  Bar,
  CartesianGrid,
  Cell,
  Line,
  ComposedChart,
  PolarAngleAxis,
  RadialBar,
  RadialBarChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { Flame } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { buttonClasses } from "@/components/ui/button";
import { MacroBar } from "@/components/MacroBar";
import { WeightCard } from "@/components/WeightCard";
import {
  EmptyState,
  ErrorState,
  LoadingState,
} from "@/components/StateMessage";
import { useDiary, useTargets, useTrend } from "@/hooks/useQueries";
import { round, today } from "@/lib/utils";

export function Dashboard() {
  const date = today();
  const targets = useTargets();
  const diary = useDiary(date);
  const trend = useTrend(7);

  if (targets.isLoading || diary.isLoading) {
    return <LoadingState label="Loading your dashboard…" />;
  }
  if (targets.isError) return <ErrorState error={targets.error} />;
  if (diary.isError) return <ErrorState error={diary.error} />;

  const target = targets.data;
  const consumed = diary.data?.consumed ?? {
    kcal: 0,
    proteinG: 0,
    fatG: 0,
    carbG: 0,
  };

  if (!target) {
    return (
      <div className="space-y-6">
        <PageHeading title="Dashboard" subtitle="Today at a glance" />
        <EmptyState title="No targets yet">
          <p>
            Set up your profile to compute personalized calorie and macro
            targets.
          </p>
          <Link to="/profile" className={buttonClasses("primary", "md", "mt-4")}>
            Go to Profile
          </Link>
        </EmptyState>
      </div>
    );
  }

  const kcalConsumed = round(consumed.kcal);
  const kcalTarget = round(target.kcal);
  const ringPct =
    kcalTarget > 0 ? Math.min((kcalConsumed / kcalTarget) * 100, 100) : 0;
  const remaining = Math.max(kcalTarget - kcalConsumed, 0);

  const ringData = [{ name: "kcal", value: ringPct, fill: "var(--color-brand-500)" }];

  return (
    <div className="space-y-6">
      <PageHeading title="Dashboard" subtitle="Today at a glance" />

      <div className="grid gap-6 lg:grid-cols-3">
        {/* Calorie ring */}
        <Card className="lg:col-span-1">
          <CardHeader>
            <CardTitle>Calories</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="relative h-56">
              <ResponsiveContainer width="100%" height="100%">
                <RadialBarChart
                  innerRadius="72%"
                  outerRadius="100%"
                  data={ringData}
                  startAngle={90}
                  endAngle={-270}
                >
                  <PolarAngleAxis
                    type="number"
                    domain={[0, 100]}
                    tick={false}
                  />
                  <RadialBar
                    dataKey="value"
                    cornerRadius={12}
                    background={{ fill: "#1e293b" }}
                  />
                </RadialBarChart>
              </ResponsiveContainer>
              <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
                <Flame className="mb-1 h-5 w-5 text-brand-400" />
                <span className="text-3xl font-bold text-slate-100">
                  {kcalConsumed}
                </span>
                <span className="text-xs text-slate-400">
                  of {kcalTarget} kcal
                </span>
                <span className="mt-1 text-xs font-medium text-brand-300">
                  {remaining} kcal left
                </span>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Macro bars */}
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Macros</CardTitle>
          </CardHeader>
          <CardContent className="space-y-5 pt-4">
            <MacroBar
              label="Protein"
              consumed={consumed.proteinG}
              target={target.proteinG}
              colorClass="bg-emerald-500"
            />
            <MacroBar
              label="Fat"
              consumed={consumed.fatG}
              target={target.fatG}
              colorClass="bg-amber-400"
            />
            <MacroBar
              label="Carbs"
              consumed={consumed.carbG}
              target={target.carbG}
              colorClass="bg-sky-400"
            />
            <p className="pt-1 text-xs text-slate-500">
              Targets via {target.formula}
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Weekly trend */}
      <Card>
        <CardHeader>
          <CardTitle>Last 7 days</CardTitle>
        </CardHeader>
        <CardContent>
          {trend.isLoading ? (
            <LoadingState label="Loading trend…" />
          ) : trend.isError ? (
            <ErrorState error={trend.error} />
          ) : !trend.data || trend.data.length === 0 ? (
            <EmptyState title="No history yet">
              <p>Log foods over the coming days to see your trend.</p>
            </EmptyState>
          ) : (
            <TrendChart
              data={trend.data.map((p) => ({
                day: p.date.slice(5),
                kcal: round(p.kcal),
                targetKcal: round(p.targetKcal),
              }))}
            />
          )}
        </CardContent>
      </Card>

      <WeightCard />
    </div>
  );
}

interface TrendDatum {
  day: string;
  kcal: number;
  targetKcal: number;
}

function TrendChart({ data }: { data: TrendDatum[] }) {
  return (
    <div className="h-64 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={data} margin={{ top: 8, right: 8, bottom: 0, left: -16 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" vertical={false} />
          <XAxis
            dataKey="day"
            stroke="#64748b"
            fontSize={12}
            tickLine={false}
            axisLine={false}
          />
          <YAxis
            stroke="#64748b"
            fontSize={12}
            tickLine={false}
            axisLine={false}
          />
          <Tooltip
            contentStyle={{
              background: "#0f172a",
              border: "1px solid #1e293b",
              borderRadius: 8,
              color: "#e2e8f0",
              fontSize: 12,
            }}
            cursor={{ fill: "rgba(148,163,184,0.08)" }}
          />
          <Bar dataKey="kcal" name="Consumed" radius={[4, 4, 0, 0]} maxBarSize={36}>
            {data.map((d, i) => (
              <Cell
                key={i}
                fill={
                  d.targetKcal > 0 && d.kcal > d.targetKcal
                    ? "#f59e0b"
                    : "var(--color-brand-500)"
                }
              />
            ))}
          </Bar>
          <Line
            dataKey="targetKcal"
            name="Target"
            stroke="#94a3b8"
            strokeWidth={2}
            strokeDasharray="5 4"
            dot={false}
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}

function PageHeading({
  title,
  subtitle,
}: {
  title: string;
  subtitle?: string;
}) {
  return (
    <div>
      <h1 className="text-2xl font-bold text-slate-100">{title}</h1>
      {subtitle ? <p className="text-sm text-slate-400">{subtitle}</p> : null}
    </div>
  );
}
