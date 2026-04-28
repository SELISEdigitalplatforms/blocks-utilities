import { Outlet } from "react-router-dom";
import { ProtectedGuard } from "@/guards/protected-guard";
import { ConsoleHeader } from "@/layouts/console-header/console-header";

export function ConsoleLayout() {
  return (
    <ProtectedGuard>
      <div className="relative min-h-screen bg-[hsl(var(--surface-app))]">
        <ConsoleHeader />
        <main className="pt-[59px]">
          <Outlet />
        </main>
      </div>
    </ProtectedGuard>
  );
}
