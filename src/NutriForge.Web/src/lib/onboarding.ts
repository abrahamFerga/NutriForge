/**
 * Tracks whether the user has dismissed first-run onboarding (#68). Persisted in localStorage so a user
 * who skips isn't re-routed to the wizard on every visit. Once a profile exists the AppShell stops
 * routing here regardless, so this flag only matters for the "skip without a profile" path.
 */
const KEY = "nf:onboarding-dismissed";

export function isOnboardingDismissed(): boolean {
  try {
    return localStorage.getItem(KEY) === "1";
  } catch {
    return false; // private mode / storage disabled — treat as not dismissed
  }
}

export function dismissOnboarding(): void {
  try {
    localStorage.setItem(KEY, "1");
  } catch {
    /* ignore — storage unavailable */
  }
}
