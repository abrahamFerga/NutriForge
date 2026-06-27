import type { ReactNode } from "react";
import { AlertCircle, RotateCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";

// Turn technical/empty failures into something a person can act on. API errors already arrive
// humanized (problem+json → formatProblem), so those pass through unchanged.
function humanizeError(error: unknown): string {
  const raw = error instanceof Error ? error.message.trim() : "";
  if (!raw || /failed to fetch|networkerror|load failed|err_/i.test(raw)) {
    return "We couldn't reach the server. Check your connection and try again.";
  }
  return raw;
}

export function LoadingState({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-16 text-slate-400">
      <Spinner />
      <span className="text-sm">{label}</span>
    </div>
  );
}

export function ErrorState({
  error,
  onRetry,
  title = "Something went wrong",
}: {
  error: unknown;
  /** When given, shows a "Try again" button (wire this to the query's refetch). */
  onRetry?: () => void;
  title?: string;
}) {
  return (
    <div
      role="alert"
      className="flex flex-col gap-3 rounded-xl border border-red-500/25 bg-red-950/30 px-4 py-3 text-sm text-red-200 backdrop-blur sm:flex-row sm:items-center sm:justify-between"
    >
      <div className="flex items-start gap-3">
        <span className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-lg bg-red-500/15 text-red-300">
          <AlertCircle className="h-4 w-4" />
        </span>
        <div className="min-w-0">
          <p className="font-medium text-red-100">{title}</p>
          <p className="mt-0.5 text-red-200/80">{humanizeError(error)}</p>
        </div>
      </div>
      {onRetry ? (
        <Button
          variant="outline"
          size="sm"
          onClick={onRetry}
          className="w-full shrink-0 border-red-500/30 bg-transparent text-red-100 hover:border-red-500/50 hover:bg-red-500/10 sm:w-auto"
        >
          <RotateCw className="h-4 w-4" />
          Try again
        </Button>
      ) : null}
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
