import { GitHubInstallationDto, RepositorySelection } from '../models/github.models';

/**
 * Where to send a user who wants the installation to see a repository it currently cannot.
 *
 * Returns null when there is nothing for them to do there — either the company is not connected,
 * or the installation already has access to every repository, in which case the repository list
 * only needs a refresh.
 *
 * TaskSphere cannot add the repository itself: GitHub's
 * `PUT /user/installations/{id}/repositories/{id}` needs a user credential with admin access to
 * the repository, and this app deliberately never persists one.
 */
export function manageInstallationUrl(installation: GitHubInstallationDto | null): string | null {
  if (!installation) return null;
  if (installation.repositorySelection === RepositorySelection.All) return null;

  const id = installation.installationId;

  return installation.accountType === 'Organization'
    ? `https://github.com/organizations/${encodeURIComponent(installation.accountLogin)}/settings/installations/${id}`
    : `https://github.com/settings/installations/${id}`;
}
