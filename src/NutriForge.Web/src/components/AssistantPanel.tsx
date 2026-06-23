import { useEffect, useRef, useState } from "react";
import { Bot, Send, Sparkles, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { ApiError, assistantApi } from "@/lib/api";
import { cn } from "@/lib/utils";

interface AssistantPanelProps {
  open: boolean;
  onClose: () => void;
}

interface ChatMessage {
  role: "user" | "assistant";
  text: string;
}

/**
 * Always-present "NutritionAssistant" chatbot drawer — a live Microsoft Agent Framework
 * agent whose tools route through the same API the rest of the app uses, so every number
 * it states is computed by deterministic code, not the model. Degrades gracefully when no
 * chat provider is configured (the backend returns 503).
 */
export function AssistantPanel({ open, onClose }: AssistantPanelProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [sending, setSending] = useState(false);
  const [configured, setConfigured] = useState<boolean | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);

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
  }, [messages, sending]);

  async function send() {
    const text = input.trim();
    if (!text || sending) return;
    setInput("");
    setMessages((m) => [...m, { role: "user", text }]);
    setSending(true);
    try {
      const { reply } = await assistantApi.chat(text);
      setMessages((m) => [...m, { role: "assistant", text: reply }]);
    } catch (err) {
      const msg =
        err instanceof ApiError && err.status === 503
          ? "The assistant isn't configured yet — set the `Ai` provider + API key to enable it."
          : err instanceof Error
            ? err.message
            : "Something went wrong.";
      setMessages((m) => [...m, { role: "assistant", text: msg }]);
    } finally {
      setSending(false);
    }
  }

  async function clearChat() {
    setMessages([]);
    try {
      await assistantApi.clear();
    } catch {
      /* best-effort */
    }
  }

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
              <div className="space-y-1">
                <p className="text-base font-semibold text-slate-100">Ask about your nutrition</p>
                <p className="max-w-xs text-sm text-slate-400">
                  Try "How many calories do I have left today?", "What's my protein target?", or
                  "Find high-protein snacks."
                </p>
                {configured === false && (
                  <p className="mt-2 max-w-xs text-xs text-amber-400">
                    The assistant isn't configured yet — set the <code>Ai</code> provider + key to enable live chat.
                  </p>
                )}
              </div>
            </div>
          )}

          {messages.map((m, i) => (
            <div
              key={i}
              className={cn(
                "max-w-[85%] rounded-2xl px-4 py-2 text-sm",
                m.role === "user"
                  ? "ml-auto bg-brand-500/20 text-slate-100"
                  : "mr-auto bg-slate-800 text-slate-200",
              )}
            >
              {m.text}
            </div>
          ))}

          {sending && (
            <div className="mr-auto flex items-center gap-2 rounded-2xl bg-slate-800 px-4 py-2 text-sm text-slate-400">
              <Spinner className="h-4 w-4" /> thinking…
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
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="Message the assistant…"
              disabled={sending}
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
