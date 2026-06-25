import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/components/AppShell";
import { Dashboard } from "@/pages/Dashboard";
import { Diary } from "@/pages/Diary";
import { Onboarding } from "@/pages/Onboarding";
import { Plan } from "@/pages/Plan";
import { Profile } from "@/pages/Profile";
import { Recipes } from "@/pages/Recipes";

export default function App() {
  return (
    <Routes>
      {/* First-run wizard (#68) lives outside the shell so it's full-screen and un-gated. */}
      <Route path="/welcome" element={<Onboarding />} />
      <Route element={<AppShell />}>
        <Route index element={<Dashboard />} />
        <Route path="diary" element={<Diary />} />
        <Route path="recipes" element={<Recipes />} />
        <Route path="plan" element={<Plan />} />
        <Route path="profile" element={<Profile />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
