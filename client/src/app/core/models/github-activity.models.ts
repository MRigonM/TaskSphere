/** Serialized as an int by the API, not a string — the same as `RepositorySelection`. */
export enum PullRequestState {
  Open = 0,
  Closed = 1,
  Merged = 2,
}

export interface TaskCommitDto {
  sha: string;
  shortSha: string;
  message: string;
  authorName: string;
  /** Null when GitHub could not match the commit to an account. The git author still stands. */
  authorLogin: string | null;
  committedAtUtc: string;
  htmlUrl: string;
  repositoryFullName: string;
  /**
   * Null when the commit's own message named the task. Set to the branch that conferred it
   * when the link was inherited — the commit never mentions the task at all.
   */
  viaBranchName: string | null;
}

/**
 * `isDeleted` is rendered, not filtered: a branch GitHub no longer reports keeps its place in
 * the task's history with a marker rather than disappearing.
 */
export interface TaskBranchDto {
  name: string;
  headSha: string;
  isDeleted: boolean;
  repositoryFullName: string;
}

export interface TaskPullRequestDto {
  number: number;
  title: string;
  state: PullRequestState;
  authorLogin: string;
  openedAtUtc: string;
  mergedAtUtc: string | null;
  htmlUrl: string;
  repositoryFullName: string;
}

/**
 * `lastSyncedAtUtc` is null until an activity sync has run. Ingestion is pull-based, so the
 * panel says when it last looked rather than implying it is live.
 */
export interface TaskGitHubActivityDto {
  commits: TaskCommitDto[];
  branches: TaskBranchDto[];
  pullRequests: TaskPullRequestDto[];
  lastSyncedAtUtc: string | null;
}

/** `branch` is set when a single branch's commits listing failed; null when the failure is
 *  repository-scoped (the branches listing, or the one pull-requests listing). */
export interface SyncFailureDto {
  repositoryFullName: string;
  reason: string;
  branch: string | null;
}

/**
 * The outcome of one pass — commits or pull requests — across the task's repositories.
 *
 * `attempted: false` means every repository was inside this pass's cooldown: a healthy skip, not
 * a failure. `attempted: true` with `succeeded: false` means at least one repository failed, and
 * `TaskActivityRefreshDto.failures` names which. Fetching nothing new is still success.
 *
 * `refreshedAtUtc` is null when any repository has never had this pass succeed — "unknown",
 * never "old". A failed pass leaves it as it was rather than claiming freshness.
 */
export interface RefreshPassDto {
  attempted: boolean;
  succeeded: boolean;
  refreshedAtUtc: string | null;
}

/**
 * Returned by the task-scoped, member-reachable refresh. `lastSyncedAtUtc` mirrors the same
 * field on `TaskGitHubActivityDto`, but the panel does not read it from here — it always
 * follows the refresh with a read of its own, so this value is currently unused on the client.
 *
 * `refreshed` is a convenience roll-up and **cannot** distinguish "fetched", "skipped by
 * cooldown" and "failed" — reading it alone caused a wrong diagnosis on 2026-08-30. Read
 * `commits`, `pullRequests` and `failures` instead. The panel does not render these yet; they
 * exist so the API stops conflating the three states.
 */
export interface TaskActivityRefreshDto {
  refreshed: boolean;
  repositoriesRefreshed: number;
  tasksTransitioned: number;
  lastSyncedAtUtc: string | null;
  commits: RefreshPassDto;
  pullRequests: RefreshPassDto;
  failures: SyncFailureDto[];
}

/** Partial success is a result, not an error: a 200 can still carry failures. */
export interface SyncActivityResultDto {
  repositoriesSynced: number;
  commits: number;
  branches: number;
  pullRequests: number;
  linksCreated: number;
  tasksTransitioned: number;
  failures: SyncFailureDto[];
}

export interface BranchRepositoryOptionDto {
  id: number;
  fullName: string;
  defaultBranch: string;
}

/**
 * `taskKey` is here so the dialog can refuse an edit that drops it. The authoritative check is
 * `TaskKeyScanner` on the server — this is a hint, deliberately, rather than a second
 * implementation of the scanner in TypeScript.
 */
export interface BranchSuggestionDto {
  taskKey: string;
  suggestedName: string;
  repositories: BranchRepositoryOptionDto[];
}

export interface CreateBranchDto {
  /** Null when the project links exactly one repository — the server ignores it in that case. */
  repositoryId: number | null;
  name: string;
}

/** `alreadyExisted` is not an error: the branch is there, which is what was asked for. */
export interface CreatedBranchDto {
  id: number;
  name: string;
  headSha: string;
  htmlUrl: string;
  alreadyExisted: boolean;
}
