import { Bot, Send, Sparkles, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

interface AssistantPanelProps {
  open: boolean;
  onClose: () => void;
}

/**
 * Always-present "NutritionAssistant" chatbot drawer.
 * The assistant backend is not built yet, so this shows a friendly
 * placeholder and keeps the input disabled.
 */
export function AssistantPanel({ open, onClose }: AssistantPanelProps) {
  return (
    <>
      {/* Backdrop */}
      <div
        className={cn(
          "fixed inset-0 z-40 bg-black/50 transition-opacity",
          open ? "opacity-100" : "pointer-events-none opacity-0",
        )}
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Drawer */}
      <aside
        role="dialog"
        aria-label="Nutrition Assistant"
        aria-hidden={!open}
        className={cn(
          "fixed top-0 right-0 z-50 flex h-full w-full max-w-md flex-col border-l border-slate-800 bg-slate-900 shadow-2xl transition-transform duration-300",
          open ? "translate-x-0" : "translate-x-full",
        )}
      >
        <header className="flex items-center justify-between border-b border-slate-800 px-5 py-4">
          <div className="flex items-center gap-2">
            <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-brand-500/15 text-brand-400">
              <Bot className="h-5 w-5" />
            </span>
            <div>
              <p className="text-sm font-semibold text-slate-100">
                NutritionAssistant
              </p>
              <p className="text-xs text-slate-500">Your AI nutrition coach</p>
            </div>
          </div>
          <Button variant="ghost" size="icon" onClick={onClose} aria-label="Close assistant">
            <X className="h-5 w-5" />
          </Button>
        </header>

        <div className="flex flex-1 flex-col items-center justify-center gap-4 px-6 text-center">
          <span className="flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-500/10 text-brand-400">
            <Sparkles className="h-7 w-7" />
          </span>
          <div className="space-y-1">
            <p className="text-base font-semibold text-slate-100">
              Assistant coming soon
            </p>
            <p className="max-w-xs text-sm text-slate-400">
              It will log foods and answer questions about your nutrition,
              targets, and progress.
            </p>
          </div>
        </div>

        <footer className="border-t border-slate-800 p-4">
          <div className="flex items-center gap-2">
            <Input
              placeholder="Message the assistant…"
              disabled
              aria-disabled="true"
            />
            <Button size="icon" disabled aria-label="Send message">
              <Send className="h-4 w-4" />
            </Button>
          </div>
          <p className="mt-2 text-center text-xs text-slate-600">
            Chat is disabled until the assistant backend is available.
          </p>
        </footer>
      </aside>
    </>
  );
}
