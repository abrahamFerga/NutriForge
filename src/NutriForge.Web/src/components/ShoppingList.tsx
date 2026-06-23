import { Check, ShoppingBasket } from "lucide-react";
import type { ShoppingListDto } from "@/lib/types";
import { round } from "@/lib/utils";

/**
 * Renders a {@link ShoppingListDto} grouped by aisle. Each item shows its total
 * grams, and an "in pantry" badge when the quantity is already covered.
 */
export function ShoppingList({ list }: { list: ShoppingListDto }) {
  if (list.aisles.length === 0) {
    return (
      <p className="py-6 text-center text-sm text-slate-500">
        Nothing to buy — your pantry has it all.
      </p>
    );
  }

  return (
    <div className="space-y-5">
      {list.aisles.map((aisle) => (
        <div key={aisle.aisle}>
          <h3 className="mb-2 flex items-center gap-1.5 text-xs font-semibold tracking-wide text-slate-400 uppercase">
            <ShoppingBasket className="h-3.5 w-3.5" />
            {aisle.aisle}
          </h3>
          <ul className="divide-y divide-slate-800 overflow-hidden rounded-lg border border-slate-800">
            {aisle.items.map((item) => (
              <li
                key={item.ingredientName}
                className="flex items-center justify-between gap-3 px-3 py-2 text-sm"
              >
                <span className="min-w-0 truncate text-slate-100">
                  {item.ingredientName}
                </span>
                <span className="flex shrink-0 items-center gap-2">
                  {item.pantryCovered ? (
                    <span className="flex items-center gap-1 rounded bg-emerald-500/15 px-1.5 py-0.5 text-[10px] font-medium text-emerald-300">
                      <Check className="h-3 w-3" />
                      in pantry
                    </span>
                  ) : null}
                  <span className="font-medium text-slate-300">
                    {round(item.grams)}g
                  </span>
                </span>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </div>
  );
}
