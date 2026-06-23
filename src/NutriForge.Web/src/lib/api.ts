import type {
  CreateDiaryEntryRequest,
  CreateFoodRequest,
  DiaryDay,
  DiaryEntry,
  FoodDetail,
  FoodSummary,
  ProfileDto,
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
  clear(): Promise<void> {
    return request<void>("/api/v1/assistant/session", { method: "DELETE" });
  },
};
