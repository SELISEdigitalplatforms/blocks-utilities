
export interface IRepository {
  id: number;
  name: string;
  full_name: string;
  html_url: string;
  description?: string;
  private?: boolean;
  fork?: boolean;
  created_at?: string;
  updated_at?: string;
  pushed_at?: string;
  size?: number;
  stargazers_count?: number;
  watchers_count?: number;
  language?: string;
  forks_count?: number;
  open_issues_count?: number;
  default_branch?: string;
}

export interface IBranch {
  name: string;
  commit: {
    sha: string;
    url: string;
  };
  protected: boolean;
}

export interface IRepositoryUser {
  id: string;
  name: string;
  email: string;
  avatar_url: string;
  html_url: string;
  login?: string;
}

export interface ICloneRepo {
  projectKey: string;
  repoName: string;
  branch: string;
  repoUrl: string;
  buildCommand?: string;
  outputDirectory?: string;
  installCommand?: string;
  environmentVariables?: Record<string, string>;
}

export interface IBranchMatchResponse {
  isSuccess: boolean;
  message: string;
  branchExists: boolean;
  matchedBranch?: string;
}

/**
 * Where each provider's icon is served from.
 *
 * Plain paths rather than imports. These files live in `client/public/assets`, which Vite serves
 * verbatim and deliberately does not process — so an import of one is asking the bundler to
 * resolve a URL it treats as opaque. In the browser it happened to work; under Vitest the same
 * specifier reaches Node as `file:///assets/github-icon.svg` and throws before a single test in
 * the file runs, which is why four suites could not be collected at all.
 *
 * The value is identical either way: the served path. One call site already wrote it as a literal
 * for exactly that reason.
 */
export const iconMap: Record<string, string> = {
  github: "/assets/github-icon.svg",
  gitlab: "/assets/gitlab-icon.svg",
  bitbucket: "/assets/bitbucket-icon.svg",
  azure: "/assets/azure-icon.svg",
  aws: "/assets/aws-icon.svg",
};
