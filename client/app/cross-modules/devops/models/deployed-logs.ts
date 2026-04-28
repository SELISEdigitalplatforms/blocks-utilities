export interface IBuildApiResponse {
  buildId: string;
  projectKey: string;
  repoId: string;
  repoName: string;
  branch: string;
  status: "pending" | "building" | "success" | "failed";
  startedAt: string;
  completedAt?: string;
  duration?: number;
  logs?: string[];
  buildCommand?: string;
  outputDirectory?: string;
  installCommand?: string;
  errorMessage?: string;
}

export interface IDeploymentLog {
  id: string;
  timestamp: string;
  level: "info" | "warning" | "error";
  message: string;
}
