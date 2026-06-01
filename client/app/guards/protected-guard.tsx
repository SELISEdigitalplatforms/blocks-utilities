import { useEffect, useRef, useState } from "react";
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
import LogoLoadingSpinner from "@/components/loader-spinner/loader-spinner";

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

  useEffect(() => {
    if (!data) return;
    setImpersonation(
      data.impersonated,
      data.originalTenantId,
      data.impersonated ? data.impersonatedTenantId : null,
    );
    setInitialized(true);
  }, [data, setImpersonation, setInitialized]);
  if (isLoading || !isSuccess || !isInitialized)
    return <LogoLoadingSpinner size={48} color="text-primary" />;
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

  if (isImpersonated || isTriggering.current) return null;
  return <>{children}</>;
}

export function ImpersonationSynchronizer({
  children,
}: {
  children: React.ReactNode;
}) {
  const { impersonate, isImpersonated, impersonatedTenantId } =
    useImpersonateStore();
  const { mutateAsync } = useStartImpersonation();

  const { selectedProject } = useProjectStore();
  const isTriggering = useRef(false);
  const [isImpersonating, setIsImpersonating] = useState(false);

  useEffect(() => {
    if (!selectedProject?.tenantId) return;
    if (selectedProject.tenantId === impersonatedTenantId) return;
    if (isTriggering.current) return;

    isTriggering.current = true;
    setIsImpersonating(true);
    const payload: ImpersonationRequest = {
      targeted_tenant_id: selectedProject.tenantId,
    };
    mutateAsync(payload)
      .then(() => {
        impersonate(
          selectedProject.tenantId,
          getRuntimeEnv("BLOCKS_X_BLOCKS_KEY"),
        );
        isTriggering.current = false;
        setIsImpersonating(false);
      })
      .catch(() => {
        isTriggering.current = false;
        setIsImpersonating(false);
      });
  }, [
    selectedProject?.tenantId,
    mutateAsync,
    impersonate,
    impersonatedTenantId,
    isTriggering,
  ]);
  if (isImpersonating)
    return <LogoLoadingSpinner size={48} color="text-primary" />;
  if (!isImpersonated || isTriggering.current) return null;
  return <>{children}</>;
}
