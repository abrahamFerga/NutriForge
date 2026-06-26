import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ShieldCheck } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState, LoadingState } from "@/components/StateMessage";
import { consentApi } from "@/lib/api";
import type { ConsentStatus, ConsentType } from "@/lib/types";

const KEY = ["consent"] as const;

const LABELS: Record<ConsentType, string> = {
  TermsOfService: "Terms of Service",
  PrivacyPolicy: "Privacy Policy",
  HealthDataProcessing: "Health-data processing",
  Marketing: "Marketing emails",
};

/** View and manage consent (#58) — grant or withdraw (GDPR Art. 7) any tracked consent. */
export function ConsentPanel() {
  const qc = useQueryClient();
  const consent = useQuery({ queryKey: KEY, queryFn: () => consentApi.status() });

  const change = useMutation({
    mutationFn: ({ type, grant }: { type: ConsentType; grant: boolean }) =>
      grant ? consentApi.record(type, true) : consentApi.withdraw(type),
    onSuccess: (status) => qc.setQueryData(KEY, status),
  });

  if (consent.isLoading) return <LoadingState label="Loading consent…" />;
  if (consent.isError) return <ErrorState error={consent.error} />;

  const items = consent.data ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <ShieldCheck className="h-4 w-4 text-brand-400" />
          Privacy &amp; consent
        </CardTitle>
        <p className="text-xs text-slate-400">
          Manage how your data is used. You can withdraw consent at any time; your full consent history
          travels with your data export.
        </p>
      </CardHeader>
      <CardContent className="space-y-3">
        {change.isError ? <ErrorState error={change.error} /> : null}
        {items.map((c) => (
          <ConsentRow
            key={c.type}
            consent={c}
            pending={change.isPending && change.variables?.type === c.type}
            onToggle={(grant) => change.mutate({ type: c.type, grant })}
          />
        ))}
      </CardContent>
    </Card>
  );
}

function ConsentRow({
  consent,
  pending,
  onToggle,
}: {
  consent: ConsentStatus;
  pending: boolean;
  onToggle: (grant: boolean) => void;
}) {
  return (
    <div className="flex items-start justify-between gap-3 rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2.5">
      <div className="min-w-0">
        <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-slate-100">
          {LABELS[consent.type]}
          {consent.required ? (
            <span className="rounded bg-slate-700/60 px-1.5 py-0.5 text-[10px] font-medium text-slate-300">
              Required
            </span>
          ) : (
            <span className="rounded bg-slate-800 px-1.5 py-0.5 text-[10px] font-medium text-slate-400">
              Optional
            </span>
          )}
          {consent.granted ? (
            <span className="rounded bg-emerald-500/15 px-1.5 py-0.5 text-[10px] font-medium text-emerald-300">
              Granted
            </span>
          ) : consent.needsAction ? (
            <span className="rounded bg-amber-500/15 px-1.5 py-0.5 text-[10px] font-medium text-amber-300">
              Action needed
            </span>
          ) : (
            <span className="rounded bg-slate-800 px-1.5 py-0.5 text-[10px] font-medium text-slate-400">
              Not granted
            </span>
          )}
        </p>
        <p className="mt-0.5 text-xs text-slate-500">{consent.description}</p>
        <p className="mt-0.5 text-[0.65rem] text-slate-600">
          Lawful basis: {consent.lawfulBasis} · v{consent.version}
        </p>
      </div>
      <div className="shrink-0">
        {consent.granted ? (
          <Button
            variant="outline"
            size="sm"
            disabled={pending}
            onClick={() => onToggle(false)}
            title="Withdraw consent"
          >
            {pending ? <Spinner /> : "Withdraw"}
          </Button>
        ) : (
          <Button size="sm" disabled={pending} onClick={() => onToggle(true)}>
            {pending ? <Spinner /> : "Grant"}
          </Button>
        )}
      </div>
    </div>
  );
}
