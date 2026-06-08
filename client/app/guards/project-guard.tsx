import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetProjects } from "@blocks-identifier/hooks/use-project";

export function ProjectGuard({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate();
  const { selectedProject, selectedTenantGroup } = useProjectStore();
  const { data: environmentList, isLoading } = useGetProjects(selectedTenantGroup || "");

  useEffect(() => {
    if (!isLoading && (!selectedProject || !environmentList || environmentList.length === 0)) {
      navigate("/console", { replace: true });
    }
  }, [selectedProject, navigate, environmentList, isLoading]);

  if (isLoading) {
    return null;
  }

  if (!selectedProject) {
    return null;
  }

  return <>{children}</>;
}
