import { useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Bot, Check, Send, Sparkles, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { ApiError, assistantApi, diaryApi } from "@/lib/api";
import { queryKeys } from "@/lib/queryKeys";
import type { LogProposal } from "@/lib/types";
import { cn } from "@/lib/utils";

interface AssistantPanelProps {
  open: boolean;
  onClose: () => void;
}

interface ChatMessage {
  role: "user" | "assistant";
  text: string;
}

const NOT_CONFIGURED_MESSAGE =
  "The assistant isn't configured yet — set the `Ai` provider + API key to enable it.";

/** One-tap starter questions for the empty state — span the assistant's range (lookup + AI logging). */
const SUGGESTIONS = [
  "How many calories do I have left today?",
  "What's my protein target?",
  "Log 2 large eggs for breakfast",
];

/**
 * Always-present "NutritionAssistant" chatbot drawer — a live Microsoft Agent Framework
 * agent whose tools route through the same API the rest of the app uses, so every number
 * it states is computed by deterministic code, not the model. Replies stream token-by-token
 * and the agent may propose a diary log the user confirms inline. Degrades gracefully when
 * no chat provider is configured (the backend returns 503).
 */
export function AssistantPanel({ open, onClose }: AssistantPanelProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [sending, setSending] = useState(false);
  const [streaming, setStreaming] = useState(false);
  const [configured, setConfigured] = useState<boolean | null>(null);
  const [proposal, setProposal] = useState<LogProposal | null>(null);
  const [logging, setLogging] = useState(false);
  const [logResult, setLogResult] = useState<{ ok: boolean; message: string } | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const queryClient = useQueryClient();

  // Tapping a starter question drops it into the composer (focused) so the user can send it or tweak it
  // first — a lower-friction way in than typing from a blank box.
  const pickSuggestion = (s: string) => {
    setInput(s);
    inputRef.current?.focus();
  };

  useEffect(() => {
    if (open && configured === null) {
      assistantApi
        .status()
        .then((s) => setConfigured(s.configured))
        .catch(() => setConfigured(false));
    }
  }, [open, configured]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [messages, sending, proposal, logResult]);

  async function send() {
    const text = input.trim();
    if (!text || sending) return;
    setInput("");
    // A new turn supersedes any stale proposal.
    setProposal(null);
    setLogResult(null);
    setMessages((m) => [...m, { role: "user", text }]);
    setSending(true);
    setStreaming(false);

    // Append an empty assistant message we grow as tokens arrive.
    let assistantIndex = -1;
    setMessages((m) => {
      assistantIndex = m.length;
      return [...m, { role: "assistant", text: "" }];
    });

    const appendToAssistant = (chunk: string) => {
      setStreaming(true);
      setMessages((m) => {
        const next = [...m];
        const current = next[assistantIndex];
        if (current && current.role === "assistant") {
          next[assistantIndex] = { ...current, text: current.text + chunk };
        }
        return next;
      });
    };

    try {
      await assistantApi.chatStream(
        text,
        appendToAssistant,
        (p) => setProposal(p),
      );
    } catch (err) {
      const msg =
        err instanceof ApiError && err.status === 503
          ? NOT_CONFIGURED_MESSAGE
          : err instanceof Error
            ? err.message
            : "Something went wrong.";
      setMessages((m) => {
        const next = [...m];
        const current = next[assistantIndex];
        // Reuse the placeholder if nothing streamed; otherwise append.
        if (current && current.role === "assistant" && current.text === "") {
          next[assistantIndex] = { role: "assistant", text: msg };
        } else {
          next.push({ role: "assistant", text: msg });
        }
        return next;
      });
    } finally {
      setSending(false);
      setStreaming(false);
    }
  }

  async function confirmProposal() {
    if (!proposal || logging) return;
    setLogging(true);
    try {
      await diaryApi.create({
        date: proposal.date,
        mealSlot: proposal.mealSlot,
        foodId: proposal.foodId,
        portionId: proposal.portionId,
        quantity: proposal.quantity,
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.diaryAll }),
        queryClient.invalidateQueries({ queryKey: queryKeys.targets }),
      ]);
      setProposal(null);
      setLogResult({ ok: true, message: "Logged ✓" });
    } catch (err) {
      const message = err instanceof Error ? err.message : "Couldn't log that.";
      setLogResult({ ok: false, message });
    } finally {
      setLogging(false);
    }
  }

  function dismissProposal() {
    setProposal(null);
  }

  async function clearChat() {
    setMessages([]);
    setProposal(null);
    setLogResult(null);
    try {
      await assistantApi.clear();
    } catch {
      /* best-effort */
    }
  }

  // Spinner shows only while waiting for the first token of a turn.
  const showThinking = sending && !streaming;

  return (
    <>
      <div
        className={cn(
          "fixed inset-0 z-40 bg-black/50 transition-opacity",
          open ? "opacity-100" : "pointer-events-none opacity-0",
        )}
        onClick={onClose}
        aria-hidden="true"
      />

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
              <p className="text-sm font-semibold text-slate-100">NutritionAssistant</p>
              <p className="text-xs text-slate-500">Your AI nutrition coach</p>
            </div>
          </div>
          <div className="flex items-center gap-1">
            {messages.length > 0 && (
              <Button variant="ghost" size="icon" onClick={clearChat} aria-label="Clear conversation">
                <Trash2 className="h-4 w-4" />
              </Button>
            )}
            <Button variant="ghost" size="icon" onClick={onClose} aria-label="Close assistant">
              <X className="h-5 w-5" />
            </Button>
          </div>
        </header>

        <div ref={scrollRef} className="flex-1 space-y-3 overflow-y-auto px-5 py-4">
          {messages.length === 0 && (
            <div className="flex h-full flex-col items-center justify-center gap-4 text-center">
              <span className="flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-500/10 text-brand-400">
                <Sparkles className="h-7 w-7" />
              </span>
              <div className="space-y-3">
                <p className="text-base font-semibold text-slate-100">Ask about your nutrition</p>
                <p className="max-w-xs text-sm text-slate-400">Tap a question to start, or type your own.</p>
                <div className="flex flex-wrap justify-center gap-2">
                  {SUGGESTIONS.map((s) => (
                    <button
                      key={s}
                      type="button"
                      onClick={() => pickSuggestion(s)}
                      className="rounded-full border border-slate-700 bg-slate-900/60 px-3 py-1.5 text-xs text-slate-300 transition-colors hover:border-brand-500/50 hover:bg-brand-500/10 hover:text-brand-200"
                    >
                      {s}
                    </button>
                  ))}
                </div>
                {configured === false && (
                  <p className="mt-2 max-w-xs text-xs text-amber-400">
                    The assistant isn't configured yet — set the <code>Ai</code> provider + key to enable live chat.
                  </p>
                )}
              </div>
            </div>
          )}

          {messages.map((m, i) => {
            // Hide the empty assistant placeholder while the spinner is shown.
            if (m.role === "assistant" && m.text === "") return null;
            return (
              <div
                key={i}
                className={cn(
                  "max-w-[85%] whitespace-pre-wrap rounded-2xl px-4 py-2 text-sm",
                  m.role === "user"
                    ? "ml-auto bg-brand-500/20 text-slate-100"
                    : "mr-auto bg-slate-800 text-slate-200",
                )}
              >
                {m.text}
              </div>
            );
          })}

          {showThinking && (
            <div className="mr-auto flex items-center gap-2 rounded-2xl bg-slate-800 px-4 py-2 text-sm text-slate-400">
              <Spinner className="h-4 w-4" /> thinking…
            </div>
          )}

          {proposal && (
            <ProposalCard
              proposal={proposal}
              logging={logging}
              onConfirm={confirmProposal}
              onDismiss={dismissProposal}
            />
          )}

          {logResult && (
            <div
              role="status"
              className={cn(
                "mr-auto flex items-center gap-1.5 rounded-2xl px-4 py-2 text-sm font-medium",
                logResult.ok
                  ? "bg-brand-500/15 text-brand-300"
                  : "bg-rose-500/15 text-rose-300",
              )}
            >
              {logResult.ok && <Check className="h-4 w-4" />}
              {logResult.message}
            </div>
          )}
        </div>

        <footer className="border-t border-slate-800 p-4">
          <form
            className="flex items-center gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              void send();
            }}
          >
            <Input
              ref={inputRef}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="Message the assistant…"
              disabled={sending}
              aria-label="Message the assistant"
            />
            <Button type="submit" size="icon" disabled={sending || !input.trim()} aria-label="Send message">
              <Send className="h-4 w-4" />
            </Button>
          </form>
          <p className="mt-2 text-center text-xs text-slate-600">
            The assistant only states numbers computed by the app, never guesses.
          </p>
        </footer>
      </aside>
    </>
  );
}

interface ProposalCardProps {
  proposal: LogProposal;
  logging: boolean;
  onConfirm: () => void;
  onDismiss: () => void;
}

/** Compact inline card asking the user to confirm a proposed diary log. */
function ProposalCard({ proposal, logging, onConfirm, onDismiss }: ProposalCardProps) {
  const qty = Number.isInteger(proposal.quantity)
    ? proposal.quantity
    : proposal.quantity.toFixed(2).replace(/\.?0+$/, "");
  const kcal = Math.round(proposal.kcal);

  return (
    <div className="mr-auto w-[85%] rounded-2xl border border-brand-500/30 bg-slate-800/80 px-4 py-3 text-sm">
      <p className="text-slate-200">
        Log <span className="font-semibold text-slate-100">{qty} {proposal.portionName} {proposal.foodName}</span>
        {" · "}
        <span className="text-brand-300">{kcal} kcal</span>
        {" to "}
        <span className="font-medium text-slate-100">{proposal.mealSlot}</span>?
      </p>
      <p className="mt-1 text-xs text-slate-500">
        {Math.round(proposal.grams)} g · {Math.round(proposal.proteinG)}P / {Math.round(proposal.fatG)}F /{" "}
        {Math.round(proposal.carbG)}C · {proposal.date}
      </p>
      <div className="mt-3 flex items-center gap-2">
        <Button
          size="sm"
          onClick={onConfirm}
          disabled={logging}
          aria-label="Confirm diary log"
        >
          {logging ? <Spinner className="h-4 w-4" /> : <Check className="h-4 w-4" />}
          Confirm
        </Button>
        <Button
          size="sm"
          variant="ghost"
          onClick={onDismiss}
          disabled={logging}
          aria-label="Dismiss proposed log"
        >
          Dismiss
        </Button>
      </div>
    </div>
  );
}
