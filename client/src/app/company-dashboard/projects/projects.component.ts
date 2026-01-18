import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { catchError, finalize, switchMap, tap } from 'rxjs/operators';

import { AddMemberDto, MemberDto, ProjectDto } from '../../core/models/projects.models';
import { ProjectsApiService } from './projects.service';
import { AccountApiService } from '../../core/services/account-api.service';
import {Router} from '@angular/router';

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

  constructor(
    private projectsApi: ProjectsApiService,
    private router: Router
  ) {}

  ngOnInit() {
    this.loadProjects();
  }

  loadProjects() {
    this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.getAll()),
        tap((res) => this.projects.set(res ?? [])),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to load projects.'));
          this.projects.set([]);
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  createProject() {
    const name = this.name.trim();
    if (!name) return;

    this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.create({ name })),
        tap((p) => {
          this.name = '';
          if (p) this.projects.set([p, ...this.projects()]);
        }),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to create project.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  openProject(p: ProjectDto) {
    this.router.navigate(['dashboard/projects', p.id]);
  }

  private toMsg(err: any, fallback: string): string {
    if (Array.isArray(err?.error)) return err.error.join('\n');
    if (typeof err?.error === 'string') return err.error;
    if (err?.status === 0) return 'API unreachable / CORS error.';
    return fallback;
  }
}
