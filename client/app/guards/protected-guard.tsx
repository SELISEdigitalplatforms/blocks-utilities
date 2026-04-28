import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useAuthStore } from "@/store/useAuthStore";
import { useAppState } from "./public-guard";

export function ProtectedGuard({ children }: { children: React.ReactNode }) {
  const { isAuthenticated } = useAuthStore();
  const { isMounted } = useAppState();
  const navigate = useNavigate();
  
  useEffect(() => {
    if (!isMounted) return;
    if (!isAuthenticated) return navigate("/login", { replace: true });
  }, [isAuthenticated, isMounted, navigate]);

  if (!isMounted || !isAuthenticated) return null;

  return <>{children}</>;
}
