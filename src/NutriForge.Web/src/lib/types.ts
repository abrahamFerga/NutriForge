// Domain enums — string values MUST match the backend exactly.

export type Sex = "Male" | "Female";

export type ActivityLevel =
  | "Sedentary"
  | "LightlyActive"
  | "ModeratelyActive"
  | "Active"
  | "VeryActive";

export type Goal =
  | "AggressiveCut"
  | "ModerateCut"
  | "Maintain"
  | "LeanBulk"
  | "Bulk";

export type MacroStrategy = "ProteinAnchored" | "Percentage";

export type MealSlot = "Breakfast" | "Lunch" | "Dinner" | "Snack";

export const MEAL_SLOTS: readonly MealSlot[] = [
  "Breakfast",
  "Lunch",
  "Dinner",
  "Snack",
] as const;

export const SEXES: readonly Sex[] = ["Male", "Female"] as const;

export const ACTIVITY_LEVELS: readonly ActivityLevel[] = [
  "Sedentary",
  "LightlyActive",
  "ModeratelyActive",
  "Active",
  "VeryActive",
] as const;

export const GOALS: readonly Goal[] = [
  "AggressiveCut",
  "ModerateCut",
  "Maintain",
  "LeanBulk",
  "Bulk",
] as const;

export const MACRO_STRATEGIES: readonly MacroStrategy[] = [
  "ProteinAnchored",
  "Percentage",
] as const;

// ---- Food ----

export interface Portion {
  id: string;
  name: string;
  grams: number;
}

export interface FoodSummary {
  id: string;
  name: string;
  brand: string | null;
  verificationStatus: string;
  kcalPer100g: number;
  proteinPer100g: number;
  fatPer100g: number;
  carbPer100g: number;
  portions: Portion[];
}

export type FoodDetail = FoodSummary;

export interface CreateFoodRequest {
  name: string;
  brand?: string;
  gtin?: string;
  kcalPer100g: number;
  proteinPer100g: number;
  fatPer100g: number;
  carbPer100g: number;
  portions?: { name: string; grams: number }[];
}

// ---- Profile ----

export interface ProfileDto {
  sex: Sex;
  birthDate: string; // yyyy-MM-dd
  heightCm: number;
  weightKg: number;
  bodyFatPct: number | null;
  activity: ActivityLevel;
  goal: Goal;
  macroStrategy: MacroStrategy;
  allergens: string[];
  dislikes: string[];
  preferredDiets: string[];
  version: number;
}

export interface UpdateProfileRequest {
  sex: Sex;
  birthDate: string;
  heightCm: number;
  weightKg: number;
  bodyFatPct?: number | null;
  activity: ActivityLevel;
  goal: Goal;
  macroStrategy: MacroStrategy;
  allergens?: string[];
  dislikes?: string[];
  preferredDiets?: string[];
}

// ---- Targets ----

export interface TargetsDto {
  kcal: number;
  proteinG: number;
  fatG: number;
  carbG: number;
  formula: string;
  computedAt: string;
}

// ---- Diary ----

export interface MacroTotals {
  kcal: number;
  proteinG: number;
  fatG: number;
  carbG: number;
}

export interface DiaryEntry {
  id: string;
  date: string;
  mealSlot: MealSlot;
  sequence: number;
  foodId: string;
  foodName: string;
  portionName: string;
  quantity: number;
  grams: number;
  kcal: number;
  proteinG: number;
  fatG: number;
  carbG: number;
}

export interface DiaryDay {
  date: string;
  entries: DiaryEntry[];
  consumed: MacroTotals;
  target: TargetsDto | null;
  remaining: MacroTotals;
}

export interface CreateDiaryEntryRequest {
  date: string;
  mealSlot: MealSlot;
  foodId: string;
  portionId?: string | null;
  quantity: number;
}

export interface TrendPoint {
  date: string;
  kcal: number;
  targetKcal: number;
}
