import type { ReactNode } from "react";
import { AlertCircle } from "lucide-react";
import { Spinner } from "@/components/ui/spinner";

export function LoadingState({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-16 text-slate-400">
      <Spinner />
      <span className="text-sm">{label}</span>
    </div>
  );
}

export function ErrorState({ error }: { error: unknown }) {
  const message =
    error instanceof Error ? error.message : "Something went wrong.";
  return (
    <div className="flex items-start gap-3 rounded-xl border border-red-500/25 bg-red-950/30 px-4 py-3 text-sm text-red-200 backdrop-blur">
      <span className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-lg bg-red-500/15 text-red-300">
        <AlertCircle className="h-4 w-4" />
      </span>
      <span className="self-center">{message}</span>
    </div>
  );
}

export function EmptyState({
  title,
  children,
}: {
  title: string;
  children?: ReactNode;
}) {
  return (
    <div className="rounded-2xl border border-dashed border-slate-700/70 bg-slate-900/30 px-6 py-12 text-center backdrop-blur">
      <p className="text-base font-semibold text-slate-100">{title}</p>
      {children ? (
        <div className="mx-auto mt-2 max-w-md text-sm text-slate-400">{children}</div>
      ) : null}
    </div>
  );
}
