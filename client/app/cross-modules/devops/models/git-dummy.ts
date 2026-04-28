export interface IProvider {
  id: string;
  name: string;
  icon: string;
  active: boolean;
}

export const providers: IProvider[] = [
  {
    id: "github",
    name: "GitHub",
    icon: "github",
    active: true,
  },
  {
    id: "gitlab",
    name: "GitLab",
    icon: "gitlab",
    active: false,
  },
  {
    id: "bitbucket",
    name: "Bitbucket",
    icon: "bitbucket",
    active: false,
  },
  {
    id: "azure",
    name: "Azure DevOps",
    icon: "azure",
    active: false,
  },
  {
    id: "aws",
    name: "AWS CodeCommit",
    icon: "aws",
    active: false,
  },
];
