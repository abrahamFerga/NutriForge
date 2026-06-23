import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Trash2 } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState, LoadingState } from "@/components/StateMessage";
import { pantryApi } from "@/lib/api";
import { queryKeys } from "@/lib/queryKeys";
import { round } from "@/lib/utils";

/**
 * Quick add/list pantry widget. Pantry items reduce the quantities the shopping
 * list says you need to buy (the "in pantry" coverage).
 */
export function PantryPanel() {
  const qc = useQueryClient();
  const [name, setName] = useState("");
  const [quantity, setQuantity] = useState("");
  const [unit, setUnit] = useState("");

  const pantry = useQuery({
    queryKey: queryKeys.pantry,
    queryFn: () => pantryApi.list(),
  });

  const add = useMutation({
    mutationFn: () =>
      pantryApi.add({
        ingredientName: name.trim(),
        quantity: Number(quantity) || 0,
        unit: unit.trim() || undefined,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.pantry });
      setName("");
      setQuantity("");
      setUnit("");
    },
  });

  const remove = useMutation({
    mutationFn: (id: string) => pantryApi.remove(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.pantry }),
  });

  const canAdd =
    name.trim().length > 0 && Number(quantity) > 0 && !add.isPending;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Pantry</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-2">
          <div className="flex items-end gap-2">
            <div className="flex-1 space-y-1">
              <Label htmlFor="pantry-name">Ingredient</Label>
              <Input
                id="pantry-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g. white rice"
              />
            </div>
            <div className="w-20 space-y-1">
              <Label htmlFor="pantry-qty">Qty</Label>
              <Input
                id="pantry-qty"
                type="number"
                min={0}
                step={0.1}
                value={quantity}
                onChange={(e) => setQuantity(e.target.value)}
              />
            </div>
            <div className="w-20 space-y-1">
              <Label htmlFor="pantry-unit">Unit</Label>
              <Input
                id="pantry-unit"
                value={unit}
                onChange={(e) => setUnit(e.target.value)}
                placeholder="g"
              />
            </div>
          </div>
          <Button
            size="sm"
            onClick={() => add.mutate()}
            disabled={!canAdd}
            className="w-full"
          >
            {add.isPending ? <Spinner /> : <Plus className="h-4 w-4" />}
            Add to pantry
          </Button>
          {add.isError ? <ErrorState error={add.error} /> : null}
        </div>

        {pantry.isLoading ? (
          <LoadingState label="Loading pantry…" />
        ) : pantry.isError ? (
          <ErrorState error={pantry.error} />
        ) : !pantry.data || pantry.data.length === 0 ? (
          <p className="py-4 text-center text-sm text-slate-500">
            Your pantry is empty.
          </p>
        ) : (
          <ul className="divide-y divide-slate-800 overflow-hidden rounded-lg border border-slate-800">
            {pantry.data.map((item) => (
              <li
                key={item.id}
                className="flex items-center justify-between gap-2 px-3 py-2 text-sm"
              >
                <span className="min-w-0 truncate text-slate-100">
                  {item.ingredientName}
                </span>
                <span className="flex shrink-0 items-center gap-2">
                  <span className="text-slate-400">{round(item.grams)}g</span>
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={`Remove ${item.ingredientName}`}
                    disabled={remove.isPending && remove.variables === item.id}
                    onClick={() => remove.mutate(item.id)}
                    className="text-slate-500 hover:text-red-400"
                  >
                    {remove.isPending && remove.variables === item.id ? (
                      <Spinner />
                    ) : (
                      <Trash2 className="h-4 w-4" />
                    )}
                  </Button>
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
