/** Serialized as an int by the API, not a string. */
export enum RepositorySelection {
  Selected = 0,
  All = 1,
}

export interface GitHubInstallationDto {
  id: number;
  installationId: number;
  accountLogin: string;
  accountType: string;
  repositorySelection: RepositorySelection;
  isSuspended: boolean;
}

export interface GitHubRepositoryDto {
  id: number;
  repositoryId: number;
  fullName: string;
  defaultBranch: string;
  isPrivate: boolean;
}

/** A null `installation` is a successful "not connected" answer, not an error. */
export interface GitHubConnectionDto {
  installation: GitHubInstallationDto | null;
  repositories: GitHubRepositoryDto[];
}

export interface ProjectRepositoryLinkDto {
  id: number;
  projectId: number;
  gitHubRepositoryId: number;
  fullName: string;
  linkedByUserId: string;
}

/**
 * A link whose repository has been dropped from the installation is filtered out of `links`
 * server-side and survives only as `unavailableCount` — the link row itself is kept.
 */
export interface ProjectRepositoriesDto {
  links: ProjectRepositoryLinkDto[];
  unavailableCount: number;
}

export interface GitHubInstallUrlDto {
  url: string;
}

export interface ConnectGitHubDto {
  installationId: number;
  state: string;
  code: string;
}
