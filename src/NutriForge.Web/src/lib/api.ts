import type {
  AdherencePoint,
  CreateDiaryEntryRequest,
  CreateDietPlanRequest,
  CreateFoodRequest,
  CreatePantryItemRequest,
  CreateRecipeRequest,
  CreateShoppingListRequest,
  DiaryDay,
  DiaryEntry,
  DiaryParseResult,
  DietPlanDto,
  FoodDetail,
  FoodSummary,
  ImportPreviewDto,
  ImportRecipeRequest,
  LogProposal,
  MealSlot,
  PantryItem,
  PhotoAnalysisResult,
  ProfileDto,
  RecipeDto,
  RecipeScaleDto,
  RecipeSummary,
  ShoppingListDto,
  TargetsDto,
  TrendPoint,
  UpdateProfileRequest,
} from "./types";

/**
 * Base URL for the backend API.
 * - Read from `VITE_API_BASE`.
 * - Defaults to `http://localhost:5000` when unset.
 * - An explicit empty string is honored (relative-path mode behind a proxy).
 */
const RAW_BASE = import.meta.env.VITE_API_BASE;
export const API_BASE: string =
  RAW_BASE === undefined ? "http://localhost:5000" : RAW_BASE;

// RFC9457 problem+json shape (partial).
interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

/** Error carrying a human-readable message plus the raw problem details. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ProblemDetails;

  constructor(message: string, status: number, problem?: ProblemDetails) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }
}

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  signal?: AbortSignal;
}

const WRITE_METHODS = new Set(["POST", "PUT", "DELETE"]);

function buildUrl(path: string): string {
  if (!API_BASE) return path; // relative mode
  return `${API_BASE.replace(/\/$/, "")}${path}`;
}

function buildHeaders(method: string, hasBody: boolean): Headers {
  const headers = new Headers();
  // Dev-auth scheme — sent on every request for local development.
  headers.set("X-Debug-Subject", "demo-user");
  headers.set("X-Debug-Role", "user");
  headers.set("Accept", "application/json");

  if (hasBody) {
    headers.set("Content-Type", "application/json");
  }
  if (WRITE_METHODS.has(method)) {
    headers.set("Idempotency-Key", crypto.randomUUID());
  }
  return headers;
}

function formatProblem(problem: ProblemDetails, fallback: string): string {
  const parts: string[] = [];
  if (problem.title) parts.push(problem.title);
  if (problem.errors) {
    for (const [field, messages] of Object.entries(problem.errors)) {
      for (const m of messages) {
        parts.push(field ? `${field}: ${m}` : m);
      }
    }
  } else if (problem.detail) {
    parts.push(problem.detail);
  }
  return parts.length > 0 ? parts.join(" — ") : fallback;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? "GET";
  const hasBody = options.body !== undefined;

  const response = await fetch(buildUrl(path), {
    method,
    headers: buildHeaders(method, hasBody),
    body: hasBody ? JSON.stringify(options.body) : undefined,
    signal: options.signal,
  });

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  let parsed: unknown = undefined;
  if (text) {
    try {
      parsed = JSON.parse(text);
    } catch {
      parsed = undefined;
    }
  }

  if (!response.ok) {
    const problem = (parsed ?? {}) as ProblemDetails;
    const fallback = `Request failed (${response.status} ${response.statusText})`;
    throw new ApiError(formatProblem(problem, fallback), response.status, problem);
  }

  return parsed as T;
}

// ---- Foods ----

export const foodsApi = {
  search(q: string, signal?: AbortSignal): Promise<FoodSummary[]> {
    return request<FoodSummary[]>(
      `/api/v1/foods/search?q=${encodeURIComponent(q)}`,
      { signal },
    );
  },
  create(body: CreateFoodRequest): Promise<FoodDetail> {
    return request<FoodDetail>("/api/v1/foods", { method: "POST", body });
  },
  /** Looks up a food by GTIN/barcode. Returns null when not found (404). */
  async barcode(gtin: string): Promise<FoodDetail | null> {
    try {
      return await request<FoodDetail>(
        `/api/v1/foods/barcode/${encodeURIComponent(gtin)}`,
      );
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) return null;
      throw err;
    }
  },
};

// ---- Profile ----

export const profileApi = {
  /** Returns null when no profile exists yet (404). */
  async get(): Promise<ProfileDto | null> {
    try {
      return await request<ProfileDto>("/api/v1/profile");
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) return null;
      throw err;
    }
  },
  update(body: UpdateProfileRequest): Promise<ProfileDto> {
    return request<ProfileDto>("/api/v1/profile", { method: "PUT", body });
  },
};

// ---- Targets ----

export const targetsApi = {
  /** Returns null when no profile/targets exist yet (404). */
  async get(): Promise<TargetsDto | null> {
    try {
      return await request<TargetsDto>("/api/v1/targets");
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) return null;
      throw err;
    }
  },
};

// ---- Diary ----

export const diaryApi = {
  get(date: string): Promise<DiaryDay> {
    return request<DiaryDay>(`/api/v1/diary?date=${encodeURIComponent(date)}`);
  },
  create(body: CreateDiaryEntryRequest): Promise<DiaryEntry> {
    return request<DiaryEntry>("/api/v1/diary", { method: "POST", body });
  },
  remove(id: string): Promise<void> {
    return request<void>(`/api/v1/diary/${encodeURIComponent(id)}`, {
      method: "DELETE",
    });
  },
  trend(days = 7): Promise<TrendPoint[]> {
    return request<TrendPoint[]>(`/api/v1/diary/trend?days=${days}`);
  },
  /**
   * Parses a natural-language meal description into confirmable candidates.
   * Throws {@link ApiError} with status 503 when no LLM provider is configured,
   * and 400 on empty/too-long text.
   */
  parse(
    text: string,
    mealSlot?: MealSlot,
    date?: string,
  ): Promise<DiaryParseResult> {
    return request<DiaryParseResult>("/api/v1/diary/parse", {
      method: "POST",
      body: { text, mealSlot, date },
    });
  },
  /**
   * Sends a meal photo (multipart) for vision recognition → confirmable candidates.
   * Throws {@link ApiError} 503 when no vision provider is configured.
   */
  async parsePhoto(
    file: File,
    mealSlot: MealSlot,
    date: string,
  ): Promise<PhotoAnalysisResult> {
    const headers = new Headers();
    headers.set("X-Debug-Subject", "demo-user");
    headers.set("X-Debug-Role", "user");
    headers.set("Accept", "application/json");
    headers.set("Idempotency-Key", crypto.randomUUID());
    // No Content-Type: the browser sets the multipart boundary for FormData.

    const form = new FormData();
    form.append("image", file);
    form.append("mealSlot", mealSlot);
    form.append("date", date);

    const response = await fetch(buildUrl("/api/v1/diary/parse-photo"), {
      method: "POST",
      headers,
      body: form,
    });

    if (!response.ok) {
      const text = await response.text().catch(() => "");
      let problem: ProblemDetails = {};
      if (text) {
        try {
          problem = JSON.parse(text) as ProblemDetails;
        } catch {
          /* non-JSON body */
        }
      }
      throw new ApiError(
        formatProblem(problem, `Request failed (${response.status} ${response.statusText})`),
        response.status,
        problem,
      );
    }

    return response.json() as Promise<PhotoAnalysisResult>;
  },
};

// ---- Recipes ----

export const recipesApi = {
  search(q: string, signal?: AbortSignal): Promise<RecipeSummary[]> {
    const qs = q ? `?q=${encodeURIComponent(q)}` : "";
    return request<RecipeSummary[]>(`/api/v1/recipes${qs}`, { signal });
  },
  get(id: string): Promise<RecipeDto> {
    return request<RecipeDto>(`/api/v1/recipes/${encodeURIComponent(id)}`);
  },
  create(body: CreateRecipeRequest): Promise<RecipeDto> {
    return request<RecipeDto>("/api/v1/recipes", { method: "POST", body });
  },
  scale(id: string, servings: number): Promise<RecipeScaleDto> {
    return request<RecipeScaleDto>(
      `/api/v1/recipes/${encodeURIComponent(id)}/scale?servings=${servings}`,
    );
  },
  /**
   * Imports a recipe from a YouTube/recipe URL or pasted text → an editable draft.
   * Throws {@link ApiError} 503 (no AI provider) or 422 (nothing extractable).
   */
  importPreview(body: ImportRecipeRequest): Promise<ImportPreviewDto> {
    return request<ImportPreviewDto>("/api/v1/recipes/import/preview", {
      method: "POST",
      body,
    });
  },
};

// ---- Pantry ----

export const pantryApi = {
  list(): Promise<PantryItem[]> {
    return request<PantryItem[]>("/api/v1/pantry");
  },
  add(body: CreatePantryItemRequest): Promise<PantryItem> {
    return request<PantryItem>("/api/v1/pantry", { method: "POST", body });
  },
  remove(id: string): Promise<void> {
    return request<void>(`/api/v1/pantry/${encodeURIComponent(id)}`, {
      method: "DELETE",
    });
  },
};

// ---- Shopping lists ----

export const shoppingApi = {
  create(body: CreateShoppingListRequest): Promise<ShoppingListDto> {
    return request<ShoppingListDto>("/api/v1/shopping-lists", {
      method: "POST",
      body,
    });
  },
  get(id: string): Promise<ShoppingListDto> {
    return request<ShoppingListDto>(
      `/api/v1/shopping-lists/${encodeURIComponent(id)}`,
    );
  },
};

// ---- Diet plans ----

export const dietPlansApi = {
  /** POST returns 202 with the (likely still Generating) plan. */
  create(body: CreateDietPlanRequest): Promise<DietPlanDto> {
    return request<DietPlanDto>("/api/v1/diet-plans", { method: "POST", body });
  },
  get(id: string, signal?: AbortSignal): Promise<DietPlanDto> {
    return request<DietPlanDto>(
      `/api/v1/diet-plans/${encodeURIComponent(id)}`,
      { signal },
    );
  },
  accept(id: string): Promise<DietPlanDto> {
    return request<DietPlanDto>(
      `/api/v1/diet-plans/${encodeURIComponent(id)}/accept`,
      { method: "POST", body: {} },
    );
  },
  shoppingList(id: string): Promise<ShoppingListDto> {
    return request<ShoppingListDto>(
      `/api/v1/diet-plans/${encodeURIComponent(id)}/shopping-list`,
      { method: "POST", body: {} },
    );
  },
  adherence(id: string): Promise<AdherencePoint[]> {
    return request<AdherencePoint[]>(
      `/api/v1/diet-plans/${encodeURIComponent(id)}/adherence`,
    );
  },
  /** Fetches the printable meal-prep PDF as a Blob (dev-auth headers can't ride a plain link). */
  async pdf(id: string): Promise<Blob> {
    const headers = new Headers();
    headers.set("X-Debug-Subject", "demo-user");
    headers.set("X-Debug-Role", "user");
    headers.set("Accept", "application/pdf");

    const response = await fetch(
      buildUrl(`/api/v1/diet-plans/${encodeURIComponent(id)}/pdf`),
      { headers },
    );
    if (!response.ok) {
      throw new ApiError(
        `Failed to generate PDF (${response.status} ${response.statusText})`,
        response.status,
      );
    }
    return response.blob();
  },
};

// ---- Assistant (MAF NutritionAssistant) ----

export interface AssistantStatus {
  configured: boolean;
}
export interface AssistantReply {
  reply: string;
}

export const assistantApi = {
  status(): Promise<AssistantStatus> {
    return request<AssistantStatus>("/api/v1/assistant/status");
  },
  chat(message: string): Promise<AssistantReply> {
    return request<AssistantReply>("/api/v1/assistant/chat", {
      method: "POST",
      body: { message },
    });
  },
  /**
   * Streams an assistant reply via Server-Sent Events.
   * - `onText` is called with each text chunk as it arrives.
   * - `onProposal` is called at most once if the assistant proposes a diary log.
   * Resolves when the stream terminates (the `done` event or end of body).
   * Throws {@link ApiError} with status 503 when the assistant isn't configured.
   */
  async chatStream(
    message: string,
    onText: (chunk: string) => void,
    onProposal: (proposal: LogProposal) => void,
    signal?: AbortSignal,
  ): Promise<void> {
    const headers = new Headers();
    headers.set("X-Debug-Subject", "demo-user");
    headers.set("X-Debug-Role", "user");
    headers.set("Content-Type", "application/json");
    headers.set("Accept", "text/event-stream");
    headers.set("Idempotency-Key", crypto.randomUUID());

    const response = await fetch(buildUrl("/api/v1/assistant/chat/stream"), {
      method: "POST",
      headers,
      body: JSON.stringify({ message }),
      signal,
    });

    if (!response.ok || !response.body) {
      const text = await response.text().catch(() => "");
      let problem: ProblemDetails = {};
      if (text) {
        try {
          problem = JSON.parse(text) as ProblemDetails;
        } catch {
          /* non-JSON body */
        }
      }
      const fallback = `Request failed (${response.status} ${response.statusText})`;
      throw new ApiError(formatProblem(problem, fallback), response.status, problem);
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    try {
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        let sep: number;
        // SSE frames are separated by a blank line ("\n\n").
        while ((sep = buffer.indexOf("\n\n")) !== -1) {
          const frame = buffer.slice(0, sep);
          buffer = buffer.slice(sep + 2);
          const stop = handleSseFrame(frame, onText, onProposal);
          if (stop) return;
        }
      }
      // Flush any trailing frame without a closing blank line.
      if (buffer.trim().length > 0) {
        handleSseFrame(buffer, onText, onProposal);
      }
    } finally {
      reader.releaseLock();
    }
  },
  clear(): Promise<void> {
    return request<void>("/api/v1/assistant/session", { method: "DELETE" });
  },
};

/**
 * Parses one SSE frame and dispatches it. Returns `true` when the terminating
 * `done` event is seen, signalling the caller to stop reading.
 */
function handleSseFrame(
  frame: string,
  onText: (chunk: string) => void,
  onProposal: (proposal: LogProposal) => void,
): boolean {
  let event = "message";
  const dataLines: string[] = [];

  for (const rawLine of frame.split("\n")) {
    const line = rawLine.replace(/\r$/, "");
    if (line.startsWith(":") || line === "") continue; // comment / blank
    if (line.startsWith("event:")) {
      event = line.slice("event:".length).trim();
    } else if (line.startsWith("data:")) {
      dataLines.push(line.slice("data:".length).replace(/^ /, ""));
    }
  }

  const data = dataLines.join("\n");

  if (event === "done") return true;
  if (event === "proposal") {
    if (data) {
      try {
        onProposal(JSON.parse(data) as LogProposal);
      } catch {
        /* ignore malformed proposal */
      }
    }
    return false;
  }
  // default (message) event — a text chunk.
  if (data) {
    try {
      const payload = JSON.parse(data) as { text?: string };
      if (typeof payload.text === "string") onText(payload.text);
    } catch {
      /* ignore malformed chunk */
    }
  }
  return false;
}
