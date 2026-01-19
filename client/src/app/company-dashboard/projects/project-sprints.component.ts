import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { of } from 'rxjs';
import { catchError, finalize, switchMap, tap } from 'rxjs/operators';
import {SprintsApiService} from '../../core/services/sprints-api.service';
import {SprintDto} from '../../core/models/sprints.models';

@Component({
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './project-sprints.component.html',
})
export class ProjectSprintsComponent {
  projectId = signal<number | null>(null);

  loading = signal(false);
  error = signal<string | null>(null);

  includeArchived = signal(false);
  sprints = signal<SprintDto[]>([]);

  constructor(
    private route: ActivatedRoute,
    private api: SprintsApiService
  ) {}

  ngOnInit() {
    const raw = this.route.snapshot.paramMap.get('projectId');
    const id = raw ? Number(raw) : NaN;

    if (!Number.isFinite(id) || id <= 0) {
      this.error.set('Invalid project id.');
      return;
    }

    this.projectId.set(id);
    this.loadSprints();
  }

  toggleArchived() {
    this.includeArchived.set(!this.includeArchived());
    this.loadSprints();
  }

  loadSprints() {
    const pid = this.projectId();
    if (!pid) return;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.getByProject(pid, this.includeArchived())),
      tap((list) => {
        const s = (list ?? []).slice();
        s.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(s);
      }),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to load sprints.'));
        this.sprints.set([]);
        return of([]);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  setArchived(s: SprintDto, isArchived: boolean) {
    const pid = this.projectId();
    if (!pid || !s?.id) return;

    if (isArchived && s.isActive) {
      this.error.set('Set the sprint inactive before archiving.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.setArchived(s.id!, isArchived)),
      switchMap(() => this.api.getByProject(pid, this.includeArchived())),
      tap((list) => {
        const arr = (list ?? []).slice();
        arr.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(arr);
      }),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to update sprint archive status.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  private toMsg(err: any, fallback: string): string {
    if (Array.isArray(err?.error)) return err.error.join('\n');
    if (typeof err?.error === 'string') return err.error;
    if (err?.status === 0) return 'API unreachable / CORS error.';
    return fallback;
  }
}
