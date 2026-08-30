import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { catchError, finalize, switchMap, tap } from 'rxjs/operators';

import { apiErrorMessage } from '../../core/http/api-error';
import { ProjectDto } from '../../core/models/projects.models';
import { ProjectsApiService } from './projects.service';
import {Router} from '@angular/router';
import {AuthStoreService} from '../../core/services/auth-store.service';
import {ToastService} from '../../core/services/toast.service';
import { TasksApiService } from '../../core/services/tasks-api.service';

@Component({
  selector: 'app-project',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './projects.component.html',
})
export class ProjectComponent {
  loading = signal(false);
  error = signal<string | null>(null);
  projects = signal<ProjectDto[]>([]);

  name = '';
  key = '';

  jumpKey = '';
  jumpError = signal<string | null>(null);

  constructor(
    private projectsApi: ProjectsApiService,
    private router: Router,
    private authStore: AuthStoreService,
    private toast: ToastService,
    private tasksApi: TasksApiService,
  ) {}

  ngOnInit() {
    this.loadProjects();
  }

  loadProjects() {
    this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.authStore.isCompany()
          ? this.projectsApi.getAll()
          : this.projectsApi.getMembersProjects()
        ),
        tap((res) => this.projects.set(res ?? [])),
        catchError((err) => {
          this.error.set(apiErrorMessage(err, 'Failed to load projects.'));
          this.projects.set([]);
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  createProject() {
    const name = this.name.trim();
    const key = this.key.trim().toUpperCase();

    if (!name) return;

    if (!/^[A-Z][A-Z0-9]{1,9}$/.test(key)) {
      this.error.set('Project key must be 2-10 characters, start with a letter, and contain only letters and digits.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.create({ name, key })),
        tap((p) => {
          this.name = '';
          this.key = '';
          if (p) this.projects.set([p, ...this.projects()]);
          this.toast.show('Project was created');
        }),
        catchError((err) => {
          this.error.set(apiErrorMessage(err, 'Failed to create project.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  jumpToKey() {
    const key = this.jumpKey.trim().toUpperCase();
    if (!key) return;

    this.jumpError.set(null);

    this.tasksApi.getByKey(key).subscribe({
      next: (task) => {
        if (!task?.projectId) {
          this.jumpError.set(`No task ${key}`);
          return;
        }

        this.jumpKey = '';
        this.router.navigate(['/sprints', task.projectId], { queryParams: { task: task.id } });
      },
      error: () => this.jumpError.set(`No task ${key}`),
    });
  }

  openProject(p: ProjectDto) {
    if (this.authStore.isCompany()) {
      this.router.navigate(['/dashboard/projects', p.id]);
      return;
    }

    if (this.authStore.isCompanyUser()) {
      this.router.navigate(['/sprints', p.id]);
      return;
    }

    this.router.navigate(['/sprints', p.id]);
  }

  isCompany(): boolean {
    return this.authStore.isCompany();
  }
}
