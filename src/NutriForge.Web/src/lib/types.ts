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

// ---- Natural-language parse ----

/**
 * A single resolved food candidate returned by the natural-language parser.
 * The five fields { date, mealSlot, foodId, portionId, quantity } map straight
 * onto {@link CreateDiaryEntryRequest}.
 */
export interface ParseCandidate {
  date: string; // yyyy-MM-dd
  mealSlot: string;
  foodId: string;
  foodName: string;
  portionId: string | null;
  portionName: string;
  quantity: number;
  grams: number;
  kcal: number;
  proteinG: number;
  fatG: number;
  carbG: number;
}

export interface DiaryParseResult {
  candidates: ParseCandidate[];
  unresolved: string[];
}

// ---- Assistant ----

/**
 * A diary-log proposal streamed by the assistant that the user must confirm.
 * The five fields { date, mealSlot, foodId, portionId, quantity } map straight
 * onto {@link CreateDiaryEntryRequest}.
 */
export interface LogProposal {
  foodId: string;
  foodName: string;
  portionId: string | null;
  portionName: string;
  quantity: number;
  grams: number;
  mealSlot: MealSlot;
  date: string; // yyyy-MM-dd
  kcal: number;
  proteinG: number;
  fatG: number;
  carbG: number;
}

// ---- Recipes ----

export interface RecipeSummary {
  id: string;
  name: string;
  servings: number;
  totalMinutes: number;
  tags: string[];
  isNutritionComputed: boolean;
  kcalPerServing: number;
  proteinPerServing: number;
  fatPerServing: number;
  carbPerServing: number;
}

export interface RecipeIngredient {
  name: string;
  quantity: number;
  unit: string | null;
  grams: number;
  resolved: boolean;
  kcal: number;
  proteinG: number;
  fatG: number;
  carbG: number;
}

export interface RecipeDto extends RecipeSummary {
  instructions: string | null;
  ingredients: RecipeIngredient[];
}

export interface CreateRecipeIngredient {
  quantity: number;
  unit?: string;
  name: string;
}

export interface CreateRecipeRequest {
  name: string;
  servings: number;
  totalMinutes: number;
  instructions?: string;
  tags?: string[];
  ingredients: CreateRecipeIngredient[];
}

export interface ScaledIngredient {
  name: string;
  quantity: number;
  unit: string | null;
  grams: number;
}

export interface RecipeScaleDto {
  id: string;
  name: string;
  originalServings: number;
  targetServings: number;
  ingredients: ScaledIngredient[];
  kcalPerServing: number;
}

// ---- Pantry ----

export interface PantryItem {
  id: string;
  ingredientName: string;
  grams: number;
}

export interface CreatePantryItemRequest {
  ingredientName: string;
  quantity: number;
  unit?: string;
}

// ---- Shopping lists ----

export interface ShoppingListItem {
  ingredientName: string;
  grams: number;
  pantryCovered: boolean;
}

export interface ShoppingListAisle {
  aisle: string;
  items: ShoppingListItem[];
}

export interface ShoppingListDto {
  id: string;
  mealPlanId: string | null;
  aisles: ShoppingListAisle[];
}

export interface CreateShoppingListRequest {
  recipes: { recipeId: string; servings: number }[];
}

// ---- Diet plans ----

export type DietPlanStatus = "Generating" | "Ready" | "Infeasible" | "Accepted";

export interface DietPlanSlot {
  day: number;
  mealSlot: string;
  recipeId: string;
  recipeName: string;
  servings: number;
  kcal: number;
  proteinG: number;
  fatG: number;
  carbG: number;
}

export interface DietPlanDto {
  id: string;
  status: DietPlanStatus;
  targetKcal: number;
  achievedKcal: number;
  achievedProteinG: number;
  achievedFatG: number;
  achievedCarbG: number;
  message: string | null;
  slots: DietPlanSlot[];
}

export type DietSlug = "vegan" | "vegetarian" | "high-protein";

export interface CreateDietPlanRequest {
  desire?: string;
  kcalTarget?: number;
  dietSlug?: string;
  excludeAllergens?: string[];
  dislikes?: string[];
  maxPrepMinutes?: number;
  mealsPerDay?: number;
  horizonDays?: number;
}

export interface AdherencePoint {
  date: string;
  plannedKcal: number;
  loggedKcal: number;
  adherencePct: number;
}
