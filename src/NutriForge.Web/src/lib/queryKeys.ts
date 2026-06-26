/** Centralized TanStack Query keys for consistent caching & invalidation. */
export const queryKeys = {
  profile: ["profile"] as const,
  targets: ["targets"] as const,
  diary: (date: string) => ["diary", date] as const,
  diaryAll: ["diary"] as const,
  trend: (days: number) => ["diary", "trend", days] as const,
  foodSearch: (q: string) => ["foods", "search", q] as const,
  recipes: ["recipes"] as const,
  recipeSearch: (q: string) => ["recipes", "search", q] as const,
  recipe: (id: string) => ["recipes", id] as const,
  pantry: ["pantry"] as const,
  household: ["household"] as const,
  dietPlan: (id: string) => ["diet-plans", id] as const,
  dietPlanAdherence: (id: string) => ["diet-plans", id, "adherence"] as const,
};
