import { describe, it, expect } from 'vitest';

import { manageInstallationUrl } from './manage-installation-url';
import { GitHubInstallationDto, RepositorySelection } from '../models/github.models';

function installation(over: Partial<GitHubInstallationDto> = {}): GitHubInstallationDto {
  return {
    id: 1,
    installationId: 4242,
    accountLogin: 'acme-corp',
    accountType: 'Organization',
    repositorySelection: RepositorySelection.Selected,
    isSuspended: false,
    ...over,
  };
}

describe('manageInstallationUrl', () => {
  it('points an organization installation at the org settings page', () => {
    expect(manageInstallationUrl(installation())).toBe(
      'https://github.com/organizations/acme-corp/settings/installations/4242',
    );
  });

  it('points a user installation at the personal settings page', () => {
    expect(manageInstallationUrl(installation({ accountType: 'User', accountLogin: 'rigon' }))).toBe(
      'https://github.com/settings/installations/4242',
    );
  });

  it('returns null when the installation already has access to all repositories', () => {
    // There is nothing to grant, so sending the user to GitHub would waste the trip.
    expect(
      manageInstallationUrl(installation({ repositorySelection: RepositorySelection.All })),
    ).toBeNull();
  });

  it('returns null when there is no installation', () => {
    expect(manageInstallationUrl(null)).toBeNull();
  });

  it('escapes a login that would otherwise alter the path', () => {
    expect(manageInstallationUrl(installation({ accountLogin: 'a/b' }))).toBe(
      'https://github.com/organizations/a%2Fb/settings/installations/4242',
    );
  });
});
