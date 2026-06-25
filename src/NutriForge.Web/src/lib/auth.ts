import {
  PublicClientApplication,
  type AccountInfo,
  type Configuration,
} from "@azure/msal-browser";

/**
 * Auth for the SPA (#56). Two modes, chosen at build time by env:
 *  - **Entra External ID (OIDC)** when VITE_AUTH_AUTHORITY + VITE_AUTH_CLIENT_ID are set: Auth-Code +
 *    PKCE login via MSAL; API calls carry a Bearer access token.
 *  - **dev-auth** otherwise (local/default): the X-Debug-Subject/Role headers the API trusts only in
 *    Development. Nothing here changes that fallback, so the app works with no tenant configured.
 */
const AUTHORITY = import.meta.env.VITE_AUTH_AUTHORITY;
const CLIENT_ID = import.meta.env.VITE_AUTH_CLIENT_ID;
const SCOPE = import.meta.env.VITE_AUTH_SCOPE ?? "";

/** True when a real OIDC provider is configured; otherwise the dev-auth fallback is used. */
export const authEnabled = Boolean(AUTHORITY && CLIENT_ID);

const scopes = SCOPE ? [SCOPE] : [];

export const msalInstance: PublicClientApplication | null = authEnabled
  ? new PublicClientApplication({
      auth: {
        clientId: CLIENT_ID!,
        authority: AUTHORITY!,
        knownAuthorities: [safeHost(AUTHORITY!)],
        redirectUri: window.location.origin,
        postLogoutRedirectUri: window.location.origin,
      },
      cache: { cacheLocation: "localStorage" },
    } satisfies Configuration)
  : null;

function safeHost(url: string): string {
  try {
    return new URL(url).host;
  } catch {
    return "";
  }
}

function activeAccount(): AccountInfo | null {
  if (!msalInstance) return null;
  return msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0] ?? null;
}

/** True when the user can use the app: always in dev-auth mode, or once signed in under OIDC. */
export function isSignedIn(): boolean {
  return !authEnabled || activeAccount() !== null;
}

export function login(): void {
  void msalInstance?.loginRedirect({ scopes });
}

export function logout(): void {
  void msalInstance?.logoutRedirect();
}

// ---- dev-auth role toggle (only meaningful in dev-auth mode) ----

const DEV_ROLE_KEY = "nf-debug-role";

export function getDevRole(): "user" | "admin" {
  try {
    return localStorage.getItem(DEV_ROLE_KEY) === "admin" ? "admin" : "user";
  } catch {
    return "user";
  }
}

export function setDevRole(role: "user" | "admin"): void {
  try {
    localStorage.setItem(DEV_ROLE_KEY, role);
  } catch {
    /* localStorage unavailable — ignore */
  }
}

/** Admin = the OIDC `admin` app role, or the dev-auth admin toggle when no provider is configured. */
export function isAdmin(): boolean {
  if (authEnabled) {
    const roles = (activeAccount()?.idTokenClaims as { roles?: string[] } | undefined)?.roles ?? [];
    return roles.includes("admin");
  }
  return getDevRole() === "admin";
}

/**
 * The auth headers for an API request. OIDC mode attaches a freshly-acquired Bearer token; dev-auth
 * mode sends the debug subject/role. Async because token acquisition may refresh silently.
 */
export async function getAuthHeaders(): Promise<Record<string, string>> {
  if (authEnabled && msalInstance) {
    const account = activeAccount();
    if (account) {
      try {
        const result = await msalInstance.acquireTokenSilent({ scopes, account });
        return { Authorization: `Bearer ${result.accessToken}` };
      } catch {
        // Silent acquisition failed (expired/interaction required) — the login gate will re-prompt.
      }
    }
    return {};
  }

  return { "X-Debug-Subject": "demo-user", "X-Debug-Role": getDevRole() };
}

/** Initialize MSAL + complete any redirect. No-op in dev-auth mode. Call once before rendering. */
export async function initAuth(): Promise<void> {
  if (!msalInstance) return;
  await msalInstance.initialize();
  const result = await msalInstance.handleRedirectPromise();
  if (result?.account) {
    msalInstance.setActiveAccount(result.account);
  } else if (!msalInstance.getActiveAccount()) {
    const [first] = msalInstance.getAllAccounts();
    if (first) msalInstance.setActiveAccount(first);
  }
}
