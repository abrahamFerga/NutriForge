import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Star, Zap } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ErrorState } from "@/components/StateMessage";
import { diaryApi, favoritesApi } from "@/lib/api";
import type { MealSlot, QuickAddFood } from "@/lib/types";
import { round } from "@/lib/utils";

const RECENTS_KEY = ["diary", "recents"] as const;
const FAVORITES_KEY = ["favorites"] as const;

/** One-tap re-logging of favorite + recent foods into the current meal/date (#69). */
export function QuickAddCard({
  date,
  mealSlot,
  onLogged,
}: {
  date: string;
  mealSlot: MealSlot;
  onLogged: () => void;
}) {
  const qc = useQueryClient();
  const recents = useQuery({ queryKey: RECENTS_KEY, queryFn: () => diaryApi.recents(12) });
  const favorites = useQuery({ queryKey: FAVORITES_KEY, queryFn: () => favoritesApi.list() });

  const log = useMutation({
    mutationFn: (f: QuickAddFood) =>
      diaryApi.create({ date, mealSlot, foodId: f.foodId, portionId: f.portionId, quantity: f.quantity }),
    onSuccess: () => {
      onLogged();
      qc.invalidateQueries({ queryKey: RECENTS_KEY });
    },
  });

  const toggleFav = useMutation({
    mutationFn: ({ foodId, fav }: { foodId: string; fav: boolean }) =>
      fav ? favoritesApi.remove(foodId) : favoritesApi.add(foodId),
    onSuccess: () => qc.invalidateQueries({ queryKey: FAVORITES_KEY }),
  });

  const favIds = new Set((favorites.data ?? []).map((f) => f.foodId));
  // Favorites first, then recents not already starred.
  const items = [
    ...(favorites.data ?? []),
    ...(recents.data ?? []).filter((r) => !favIds.has(r.foodId)),
  ];

  if (items.length === 0) return null; // nothing to quick-add yet

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Zap className="h-4 w-4 text-brand-400" />
          Quick add
          <span className="text-xs font-normal text-slate-500">→ {mealSlot}</span>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <div className="flex flex-wrap gap-2">
          {items.map((f) => {
            const fav = favIds.has(f.foodId);
            return (
              <div
                key={f.foodId}
                className="flex items-center gap-1 rounded-full border border-slate-700 bg-slate-900/60 pr-1 text-sm"
              >
                <button
                  onClick={() => log.mutate(f)}
                  disabled={log.isPending}
                  className="rounded-full py-1 pl-3 pr-1 text-slate-100 hover:text-brand-300 disabled:opacity-50"
                  title={`Log ${round(f.quantity, 2)} ${f.portionName} to ${mealSlot}`}
                >
                  {f.foodName}
                  <span className="ml-1.5 text-xs text-slate-500">{round(f.kcal)} kcal</span>
                </button>
                <button
                  onClick={() => toggleFav.mutate({ foodId: f.foodId, fav })}
                  disabled={toggleFav.isPending}
                  aria-label={fav ? "Remove from favorites" : "Add to favorites"}
                  className="rounded-full p-1"
                >
                  <Star
                    className={
                      fav ? "h-3.5 w-3.5 fill-amber-400 text-amber-400" : "h-3.5 w-3.5 text-slate-500"
                    }
                  />
                </button>
              </div>
            );
          })}
        </div>
        {log.isError ? <ErrorState error={log.error} /> : null}
      </CardContent>
    </Card>
  );
}
