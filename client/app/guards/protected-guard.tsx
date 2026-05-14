import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuthStore } from "@/store/useAuthStore";
import { useAppState } from "./public-guard";
import { useGetUser } from "@/idp/iam/hooks/use-user";
import { useProjectStore } from "@/store/useProjectStore";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useImpersonateStore } from "@/store/impersonate-store";
import { useStartImpersonation, useStopImpersonation } from "@/hooks/use-impersonation";
import { ImpersonationRequest } from "@/services/impersonation.service";
import { debug } from "@/lib/debug";

export function ProtectedGuard({ children }: { children: React.ReactNode }) {
  const { isMounted } = useAppState();
  const { data, isLoading, isFetching, isError, error } = useGetUser();
  const { setUser } = useAuthStore();
  const navigate = useNavigate();

  debug.group("ProtectedGuard");
  debug.log("isMounted:", isMounted);
  debug.log("useGetUser - isLoading:", isLoading, "isFetching:", isFetching, "isError:", isError);
  debug.log("data:", data ? { hasData: true, itemId: data.data?.itemId, email: data.data?.email } : null);
  debug.log("error:", error);
  debug.dumpAuthState();
  debug.log("Current URL:", window.location.href);
  debug.groupEnd();

  useEffect(() => {
    debug.group("ProtectedGuard.useEffect");
    debug.log("isMounted:", isMounted, "data:", !!data);
    if (!isMounted) {
      debug.log("Not mounted yet, returning");
      debug.groupEnd();
      return;
    }
    if (!data) {
      debug.warn("No user data, redirecting to /login");
      debug.groupEnd();
      return navigate(`/login`, { replace: true });
    }
    debug.log("Setting user:", data.data?.itemId, data.data?.email);
    setUser(data.data);
    debug.groupEnd();
  }, [data, navigate, setUser, isMounted]);
  if (!isMounted || !data) return null;
  return <>{children}</>;
}

export function ImpersonateGuard({ children }: { children: React.ReactNode }) {
  const { startImpersonation, stopImpersonation } = useImpersonateStore();
  const { mutate: startImpersonationMutate, isPending: isStartPending, isError: isStartError, error: startError } = useStartImpersonation();
  const { mutate: stopImpersonationMutate, isPending: isStopPending } = useStopImpersonation();

  const { selectedProject } = useProjectStore();

  const [ready, setReady] = useState(false);
  const impersonateRef = useRef({
    hasStarted: false,
    isCompleted: false,
  });

  debug.group("ImpersonateGuard");
  debug.log("selectedProject?.tenantId:", selectedProject?.tenantId);
  debug.log("isStartPending:", isStartPending, "isStartError:", isStartError);
  debug.log("startError:", startError);
  debug.log("ready:", ready, "hasStarted:", impersonateRef.current.hasStarted, "isCompleted:", impersonateRef.current.isCompleted);
  debug.dumpAuthState();
  debug.groupEnd();

  useEffect(() => {
    debug.group("ImpersonateGuard.useEffect");
    debug.log("selectedProject?.tenantId:", selectedProject?.tenantId);
    debug.log("hasStarted:", impersonateRef.current.hasStarted);

    if (!selectedProject?.tenantId) {
      debug.log("No tenantId, skipping impersonation");
      debug.groupEnd();
      return;
    }
    if (impersonateRef.current.hasStarted) {
      debug.log("Already started, skipping");
      debug.groupEnd();
      return;
    }

    impersonateRef.current.hasStarted = true;

    const payload: ImpersonationRequest = {
      targetTenantId: selectedProject.tenantId,
    };

    debug.log("Starting impersonation with payload:", payload);

    startImpersonationMutate(payload, {
      onSuccess: () => {
        debug.log("Impersonation start SUCCESS");
        startImpersonation(
          payload.targetTenantId,
          getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
        );

        impersonateRef.current.isCompleted = true;
        setReady(true);
        debug.dumpAuthState();
      },
      onError: (err) => {
        debug.error("Impersonation start FAILED:", err);
        impersonateRef.current.hasStarted = false;
      },
    });

    debug.groupEnd();

    return () => {
      debug.group("ImpersonateGuard.cleanup");
      debug.log("isCompleted:", impersonateRef.current.isCompleted);
      if (!impersonateRef.current.isCompleted) {
        debug.log("Not completed, skipping stop");
        debug.groupEnd();
        return;
      }

      stopImpersonationMutate(undefined, {
        onSuccess: () => {
          debug.log("Impersonation stop SUCCESS");
          stopImpersonation();
          impersonateRef.current.hasStarted = false;
          impersonateRef.current.isCompleted = false;
          setReady(false);
        },
        onError: (err) => {
          debug.error("Impersonation stop FAILED:", err);
        },
      });
      debug.groupEnd();
    };
  }, [
    selectedProject?.tenantId,
    startImpersonationMutate,
    stopImpersonationMutate,
    startImpersonation,
    stopImpersonation,
  ]);

  if (!ready) return null;

  return <>{children}</>;
}