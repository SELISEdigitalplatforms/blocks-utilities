import { Outlet } from "react-router-dom";
import { Suspense } from "react";
import { PublicGuard } from "@/guards/public-guard";

export function PublicLayout() {
  return (
    <Suspense>
      <PublicGuard>
        <Outlet />
      </PublicGuard>
    </Suspense>
  );
}
