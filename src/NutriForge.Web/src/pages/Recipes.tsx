import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  Check,
  Clock,
  Plus,
  Search,
  Sparkles,
  Trash2,
  Utensils,
  Youtube,
} from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState, LoadingState } from "@/components/StateMessage";
import { useDebounced } from "@/hooks/useQueries";
import { ApiError, recipesApi } from "@/lib/api";
import { queryKeys } from "@/lib/queryKeys";
import type {
  CreateRecipeIngredient,
  ImportPreviewDto,
  RecipeSummary,
} from "@/lib/types";
import { round } from "@/lib/utils";

export function Recipes() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [showImport, setShowImport] = useState(false);
  const [importDraft, setImportDraft] = useState<ImportPreviewDto | null>(null);

  if (selectedId) {
    return (
      <RecipeDetail id={selectedId} onBack={() => setSelectedId(null)} />
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-slate-100">Recipes</h1>
          <p className="text-sm text-slate-400">
            Browse recipes and their per-serving nutrition
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            onClick={() => {
              setShowImport((v) => !v);
              setShowForm(false);
              setImportDraft(null);
            }}
          >
            <Youtube className="h-4 w-4" />
            Import
          </Button>
          <Button
            onClick={() => {
              setShowForm((v) => !v);
              setShowImport(false);
              setImportDraft(null);
            }}
          >
            <Plus className="h-4 w-4" />
            New recipe
          </Button>
        </div>
      </div>

      {showImport && !importDraft ? (
        <ImportPanel
          onPreview={(d) => {
            setImportDraft(d);
            setShowImport(false);
          }}
        />
      ) : null}

      {importDraft ? (
        <NewRecipeForm
          draft={importDraft}
          onCreated={() => setImportDraft(null)}
          onCancel={() => setImportDraft(null)}
        />
      ) : showForm ? (
        <NewRecipeForm onCreated={() => setShowForm(false)} />
      ) : null}

      <RecipeList onSelect={setSelectedId} />
    </div>
  );
}

// -------------------- Import panel --------------------

function ImportPanel({ onPreview }: { onPreview: (d: ImportPreviewDto) => void }) {
  const [url, setUrl] = useState("");
  const [text, setText] = useState("");

  const preview = useMutation({
    mutationFn: () =>
      recipesApi.importPreview({
        url: url.trim() || undefined,
        text: text.trim() || undefined,
      }),
    onSuccess: onPreview,
  });

  const noProvider =
    preview.error instanceof ApiError && preview.error.status === 503;
  const canSubmit =
    (url.trim().length > 0 || text.trim().length > 0) && !preview.isPending;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Import a recipe</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="imp-url">YouTube or recipe URL</Label>
          <Input
            id="imp-url"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            placeholder="https://www.youtube.com/watch?v=…"
          />
        </div>
        <div className="space-y-1">
          <Label htmlFor="imp-text">…or paste the recipe text</Label>
          <textarea
            id="imp-text"
            value={text}
            onChange={(e) => setText(e.target.value)}
            rows={4}
            placeholder="Paste ingredients + steps — handy when a video has no description"
            className="flex w-full rounded-lg border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus-visible:border-brand-500 focus-visible:ring-2 focus-visible:ring-brand-500/30 focus-visible:outline-none"
          />
        </div>

        {noProvider ? (
          <p className="rounded-lg border border-amber-900/60 bg-amber-950/40 px-4 py-3 text-sm text-amber-200">
            Recipe import needs an AI provider — set OPENAI_API_KEY (and a
            YouTube&nbsp;Data&nbsp;API key to auto-read video descriptions).
          </p>
        ) : preview.isError ? (
          <ErrorState error={preview.error} />
        ) : null}

        <Button
          onClick={() => preview.mutate()}
          disabled={!canSubmit}
          className="w-full"
        >
          {preview.isPending ? <Spinner /> : <Sparkles className="h-4 w-4" />}
          Extract recipe
        </Button>
        <p className="text-xs text-slate-500">
          We read the video title + description (or your pasted text), extract
          the recipe, and compute calories from the catalog. You review &amp;
          confirm before saving.
        </p>
      </CardContent>
    </Card>
  );
}

// -------------------- Recipe list + search --------------------

function RecipeList({ onSelect }: { onSelect: (id: string) => void }) {
  const [term, setTerm] = useState("");
  const debounced = useDebounced(term, 350);

  const recipes = useQuery({
    queryKey: queryKeys.recipeSearch(debounced.trim()),
    queryFn: ({ signal }) => recipesApi.search(debounced.trim(), signal),
  });

  return (
    <div className="space-y-4">
      <div className="relative max-w-md">
        <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-slate-500" />
        <Input
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="Search recipes…"
          className="pl-9"
          aria-label="Search recipes"
        />
      </div>

      {recipes.isLoading ? (
        <LoadingState label="Loading recipes…" />
      ) : recipes.isError ? (
        <ErrorState error={recipes.error} />
      ) : !recipes.data || recipes.data.length === 0 ? (
        <p className="py-10 text-center text-sm text-slate-500">
          No recipes found. Create one to get started.
        </p>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {recipes.data.map((recipe) => (
            <RecipeCard
              key={recipe.id}
              recipe={recipe}
              onSelect={() => onSelect(recipe.id)}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function RecipeCard({
  recipe,
  onSelect,
}: {
  recipe: RecipeSummary;
  onSelect: () => void;
}) {
  return (
    <button
      onClick={onSelect}
      className="flex flex-col rounded-xl border border-slate-800 bg-slate-900/60 p-4 text-left shadow-lg shadow-black/20 backdrop-blur transition-colors hover:border-brand-500/40 hover:bg-slate-900"
    >
      <div className="flex items-start justify-between gap-2">
        <p className="font-semibold text-slate-100">{recipe.name}</p>
        {recipe.isNutritionComputed ? (
          <span className="flex shrink-0 items-center gap-1 rounded bg-emerald-500/15 px-1.5 py-0.5 text-[10px] font-medium text-emerald-300">
            <Check className="h-3 w-3" />
            computed
          </span>
        ) : null}
      </div>

      <div className="mt-2 flex items-center gap-3 text-xs text-slate-500">
        <span className="flex items-center gap-1">
          <Utensils className="h-3.5 w-3.5" />
          {recipe.servings} servings
        </span>
        <span className="flex items-center gap-1">
          <Clock className="h-3.5 w-3.5" />
          {recipe.totalMinutes} min
        </span>
      </div>

      <div className="mt-3 grid grid-cols-4 gap-1 text-center text-xs">
        <Macro label="kcal" value={round(recipe.kcalPerServing)} />
        <Macro label="P" value={round(recipe.proteinPerServing)} />
        <Macro label="F" value={round(recipe.fatPerServing)} />
        <Macro label="C" value={round(recipe.carbPerServing)} />
      </div>

      {recipe.tags.length > 0 ? (
        <div className="mt-3 flex flex-wrap gap-1.5">
          {recipe.tags.map((tag) => (
            <span
              key={tag}
              className="rounded bg-slate-800 px-1.5 py-0.5 text-[10px] font-medium text-slate-300"
            >
              {tag}
            </span>
          ))}
        </div>
      ) : null}

      <p className="mt-3 text-[11px] text-slate-600">per serving</p>
    </button>
  );
}

function Macro({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded bg-slate-950/50 py-1.5">
      <p className="font-semibold text-slate-100">{value}</p>
      <p className="text-[10px] text-slate-500">{label}</p>
    </div>
  );
}

// -------------------- Recipe detail --------------------

function RecipeDetail({ id, onBack }: { id: string; onBack: () => void }) {
  const recipe = useQuery({
    queryKey: queryKeys.recipe(id),
    queryFn: () => recipesApi.get(id),
  });

  return (
    <div className="space-y-6">
      <Button variant="ghost" size="sm" onClick={onBack}>
        <ArrowLeft className="h-4 w-4" />
        Back to recipes
      </Button>

      {recipe.isLoading ? (
        <LoadingState label="Loading recipe…" />
      ) : recipe.isError ? (
        <ErrorState error={recipe.error} />
      ) : recipe.data ? (
        <>
          <div>
            <h1 className="text-2xl font-bold text-slate-100">
              {recipe.data.name}
            </h1>
            <div className="mt-1 flex items-center gap-3 text-sm text-slate-400">
              <span className="flex items-center gap-1">
                <Utensils className="h-4 w-4" />
                {recipe.data.servings} servings
              </span>
              <span className="flex items-center gap-1">
                <Clock className="h-4 w-4" />
                {recipe.data.totalMinutes} min
              </span>
            </div>
          </div>

          {recipe.data.sourceVideoId ? (
            <Card>
              <CardContent className="p-0">
                <div className="aspect-video w-full overflow-hidden rounded-lg">
                  <iframe
                    className="h-full w-full"
                    src={`https://www.youtube.com/embed/${recipe.data.sourceVideoId}`}
                    title={recipe.data.name}
                    allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                    allowFullScreen
                  />
                </div>
              </CardContent>
            </Card>
          ) : recipe.data.sourceUrl ? (
            <a
              href={recipe.data.sourceUrl}
              target="_blank"
              rel="noreferrer"
              className="inline-block text-sm font-medium text-brand-400 hover:underline"
            >
              View source ↗
            </a>
          ) : null}

          <div className="grid gap-6 lg:grid-cols-5">
            <div className="lg:col-span-3">
              <Card>
                <CardHeader>
                  <CardTitle>Ingredients</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="overflow-x-auto rounded-lg border border-slate-800">
                    <table className="w-full text-sm">
                      <thead>
                        <tr className="text-xs text-slate-500">
                          <th className="px-3 py-2 text-left font-medium">
                            Ingredient
                          </th>
                          <th className="px-3 py-2 text-right font-medium">
                            Grams
                          </th>
                          <th className="px-3 py-2 text-right font-medium">
                            kcal
                          </th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-slate-800">
                        {recipe.data.ingredients.map((ing, i) => (
                          <tr key={`${ing.name}-${i}`}>
                            <td className="px-3 py-2">
                              <p className="text-slate-100">{ing.name}</p>
                              <p className="text-xs text-slate-500">
                                {round(ing.quantity, 2)}
                                {ing.unit ? ` ${ing.unit}` : ""}
                                {ing.resolved ? null : (
                                  <span className="ml-1.5 text-amber-400">
                                    · unresolved
                                  </span>
                                )}
                              </p>
                            </td>
                            <td className="px-3 py-2 text-right whitespace-nowrap text-slate-300">
                              {round(ing.grams)}g
                            </td>
                            <td className="px-3 py-2 text-right whitespace-nowrap text-slate-300">
                              {round(ing.kcal)}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </CardContent>
              </Card>

              {recipe.data.instructions ? (
                <Card className="mt-6">
                  <CardHeader>
                    <CardTitle>Instructions</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <p className="text-sm whitespace-pre-wrap text-slate-300">
                      {recipe.data.instructions}
                    </p>
                  </CardContent>
                </Card>
              ) : null}
            </div>

            <div className="lg:col-span-2">
              <Card>
                <CardHeader>
                  <CardTitle>Per serving</CardTitle>
                </CardHeader>
                <CardContent className="space-y-2 text-sm">
                  <NutritionRow
                    label="Calories"
                    value={`${round(recipe.data.kcalPerServing)} kcal`}
                  />
                  <NutritionRow
                    label="Protein"
                    value={`${round(recipe.data.proteinPerServing)} g`}
                  />
                  <NutritionRow
                    label="Fat"
                    value={`${round(recipe.data.fatPerServing)} g`}
                  />
                  <NutritionRow
                    label="Carbs"
                    value={`${round(recipe.data.carbPerServing)} g`}
                  />
                  {recipe.data.tags.length > 0 ? (
                    <div className="flex flex-wrap gap-1.5 pt-2">
                      {recipe.data.tags.map((tag) => (
                        <span
                          key={tag}
                          className="rounded bg-slate-800 px-1.5 py-0.5 text-[10px] font-medium text-slate-300"
                        >
                          {tag}
                        </span>
                      ))}
                    </div>
                  ) : null}
                </CardContent>
              </Card>
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}

function NutritionRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between border-b border-slate-800 pb-2 last:border-0 last:pb-0">
      <span className="text-slate-400">{label}</span>
      <span className="font-semibold text-slate-100">{value}</span>
    </div>
  );
}

// -------------------- New recipe form --------------------

interface IngredientLine {
  quantity: string;
  unit: string;
  name: string;
}

function emptyLine(): IngredientLine {
  return { quantity: "", unit: "", name: "" };
}

function NewRecipeForm({
  onCreated,
  draft,
  onCancel,
}: {
  onCreated: () => void;
  draft?: ImportPreviewDto;
  onCancel?: () => void;
}) {
  const qc = useQueryClient();
  const d = draft?.draft;
  const [name, setName] = useState(d?.name ?? "");
  const [servings, setServings] = useState(String(d?.servings ?? 2));
  const [minutes, setMinutes] = useState(String(d?.totalMinutes ?? 30));
  const [tags, setTags] = useState((d?.tags ?? []).join(", "));
  const [instructions, setInstructions] = useState(d?.instructions ?? "");
  const [lines, setLines] = useState<IngredientLine[]>(
    d && d.ingredients.length > 0
      ? d.ingredients.map((i) => ({
          quantity: i.quantity ? String(i.quantity) : "",
          unit: i.unit ?? "",
          name: i.name,
        }))
      : [emptyLine()],
  );

  const create = useMutation({
    mutationFn: () => {
      const ingredients: CreateRecipeIngredient[] = lines
        .filter((l) => l.name.trim().length > 0)
        .map((l) => ({
          quantity: Number(l.quantity) || 0,
          unit: l.unit.trim() || undefined,
          name: l.name.trim(),
        }));
      return recipesApi.create({
        name: name.trim(),
        servings: Number(servings) || 1,
        totalMinutes: Number(minutes) || 0,
        instructions: instructions.trim() || undefined,
        tags: tags
          .split(",")
          .map((t) => t.trim())
          .filter((t) => t.length > 0),
        ingredients,
        // Carry provenance through when saving an imported draft.
        sourceUrl: d?.sourceUrl ?? undefined,
        sourceType: d?.sourceType ?? undefined,
        sourceVideoId: d?.sourceVideoId ?? undefined,
        thumbnailUrl: d?.thumbnailUrl ?? undefined,
      });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.recipes });
      onCreated();
    },
  });

  function updateLine(index: number, patch: Partial<IngredientLine>) {
    setLines((prev) =>
      prev.map((l, i) => (i === index ? { ...l, ...patch } : l)),
    );
  }

  const hasIngredient = lines.some((l) => l.name.trim().length > 0);
  const canSubmit = name.trim().length > 0 && hasIngredient && !create.isPending;

  return (
    <Card>
      <CardHeader>
        <CardTitle>{draft ? "Review imported recipe" : "New recipe"}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {draft ? (
          <div className="space-y-2">
            {d?.thumbnailUrl ? (
              <img
                src={d.thumbnailUrl}
                alt=""
                className="h-36 w-full rounded-lg object-cover"
              />
            ) : null}
            {draft.warnings.length > 0 ? (
              <ul className="space-y-1 rounded-lg border border-amber-900/60 bg-amber-950/40 px-4 py-3 text-sm text-amber-200">
                {draft.warnings.map((w, i) => (
                  <li key={i}>• {w}</li>
                ))}
              </ul>
            ) : null}
            {d?.sourceUrl ? (
              <a
                href={d.sourceUrl}
                target="_blank"
                rel="noreferrer"
                className="inline-block text-xs font-medium text-brand-400 hover:underline"
              >
                Source ↗
              </a>
            ) : null}
          </div>
        ) : null}

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-1 sm:col-span-2">
            <Label htmlFor="r-name">Name</Label>
            <Input
              id="r-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Tofu stir-fry"
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="r-servings">Servings</Label>
            <Input
              id="r-servings"
              type="number"
              min={1}
              value={servings}
              onChange={(e) => setServings(e.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="r-minutes">Total minutes</Label>
            <Input
              id="r-minutes"
              type="number"
              min={0}
              value={minutes}
              onChange={(e) => setMinutes(e.target.value)}
            />
          </div>
          <div className="space-y-1 sm:col-span-2">
            <Label htmlFor="r-tags">Tags (comma-separated)</Label>
            <Input
              id="r-tags"
              value={tags}
              onChange={(e) => setTags(e.target.value)}
              placeholder="e.g. vegan, high-protein"
            />
          </div>
        </div>

        <div className="space-y-2">
          <Label>Ingredients</Label>
          <div className="space-y-2">
            {lines.map((line, i) => (
              <div key={i} className="flex items-center gap-2">
                <Input
                  type="number"
                  min={0}
                  step={0.1}
                  value={line.quantity}
                  onChange={(e) => updateLine(i, { quantity: e.target.value })}
                  placeholder="Qty"
                  aria-label={`Ingredient ${i + 1} quantity`}
                  className="w-20"
                />
                <Input
                  value={line.unit}
                  onChange={(e) => updateLine(i, { unit: e.target.value })}
                  placeholder="Unit"
                  aria-label={`Ingredient ${i + 1} unit`}
                  className="w-24"
                />
                <Input
                  value={line.name}
                  onChange={(e) => updateLine(i, { name: e.target.value })}
                  placeholder="Ingredient name (e.g. tofu)"
                  aria-label={`Ingredient ${i + 1} name`}
                  className="flex-1"
                />
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Remove ingredient ${i + 1}`}
                  disabled={lines.length === 1}
                  onClick={() =>
                    setLines((prev) => prev.filter((_, j) => j !== i))
                  }
                  className="text-slate-500 hover:text-red-400"
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            ))}
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setLines((prev) => [...prev, emptyLine()])}
          >
            <Plus className="h-4 w-4" />
            Add ingredient
          </Button>
        </div>

        <div className="space-y-1">
          <Label htmlFor="r-instructions">Instructions (optional)</Label>
          <textarea
            id="r-instructions"
            value={instructions}
            onChange={(e) => setInstructions(e.target.value)}
            rows={3}
            placeholder="Step-by-step…"
            className="flex w-full rounded-lg border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus-visible:border-brand-500 focus-visible:ring-2 focus-visible:ring-brand-500/30 focus-visible:outline-none"
          />
        </div>

        {create.isError ? <ErrorState error={create.error} /> : null}

        <div className="flex gap-2">
          <Button onClick={() => create.mutate()} disabled={!canSubmit}>
            {create.isPending ? <Spinner /> : <Plus className="h-4 w-4" />}
            {draft ? "Save recipe" : "Create recipe"}
          </Button>
          <Button
            variant="outline"
            onClick={onCancel ?? onCreated}
            disabled={create.isPending}
          >
            Cancel
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
