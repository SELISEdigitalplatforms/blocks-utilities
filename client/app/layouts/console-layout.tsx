import { Outlet } from "react-router-dom";
import {
  ImpersonationChecker,
  ProtectedGuard,
  ImpersonationTerminator,
  // UnCheckedImpersonate,
} from "@/guards/protected-guard";
import { ConsoleHeader } from "@/layouts/console-header/console-header";
export function ConsoleLayout() {
  return (
    <ProtectedGuard>
      <ImpersonationChecker>
        <ImpersonationTerminator>
          <div className="relative min-h-screen bg-[hsl(var(--surface-app))]">
            <ConsoleHeader />
            <main className="pt-[59px]">
              <Outlet />
            </main>
          </div>
        </ImpersonationTerminator>
      </ImpersonationChecker>
    </ProtectedGuard>
  );
}
