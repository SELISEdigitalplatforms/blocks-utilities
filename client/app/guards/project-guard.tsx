import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetProjects } from "@blocks-identifier/hooks/use-project";

export function ProjectGuard({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate();
  const { selectedProject, selectedTenantGroup } = useProjectStore();
  const { data: environmentList, isLoading } = useGetProjects(selectedTenantGroup || "");

  // Only redirect after loading is complete and there's no project/data
  useEffect(() => {
    if (!isLoading && !selectedProject && (!environmentList || environmentList.length === 0)) {
      navigate("/console", { replace: true });
    }
  }, [isLoading, selectedProject, environmentList, navigate]);

  // Always render children - let the page handle its own loading and error states
  return <>{children}</>;
}