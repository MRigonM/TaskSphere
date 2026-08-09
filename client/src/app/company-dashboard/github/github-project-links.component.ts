import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { catchError, finalize, of, tap } from 'rxjs';

import { apiErrorMessage } from '../../core/http/api-error';
import { ProjectRepositoriesDto } from '../../core/models/github.models';
import { ProjectDto } from '../../core/models/projects.models';
import { AuthStoreService } from '../../core/services/auth-store.service';
import { GitHubConnectionService } from '../../core/services/github-connection.service';
import { GitHubProjectLinkService } from '../../core/services/github-project-link.service';
import { ProjectsApiService } from '../projects/projects.service';

@Component({
  selector: 'app-github-project-links',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './github-project-links.component.html',
})
export class GitHubProjectLinksComponent implements OnInit {
  /**
   * Survives a reload so the screen comes back to the project you were working on. Without it
   * a refresh drops to "Select a project…" with the Linked list hidden, which looks exactly
   * like the links were deleted.
   */
  private static readonly SelectedProjectKey = 'tasksphere_github_project';

  auth = inject(AuthStoreService);
  private projectsApi = inject(ProjectsApiService);
  private links = inject(GitHubProjectLinkService);
  private github = inject(GitHubConnectionService);

  /** Read live off the root signal: a disconnect on this screen must empty the list at once. */
  connection = this.github.connection;

  projects = signal<ProjectDto[]>([]);
  selectedProjectId = signal<number | null>(null);

  /** null means unknown — no project chosen yet, or the read failed — not "nothing is linked". */
  repositories = signal<ProjectRepositoriesDto | null>(null);

  loading = signal(false);
  busy = signal(false);
  error = signal<string | null>(null);

  /** Installation repositories not yet linked to the chosen project. */
  available = computed(() => {
    const linked = new Set(this.repositories()?.links.map(l => l.gitHubRepositoryId));
    return (this.connection()?.repositories ?? []).filter(r => !linked.has(r.id));
  });

  ngOnInit() {
    this.loadProjects();
  }

  loadProjects() {
    this.error.set(null);
    this.projectsApi
      .getAll()
      .pipe(
        tap(projects => {
          this.projects.set(projects);
          this.restoreSelection();
        }),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to load the projects.'));
          return of(null);
        })
      )
      .subscribe();
  }

  /**
   * Only restores an id the company actually has. A stale id left by another tenant or a
   * deleted project would otherwise be requested on every load and answered with a 404.
   */
  private restoreSelection() {
    const stored = localStorage.getItem(GitHubProjectLinksComponent.SelectedProjectKey);
    if (!stored) return;

    if (!this.projects().some(p => p.id === +stored)) {
      localStorage.removeItem(GitHubProjectLinksComponent.SelectedProjectKey);
      return;
    }

    this.selectProject(stored);
  }

  selectProject(value: string) {
    const projectId = value ? +value : null;
    this.selectedProjectId.set(projectId);
    this.repositories.set(null);
    this.error.set(null);

    if (projectId === null) {
      localStorage.removeItem(GitHubProjectLinksComponent.SelectedProjectKey);
      return;
    }

    localStorage.setItem(GitHubProjectLinksComponent.SelectedProjectKey, String(projectId));

    this.loading.set(true);
    this.links
      .getProjectRepositories(projectId)
      .pipe(
        tap(repositories => this.repositories.set(repositories)),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to load the linked repositories.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  link(repositoryId: number) {
    const projectId = this.selectedProjectId();
    if (projectId === null) return;

    this.busy.set(true);
    this.error.set(null);
    this.links
      .link(projectId, repositoryId)
      .pipe(
        tap(link => this.repositories.update(r => r && { ...r, links: [...r.links, link] })),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to link the repository.'));
          return of(null);
        }),
        finalize(() => this.busy.set(false))
      )
      .subscribe();
  }

  unlink(repositoryId: number) {
    const projectId = this.selectedProjectId();
    if (projectId === null) return;

    this.busy.set(true);
    this.error.set(null);
    this.links
      .unlink(projectId, repositoryId)
      .pipe(
        tap(() =>
          this.repositories.update(
            r =>
              r && {
                ...r,
                links: r.links.filter(l => l.gitHubRepositoryId !== repositoryId),
              }
          )
        ),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to unlink the repository.'));
          return of(null);
        }),
        finalize(() => this.busy.set(false))
      )
      .subscribe();
  }
}
