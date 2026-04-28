import GitHubIcon from "/assets/github-icon.svg";
import GitLabIcon from "/assets/gitlab-icon.svg";
import BitbucketIcon from "/assets/bitbucket-icon.svg";
import AzureIcon from "/assets/azure-icon.svg";
import AwsIcon from "/assets/aws-icon.svg";

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

export const iconMap: Record<string, any> = {
  github: GitHubIcon,
  gitlab: GitLabIcon,
  bitbucket: BitbucketIcon,
  azure: AzureIcon,
  aws: AwsIcon,
};
