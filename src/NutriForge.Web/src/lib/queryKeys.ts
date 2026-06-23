/** Centralized TanStack Query keys for consistent caching & invalidation. */
export const queryKeys = {
  profile: ["profile"] as const,
  targets: ["targets"] as const,
  diary: (date: string) => ["diary", date] as const,
  diaryAll: ["diary"] as const,
  trend: (days: number) => ["diary", "trend", days] as const,
  foodSearch: (q: string) => ["foods", "search", q] as const,
};
