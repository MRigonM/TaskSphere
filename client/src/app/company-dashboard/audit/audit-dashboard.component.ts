import { CommonModule } from '@angular/common';
import { Component, computed, HostListener, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { catchError, finalize, of, switchMap, tap } from 'rxjs';
import { BaseChartDirective } from 'ng2-charts';
import { ChartData, ChartOptions } from 'chart.js';

import { apiErrorMessage } from '../../core/http/api-error';
import { AuditService } from '../../core/services/audit.service';
import { AuditLogDto, AuditStatsDto } from '../../core/models/audit.models';

@Component({
  selector: 'app-audit-dashboard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, BaseChartDirective],
  templateUrl: './audit-dashboard.component.html',
})
export class AuditDashboardComponent implements OnInit {
  private fb = inject(FormBuilder);
  private audit = inject(AuditService);

  loading = signal(false);
  statsLoading = signal(false);
  error = signal<string | null>(null);

  logs = signal<AuditLogDto[]>([]);
  stats = signal<AuditStatsDto | null>(null);
  selectedLog = signal<AuditLogDto | null>(null);

  page = signal(1);
  pageSize = signal(50);
  pageSizeOptions = [20, 50, 100];
  dayOptions = [7, 30, 90];
  days = signal<7 | 30 | 90>(30);

  canPrev = computed(() => this.page() > 1);
  canNext = computed(() => this.logs().length === this.pageSize());

  searchForm = this.fb.nonNullable.group({
    username: [''],
    action: [''],
  });

  chartData = computed<ChartData<'line'>>(() => {
    const s = this.stats();
    if (!s) return { labels: [], datasets: [] };
    return {
      labels: s.requestsPerDay.map(d => d.date),
      datasets: [{
        data: s.requestsPerDay.map(d => d.count),
        borderColor: '#6366f1',
        backgroundColor: 'rgba(99,102,241,0.15)',
        pointBackgroundColor: '#6366f1',
        pointRadius: 3,
        fill: true,
        tension: 0.4,
        label: 'Requests',
      }],
    };
  });

  chartOptions: ChartOptions<'line'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
    },
    scales: {
      x: {
        ticks: { color: 'rgba(255,255,255,0.6)' },
        grid: { color: 'rgba(255,255,255,0.05)' },
      },
      y: {
        ticks: { color: 'rgba(255,255,255,0.6)' },
        grid: { color: 'rgba(255,255,255,0.05)' },
      },
    },
  };

  ngOnInit() {
    this.loadLogs();
    this.loadStats();
  }

  loadLogs() {
    const { username, action } = this.searchForm.value;
    this.loading.set(true);
    this.error.set(null);
    of(null)
      .pipe(
        switchMap(() =>
          this.audit.getPaged({
            username: username?.trim() || undefined,
            action: action?.trim() || undefined,
            page: this.page(),
            pageSize: this.pageSize(),
          })
        ),
        tap(res => this.logs.set(res.items)),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to load audit logs.'));
          this.logs.set([]);
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  loadStats() {
    this.statsLoading.set(true);
    of(null)
      .pipe(
        switchMap(() => this.audit.getStats(this.days())),
        tap(res => this.stats.set(res)),
        catchError(() => {
          this.stats.set(null);
          return of(null);
        }),
        finalize(() => this.statsLoading.set(false))
      )
      .subscribe();
  }

  search() {
    this.page.set(1);
    this.loadLogs();
  }

  clearSearch() {
    this.searchForm.reset({ username: '', action: '' });
    this.page.set(1);
    this.loadLogs();
  }

  setDays(d: number) {
    this.days.set(d as 7 | 30 | 90);
    this.loadStats();
  }

  prev() {
    if (!this.canPrev()) return;
    this.page.update(p => p - 1);
    this.loadLogs();
  }

  next() {
    if (!this.canNext()) return;
    this.page.update(p => p + 1);
    this.loadLogs();
  }

  onPageSizeChange(ps: number) {
    this.pageSize.set(ps);
    this.page.set(1);
    this.loadLogs();
  }

  openDetail(log: AuditLogDto) {
    this.selectedLog.set(log);
  }

  closeDetail() {
    this.selectedLog.set(null);
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: Event) {
    if (!this.selectedLog()) return;
    const e = event as KeyboardEvent;
    if (e.key !== 'Escape') return;
    e.preventDefault();
    this.closeDetail();
  }

  formatJson(raw: string | null): string {
    if (!raw) return '—';
    try {
      return JSON.stringify(JSON.parse(raw), null, 2);
    } catch {
      return raw;
    }
  }


  /**
   * An entry with no HTTP method came from inside the app, not from a request: the merge →
   * Done transition is the first of these. Its method, path, IP, status and duration are all
   * empty by design, and rendering the zero status through statusClass would paint a
   * successful transition red.
   */
  hasNoRequest(log: AuditLogDto): boolean {
    return !log.httpMethod;
  }
  statusClass(code: number): string {
    if (code >= 200 && code < 300)
      return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-200';
    if (code >= 400 && code < 500)
      return 'border-amber-500/30 bg-amber-500/10 text-amber-200';
    return 'border-red-500/30 bg-red-500/10 text-red-200';
  }

  methodClass(method: string | null): string {
    switch (method?.toUpperCase()) {
      case 'GET':    return 'border-blue-500/30 bg-blue-500/10 text-blue-200';
      case 'POST':   return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-200';
      case 'PUT':    return 'border-amber-500/30 bg-amber-500/10 text-amber-200';
      case 'DELETE': return 'border-red-500/30 bg-red-500/10 text-red-200';
      case 'PATCH':  return 'border-purple-500/30 bg-purple-500/10 text-purple-200';
      default:       return 'border-white/10 bg-white/5 text-white/60';
    }
  }
}
