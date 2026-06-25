import { StrictMode, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MsalProvider, useIsAuthenticated } from "@azure/msal-react";
import App from "./App.tsx";
import { authEnabled, initAuth, login, msalInstance } from "./lib/auth";
import "./index.css";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});

/** Under OIDC, block the app until the user signs in. In dev-auth mode this component isn't rendered. */
function AuthGate({ children }: { children: ReactNode }) {
  const isAuthed = useIsAuthenticated();
  if (isAuthed) return <>{children}</>;
  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-950 p-6">
      <div className="w-full max-w-sm rounded-xl border border-slate-800 bg-slate-900/60 p-8 text-center shadow-lg">
        <h1 className="text-xl font-bold text-slate-100">NutriForge</h1>
        <p className="mt-1 text-sm text-slate-400">Sign in to continue.</p>
        <button
          onClick={() => login()}
          className="mt-6 w-full rounded-lg bg-brand-500 px-4 py-2 font-medium text-white hover:bg-brand-400"
        >
          Sign in
        </button>
      </div>
    </div>
  );
}

const tree = (
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {authEnabled && msalInstance ? (
          <MsalProvider instance={msalInstance}>
            <AuthGate>
              <App />
            </AuthGate>
          </MsalProvider>
        ) : (
          <App />
        )}
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>
);

// Complete any OIDC redirect before the first render (no-op in dev-auth mode).
void initAuth().finally(() => {
  createRoot(document.getElementById("root")!).render(tree);
});
