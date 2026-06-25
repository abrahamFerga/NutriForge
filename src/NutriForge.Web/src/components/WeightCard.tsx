import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { Plus, Scale } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/StateMessage";
import { measurementsApi } from "@/lib/api";
import { round, today } from "@/lib/utils";

const KEY = ["measurements", 90] as const;

/** Log weight (+ optional body fat) and see the trend over time (#71). */
export function WeightCard() {
  const qc = useQueryClient();
  const [weight, setWeight] = useState("");
  const [bodyFat, setBodyFat] = useState("");

  const history = useQuery({
    queryKey: KEY,
    queryFn: () => measurementsApi.history(90),
  });

  const log = useMutation({
    mutationFn: () =>
      measurementsApi.log({
        date: today(),
        weightKg: Number(weight),
        bodyFatPct: bodyFat.trim() ? Number(bodyFat) : null,
      }),
    onSuccess: () => {
      setWeight("");
      setBodyFat("");
      qc.invalidateQueries({ queryKey: KEY });
    },
  });

  const rows = history.data ?? [];
  const data = rows.map((m) => ({ date: m.date.slice(5), weightKg: m.weightKg }));
  const latest = rows.length ? rows[rows.length - 1] : null;
  const first = rows.length ? rows[0] : null;
  const delta = latest && first ? latest.weightKg - first.weightKg : 0;
  const canLog = Number(weight) > 0 && !log.isPending;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Scale className="h-4 w-4 text-brand-400" />
          Weight
        </CardTitle>
        {latest ? (
          <p className="text-xs text-slate-400">
            {round(latest.weightKg, 1)} kg
            {first && data.length > 1 ? (
              <span className={delta <= 0 ? "text-emerald-400" : "text-amber-400"}>
                {" · "}
                {delta >= 0 ? "+" : ""}
                {round(delta, 1)} kg over {data.length} entries
              </span>
            ) : null}
          </p>
        ) : null}
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="flex items-end gap-2">
          <div className="flex-1 space-y-1">
            <label className="text-xs text-slate-400" htmlFor="w-kg">
              Weight (kg)
            </label>
            <Input
              id="w-kg"
              type="number"
              inputMode="decimal"
              min={0}
              step={0.1}
              value={weight}
              onChange={(e) => setWeight(e.target.value)}
              placeholder="e.g. 80.5"
            />
          </div>
          <div className="w-24 space-y-1">
            <label className="text-xs text-slate-400" htmlFor="w-bf">
              Body fat %
            </label>
            <Input
              id="w-bf"
              type="number"
              inputMode="decimal"
              min={0}
              step={0.1}
              value={bodyFat}
              onChange={(e) => setBodyFat(e.target.value)}
              placeholder="opt."
            />
          </div>
          <Button onClick={() => log.mutate()} disabled={!canLog}>
            {log.isPending ? <Spinner /> : <Plus className="h-4 w-4" />}
            Log
          </Button>
        </div>

        {log.isError ? <ErrorState error={log.error} /> : null}

        {data.length > 1 ? (
          <div className="h-44">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={data} margin={{ top: 5, right: 8, left: -16, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" />
                <XAxis dataKey="date" tick={{ fontSize: 11, fill: "#64748b" }} />
                <YAxis domain={["auto", "auto"]} width={40} tick={{ fontSize: 11, fill: "#64748b" }} />
                <Tooltip
                  contentStyle={{
                    background: "#0f172a",
                    border: "1px solid #1e293b",
                    borderRadius: 8,
                    fontSize: 12,
                  }}
                />
                <Line type="monotone" dataKey="weightKg" stroke="#22d3ee" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        ) : data.length === 1 ? (
          <p className="text-xs text-slate-500">Log a few more days to see your trend.</p>
        ) : (
          <p className="text-xs text-slate-500">No weight logged yet — add today's above.</p>
        )}
      </CardContent>
    </Card>
  );
}
