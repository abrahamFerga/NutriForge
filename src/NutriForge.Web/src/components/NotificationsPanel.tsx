import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, Link2, Send } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/StateMessage";
import { notificationsApi } from "@/lib/api";

const SUB_KEY = ["notifications", "subscription"] as const;
const OUTBOX_KEY = ["notifications", "outbox"] as const;

/**
 * Notification opt-in + secure account linking (#95). Toggle the daily calorie summary, pick the send
 * hour, and link a channel via a one-time code. (A real Telegram bot redeems the code in P2; in dev we
 * simulate the redemption with a mock chat address.)
 */
export function NotificationsPanel() {
  const qc = useQueryClient();
  const [code, setCode] = useState<string | null>(null);

  const sub = useQuery({
    queryKey: SUB_KEY,
    queryFn: () => notificationsApi.getSubscription("telegram"),
  });
  const outbox = useQuery({
    queryKey: OUTBOX_KEY,
    queryFn: () => notificationsApi.outbox(),
  });

  const update = useMutation({
    mutationFn: (patch: {
      enabled: boolean;
      sendHourUtc: number;
      weeklySummaryEnabled?: boolean;
      reminderHourUtc?: number | null;
    }) => notificationsApi.updateSubscription({ channel: "telegram", ...patch }),
    onSuccess: (s) => qc.setQueryData(SUB_KEY, s),
  });

  const link = useMutation({
    mutationFn: () => notificationsApi.createLink("telegram"),
    onSuccess: (l) => setCode(l.code),
  });

  const redeem = useMutation({
    mutationFn: (c: string) =>
      notificationsApi.redeem(c, `mock-${Math.random().toString(36).slice(2, 9)}`),
    onSuccess: () => {
      setCode(null);
      qc.invalidateQueries({ queryKey: SUB_KEY });
    },
  });

  const sendNow = useMutation({
    mutationFn: () => notificationsApi.sendNow(),
    onSuccess: () => qc.invalidateQueries({ queryKey: OUTBOX_KEY }),
  });

  const s = sub.data;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Bell className="h-4 w-4 text-brand-400" />
          Daily summary
        </CardTitle>
        <p className="text-xs text-slate-400">
          Get your calories vs. target sent to Telegram each day.
        </p>
      </CardHeader>
      <CardContent className="space-y-4">
        {sub.isLoading ? (
          <Spinner />
        ) : sub.isError ? (
          <ErrorState error={sub.error} />
        ) : s ? (
          <>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-200">
                <input
                  type="checkbox"
                  className="accent-brand-500"
                  checked={s.enabled}
                  disabled={update.isPending}
                  onChange={(e) =>
                    update.mutate({ enabled: e.target.checked, sendHourUtc: s.sendHourUtc })
                  }
                />
                Enabled
              </label>
              <div className="flex items-center gap-2">
                <Label htmlFor="send-hour" className="text-xs text-slate-400">
                  Send at (UTC)
                </Label>
                <Select
                  id="send-hour"
                  value={String(s.sendHourUtc)}
                  onChange={(e) =>
                    update.mutate({
                      enabled: s.enabled,
                      sendHourUtc: Number(e.target.value),
                    })
                  }
                  className="w-24"
                >
                  {Array.from({ length: 24 }, (_, h) => (
                    <option key={h} value={h}>
                      {String(h).padStart(2, "0")}:00
                    </option>
                  ))}
                </Select>
              </div>
            </div>

            <div className="rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2">
              <p className="flex items-center gap-2 text-sm">
                <Link2 className="h-4 w-4 text-slate-500" />
                {s.isLinked ? (
                  <span className="text-emerald-300">
                    Linked{s.address ? ` · ${s.address}` : ""}
                  </span>
                ) : (
                  <span className="text-slate-400">Not linked yet</span>
                )}
              </p>

              {code ? (
                <div className="mt-2 space-y-2">
                  <p className="text-xs text-slate-400">
                    Send this code to the bot to link (expires in 15 min):
                  </p>
                  <code className="block rounded bg-slate-900 px-2 py-1 font-mono text-sm text-brand-300">
                    {code}
                  </code>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => redeem.mutate(code)}
                    disabled={redeem.isPending}
                  >
                    {redeem.isPending ? <Spinner /> : null}
                    Simulate link (dev)
                  </Button>
                </div>
              ) : (
                <Button
                  size="sm"
                  variant="outline"
                  className="mt-2"
                  onClick={() => link.mutate()}
                  disabled={link.isPending}
                >
                  {link.isPending ? <Spinner /> : <Link2 className="h-4 w-4" />}
                  {s.isLinked ? "Re-link account" : "Link account"}
                </Button>
              )}
            </div>

            {update.isError ? <ErrorState error={update.error} /> : null}
            {link.isError ? <ErrorState error={link.error} /> : null}
            {redeem.isError ? <ErrorState error={redeem.error} /> : null}

            <Button
              variant="outline"
              onClick={() => sendNow.mutate()}
              disabled={sendNow.isPending}
            >
              {sendNow.isPending ? <Spinner /> : <Send className="h-4 w-4" />}
              Send my summary now
            </Button>

            {/* Weekly digest */}
            <div className="rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-3 space-y-2">
              <p className="text-xs font-medium tracking-wide text-slate-400 uppercase">
                Weekly digest
              </p>
              <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-200">
                <input
                  type="checkbox"
                  className="accent-brand-500"
                  checked={s.weeklySummaryEnabled ?? false}
                  disabled={update.isPending}
                  onChange={(e) =>
                    update.mutate({
                      enabled: s.enabled,
                      sendHourUtc: s.sendHourUtc,
                      weeklySummaryEnabled: e.target.checked,
                      reminderHourUtc: s.reminderHourUtc,
                    })
                  }
                />
                Send a 7-day progress summary every Sunday
              </label>
            </div>

            {/* Log reminder */}
            <div className="rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-3 space-y-2">
              <p className="text-xs font-medium tracking-wide text-slate-400 uppercase">
                Log reminder
              </p>
              <p className="text-xs text-slate-400">
                Nudge me to log my meals if I haven't tracked anything by this UTC hour.
              </p>
              <div className="flex items-center gap-3">
                <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-200">
                  <input
                    type="checkbox"
                    className="accent-brand-500"
                    checked={s.reminderHourUtc != null}
                    disabled={update.isPending}
                    onChange={(e) =>
                      update.mutate({
                        enabled: s.enabled,
                        sendHourUtc: s.sendHourUtc,
                        weeklySummaryEnabled: s.weeklySummaryEnabled,
                        reminderHourUtc: e.target.checked ? 12 : null,
                      })
                    }
                  />
                  Enabled
                </label>
                {s.reminderHourUtc != null && (
                  <Select
                    value={String(s.reminderHourUtc)}
                    onChange={(e) =>
                      update.mutate({
                        enabled: s.enabled,
                        sendHourUtc: s.sendHourUtc,
                        weeklySummaryEnabled: s.weeklySummaryEnabled,
                        reminderHourUtc: Number(e.target.value),
                      })
                    }
                    className="w-24"
                  >
                    {Array.from({ length: 24 }, (_, h) => (
                      <option key={h} value={h}>
                        {String(h).padStart(2, "0")}:00
                      </option>
                    ))}
                  </Select>
                )}
              </div>
            </div>

            {outbox.data && outbox.data.length > 0 ? (
              <div>
                <p className="mb-1.5 text-xs font-medium tracking-wide text-slate-500 uppercase">
                  Recent ({outbox.data[0].channel})
                </p>
                <ul className="space-y-1">
                  {outbox.data.slice(0, 3).map((m, i) => (
                    <li
                      key={i}
                      className="truncate rounded bg-slate-950/40 px-2 py-1 text-xs text-slate-300"
                    >
                      {m.body}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}
