import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import {ProjectsApiService} from './projects.service';
import {catchError, of, switchMap, tap} from 'rxjs';

@Component({
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './project-page.component.html',
})
export class ProjectPageComponent {
  projectId = signal<number | null>(null);
  projectName = signal<string>('');
  error = signal<string | null>(null);

  constructor(
    private route: ActivatedRoute,
    private projectsApi: ProjectsApiService
  ) {}

  ngOnInit() {
    this.route.paramMap
      .pipe(
        tap(pm => {
          const raw = pm.get('projectId');
          const id = raw ? Number(raw) : NaN;

          if (!Number.isFinite(id) || id <= 0) {
            this.projectId.set(null);
            this.projectName.set('');
            this.error.set('Invalid project id.');
            return;
          }

          this.projectId.set(id);
          this.error.set(null);
        }),
        switchMap(() => {
          const id = this.projectId();
          if (!id) return of(null);

          return this.projectsApi.getAll().pipe(
            tap((list) => {
              const p = (list ?? []).find(x => x.id === id);
              this.projectName.set(p?.name ?? `Project #${id}`);
            })
          );
        }),
        catchError((err) => {
          this.projectName.set('');
          this.error.set('Failed to load project.');
          return of(null);
        })
      )
      .subscribe();
  }
}
