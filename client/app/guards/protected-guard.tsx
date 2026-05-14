import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuthStore } from "@/store/useAuthStore";
import { useAppState } from "./public-guard";
import { useGetUser } from "@/idp/iam/hooks/use-user";
import { useProjectStore } from "@/store/useProjectStore";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useImpersonateStore } from "../store/impersonate-store";
import { useStartImpersonation, useStopImpersonation } from "../hooks/use-impersonation";
import { ImpersonationRequest } from "../services/impersonation.service";

export function ProtectedGuard({ children }: { children: React.ReactNode }) {
  // Bypassed auth protection for development
  // const { isMounted } = useAppState();
  // const { data } = useGetUser();
  // const { setUser } = useAuthStore();
  // const navigate = useNavigate();

  // useEffect(() => {
  //   if (!isMounted) return;
  //   if (!data) return navigate(`/login`, { replace: true });
  //   setUser(data.data);
  // }, [data, navigate, setUser]);
  // if (!isMounted || !data) return null;

  return <>{children}</>;
}

export function ImpersonateGuard({ children }: { children: React.ReactNode }) {
  // Bypassed impersonation protection for development
  // const { startImpersonation, stopImpersonation } = useImpersonateStore();
  // const { mutate: startImpersonationMutate } = useStartImpersonation();
  // const { mutate: stopImpersonationMutate } = useStopImpersonation();

  // const { selectedProject } = useProjectStore();

  // const [ready, setReady] = useState(false);
  // const impersonateRef = useRef({
  //   hasStarted: false,
  //   isCompleted: false,
  // });

  // useEffect(() => {
  //   if (!selectedProject?.tenantId) return;
  //   if (impersonateRef.current.hasStarted) return;

  //   impersonateRef.current.hasStarted = true;

  //   const payload: ImpersonationRequest = {
  //     targetTenantId: selectedProject.tenantId,
  //   };

  //   startImpersonationMutate(payload, {
  //     onSuccess: () => {
  //       startImpersonation(
  //         payload.targetTenantId,
  //         getRuntimeEnv("BLOCKS_X_BLOCKS_KEY"),
  //       );

  //       impersonateRef.current.isCompleted = true;
  //       setReady(true);
  //     },
  //     onError: () => {
  //       impersonateRef.current.hasStarted = false;
  //     },
  //   });

  //   return () => {
  //     if (!impersonateRef.current.isCompleted) return;

  //     stopImpersonationMutate(undefined, {
  //       onSuccess: () => {
  //         stopImpersonation();
  //         impersonateRef.current.hasStarted = false;
  //         impersonateRef.current.isCompleted = false;
  //         setReady(false);
  //       },
  //     });
  //   };
  // }, [
  //   selectedProject?.tenantId,
  //   startImpersonationMutate,
  //   stopImpersonationMutate,
  //   startImpersonation,
  //   stopImpersonation,
  // ]);

  // if (!ready) return null;

  return <>{children}</>;
}