import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { catchError, finalize, of, tap } from 'rxjs';

import { apiErrorMessage } from '../../core/http/api-error';
import { CompanyRepositoryLinksDto, RepositoryLinksDto } from '../../core/models/github.models';
import { ProjectDto } from '../../core/models/projects.models';
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

  ngOnInit() {
    this.reload();
  }

  retry() {
    this.reload();
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
