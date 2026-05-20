import { useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { useAuthStore } from "@/store/useAuthStore";
import {
  useImpersonationStatusChecker,
  useStartImpersonation,
  useStopImpersonation,
} from "@/hooks/use-impersonation";
import { useAppState } from "./public-guard";
import { useGetMe } from "@/idp/iam/hooks/use-user";
import { useImpersonateStore } from "@/store/impersonate-store";
import { useProjectStore } from "@/store/useProjectStore";
import { ImpersonationRequest } from "@/services/impersonation.service";
import { getRuntimeEnv } from "@/lib/runtime-env";

export function ProtectedGuard({ children }: { children: React.ReactNode }) {
  const { isMounted } = useAppState();
  const { data } = useGetMe();
  const { setUser } = useAuthStore();
  const navigate = useNavigate();

  useEffect(() => {
    if (!isMounted) return;
    if (!data) return navigate(`/login`, { replace: true });
    setUser(data.data);
  }, [data, navigate, setUser]);
  if (!isMounted || !data) return null;
  return <>{children}</>;
}

export const ImpersonationChecker = ({
  children,
}: {
  children: React.ReactNode;
}) => {
  const { data, isLoading, isSuccess } = useImpersonationStatusChecker();
  const { setImpersonation, isInitialized, setInitialized } =
    useImpersonateStore();
  const initRef = useRef(false);

  useEffect(() => {
    if (!data || !isSuccess || initRef.current) return;
    initRef.current = true;
    setImpersonation(
      data.impersonated,
      data.originalTenantId,
      data.impersonated ? data.impersonatedTenantId : null,
    );
    setInitialized(true);
  }, [data, isSuccess, setImpersonation, setInitialized]);
  if (isLoading || !isSuccess || !isInitialized) return null;
  return <>{children}</>;
};

export function ImpersonationTerminator({
  children,
}: {
  children: React.ReactNode;
}) {
  const { terminate, isImpersonated } = useImpersonateStore();
  const { mutateAsync } = useStopImpersonation();
  const isTriggering = useRef(false);

  useEffect(() => {
    if (isTriggering.current || !isImpersonated) return;
    isTriggering.current = true;
    mutateAsync(undefined)
      .then(() => {
        terminate(getRuntimeEnv("BLOCKS_X_BLOCKS_KEY"));
        isTriggering.current = false;
      })
      .catch(() => {
        isTriggering.current = false;
      });
  }, [mutateAsync, terminate, isImpersonated, isTriggering]);

  // Always render — never conditionally unmount, as unmounting fires cleanup
  // which calls terminate() and resets state, creating a loop.
  // Return null while actively stopping to avoid flickering children.
  if (isTriggering.current) return null;
  return <>{children}</>;
}

export function ImpersonationSynchronizer({
  children,
}: {
  children: React.ReactNode;
}) {
  const { impersonate, impersonatedTenantId } = useImpersonateStore();
  const { mutateAsync } = useStartImpersonation();

  const { selectedProject } = useProjectStore();
  const isTriggering = useRef(false);

  useEffect(() => {
    if (!selectedProject?.tenantId) return;
    if (selectedProject.tenantId === impersonatedTenantId) return;
    if (isTriggering.current) return;

    isTriggering.current = true;
    const payload: ImpersonationRequest = {
      targetTenantId: selectedProject.tenantId,
    };
    mutateAsync(payload)
      .then(() => {
        impersonate(
          selectedProject.tenantId,
          getRuntimeEnv("BLOCKS_X_BLOCKS_KEY"),
        );
        isTriggering.current = false;
      })
      .catch(() => {
        isTriggering.current = false;
      });
  }, [
    selectedProject?.tenantId,
    mutateAsync,
    impersonate,
    impersonatedTenantId,
    isTriggering,
  ]);
  // Always render children — the mutation effect has its own guards.
  if (isTriggering.current) return null;
  return <>{children}</>;
}

/**
 * Composes the three impersonation components together for backward compatibility.
 * - ImpersonationChecker: syncs state from API on mount
 * - ImpersonationSynchronizer: starts impersonation when project changes
 * - ImpersonationTerminator: stops impersonation when component unmounts
 */
export function ImpersonateGuard({ children }: { children: React.ReactNode }) {
  return (
    <ImpersonationChecker>
      <ImpersonationSynchronizer>
        <ImpersonationTerminator>{children}</ImpersonationTerminator>
      </ImpersonationSynchronizer>
    </ImpersonationChecker>
  );
}
