import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { githubInfoService } from "@/cross-modules/devops/services/github-info.service";
import { Loader } from "lucide-react";

export default function CallbackPage() {
  const [searchParams] = useSearchParams();
  const code = searchParams.get("code");
  const state = searchParams.get("state");
  const [projectKey] = useState(() => localStorage.getItem("github_auth_project_key") || "");

  const { isLoading, isSuccess } = useQuery({
    queryKey: ["github-verification", code, projectKey],
    queryFn: () => githubInfoService.verifyAuthorization(code || "", projectKey),
    enabled: !!code && !!projectKey,
    retry: false,
  });

  useEffect(() => {
    if (isSuccess) {
      localStorage.setItem("isReload", "true");
      
      // Clean up stored auth data
      localStorage.removeItem("github_auth_state");
      localStorage.removeItem("github_auth_project_key");
      localStorage.removeItem("github_auth_destination");
      
      if (typeof window !== "undefined") {
        window.close();
      }
    }
  }, [isSuccess]);

  if (isLoading) {
    return (
      <div className="fixed inset-0 flex items-center justify-center bg-background/80 backdrop-blur-sm">
        <Loader className="h-8 w-8 animate-spin" />
      </div>
    );
  }

  if (isSuccess) {
    return null;
  }

  return null;
}
