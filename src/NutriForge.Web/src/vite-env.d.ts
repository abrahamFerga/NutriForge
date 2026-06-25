/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE?: string;
  // Entra External ID (CIAM). Set all three to switch the SPA from dev-auth to real OIDC login.
  readonly VITE_AUTH_AUTHORITY?: string;
  readonly VITE_AUTH_CLIENT_ID?: string;
  readonly VITE_AUTH_SCOPE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
