import { CommonModule } from '@angular/common';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, finalize, of, tap } from 'rxjs';

import { GitHubReturnSyncService } from '../../core/github/github-return-sync.service';
import { manageInstallationUrl } from '../../core/github/manage-installation-url';
import { apiErrorMessage } from '../../core/http/api-error';
import { CompanyRepositoryLinksDto, RepositoryLinksDto } from '../../core/models/github.models';
import { ProjectDto } from '../../core/models/projects.models';
import { GitHubConnectionService } from '../../core/services/github-connection.service';
import { GitHubProjectLinkService } from '../../core/services/github-project-link.service';
import { ProjectsApiService } from '../projects/projects.service';

/**
 * Repository-first view of the repository↔project link table. The predecessor rendered one
 * project at a time, which made a many-to-many model look like a one-to-many — see
 * docs/superpowers/specs/2026-08-10-github-links-view-design.md.
 */
@Component({
  selector: 'app-github-repository-links',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './github-repository-links.component.html',
})
export class GitHubRepositoryLinksComponent implements OnInit {
  private projectsApi = inject(ProjectsApiService);
  private links = inject(GitHubProjectLinkService);
  private github = inject(GitHubConnectionService);
  private returnSync = inject(GitHubReturnSyncService);
  private destroyRef = inject(DestroyRef);

  /** null means unknown — not read yet, or the read failed. Never "nothing is linked". */
  data = signal<CompanyRepositoryLinksDto | null>(null);
  projects = signal<ProjectDto[]>([]);

  /** null is "All projects". A filter over one table, not a mode. */
  filterProjectId = signal<number | null>(null);

  loading = signal(false);
  /** Per-row, so one repository's mutation does not freeze the whole table. */
  busyRepoId = signal<number | null>(null);
  pickerRepoId = signal<number | null>(null);
  error = signal<string | null>(null);

  /**
   * Filtering narrows which repositories are listed. It deliberately does NOT narrow the chips
   * on a row: hiding a repository's other projects is the exact misreading this screen removes.
   */
  rows = computed(() => {
    const all = this.data()?.repositories ?? [];
    const projectId = this.filterProjectId();
    return projectId === null ? all : all.filter(r => r.projects.some(p => p.id === projectId));
  });

  unavailable = computed(() => {
    const all = this.data()?.unavailable ?? [];
    const projectId = this.filterProjectId();
    return projectId === null ? all : all.filter(u => u.projectId === projectId);
  });

  /**
   * null when there is nothing to do on GitHub. The connection is read by the parent screen this
   * component renders inside, so there is nothing to load here.
   */
  manageUrl = computed(() => manageInstallationUrl(this.github.connection()?.installation ?? null));

  ngOnInit() {
    this.reload();

    // takeUntilDestroyed is mandatory: returned$ never completes, so a destroyed screen would
    // keep syncing and keep consuming an arm meant for the screen the user is on now.
    this.returnSync.returned$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.github
        .refreshRepositories()
        .pipe(
          tap(() => this.load()),
          catchError(err => {
            this.error.set(apiErrorMessage(err, 'Failed to refresh the repositories.'));
            return of(null);
          })
        )
        .subscribe();
    });
  }

  /**
   * Opens the installation's own settings in a new tab. A new tab rather than a navigation: the
   * return is detected by this document regaining visibility, which only works if it survives.
   */
  openGitHubSettings() {
    const url = this.manageUrl();
    if (!url) return;

    this.returnSync.arm();
    window.open(url, '_blank');
  }

  retry() {
    this.reload();
  }

  setFilter(value: string) {
    this.filterProjectId.set(value ? +value : null);
    this.pickerRepoId.set(null);
  }

  unlink(repositoryId: number, projectId: number) {
    this.busyRepoId.set(repositoryId);
    this.error.set(null);
    this.links
      .unlink(projectId, repositoryId)
      .pipe(
        // Refetched, not patched: `unavailable` is derivable only server-side, so a local edit
        // could leave the table stating something the server would contradict.
        tap(() => this.load()),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to unlink the repository.'));
          return of(null);
        }),
        finalize(() => this.busyRepoId.set(null))
      )
      .subscribe();
  }

  togglePicker(repositoryId: number) {
    this.pickerRepoId.update(open => (open === repositoryId ? null : repositoryId));
  }

  link(repositoryId: number, projectId: number) {
    this.busyRepoId.set(repositoryId);
    this.error.set(null);
    this.links
      .link(projectId, repositoryId)
      .pipe(
        tap(() => {
          this.pickerRepoId.set(null);
          this.load();
        }),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to link the repository.'));
          return of(null);
        }),
        finalize(() => this.busyRepoId.set(null))
      )
      .subscribe();
  }

  /**
   * Both reads write the one `error` signal, so it is cleared here rather than inside either of
   * them: clearing it in `load()` would silently wipe a project-read failure that had already
   * landed, and make the order of these two calls load-bearing.
   */
  private reload() {
    this.error.set(null);
    this.loadProjects();
    this.load();
  }

  load() {
    this.loading.set(true);
    this.links
      .getCompanyLinks()
      .pipe(
        tap(data => this.data.set(data)),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to load the repository links.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  loadProjects() {
    this.projectsApi
      .getAll()
      .pipe(
        tap(projects => this.projects.set(projects)),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to load the projects.'));
          return of(null);
        })
      )
      .subscribe();
  }

  /** Projects this repository is not linked to yet — everything the [+] picker may offer. */
  available(repository: RepositoryLinksDto): ProjectDto[] {
    const linked = new Set(repository.projects.map(p => p.id));
    return this.projects().filter(p => !linked.has(p.id));
  }
}
