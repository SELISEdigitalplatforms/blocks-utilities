export interface IProviderDestination {
  destination: string;
}

export interface IChangeRepoSpecs {
  projectKey: string;
  repoId: string;
  branch?: string;
  buildCommand?: string;
  outputDirectory?: string;
  installCommand?: string;
  environmentVariables?: Record<string, string>;
}

export interface IChangeSettings {
  projectKey: string;
  repoId: string;
  buildId: string;
  settings: {
    buildCommand?: string;
    outputDirectory?: string;
    installCommand?: string;
    environmentVariables?: Record<string, string>;
  };
}

export interface IManualDeploymentPayload {
  projectKey: string;
  repoId: string;
  branch?: string;
  commitMessage?: string;
}

export interface CardRepoAndBranchesResponse {
  repoId: string;
  repoName: string;
  repoUrl: string;
  branches: string[];
  currentBranch: string;
  lastDeployedAt?: string;
}
