import {Component, EventEmitter, Input, Output, signal} from '@angular/core';
import {CommonModule, NgFor, NgIf, UpperCasePipe} from '@angular/common';
import {UserDto} from '../../core/models/account.models';
import {TaskEntityDto} from '../../core/models/sprints.models';

@Component({
  selector: 'app-board-task-card',
  standalone: true,
  imports: [NgFor, NgIf, CommonModule],
  templateUrl: './board-task-card.component.html',
})
export class BoardTaskCardComponent {
  assigneeMenuOpenFor = signal<number | null>(null);
  @Input({ required: true }) t!: TaskEntityDto;
  @Input({ required: true }) users!: UserDto[];
  @Input() taskTitle: (x: any) => string = (x) => x?.title ?? '';
  @Input() taskStatus: (x: any) => string = (x) => x?.status ?? '';

  @Output() assigneeChange = new EventEmitter<{ t: any; assigneeUserId: string | null }>();
  @Output() open = new EventEmitter<any>();

  protected readonly String = String;

  hasUser(id: string | null | undefined): boolean {
    if (!id) return false;
    return this.users?.some(u => String(u.id) === String(id)) ?? false;
  }

  priorityBadgeClass(priority: string | null | undefined): string {
    const p = (priority ?? '').toLowerCase();

    if (p === 'critical') return 'border-red-500/30 bg-red-500/10 text-red-200';
    if (p === 'high') return 'border-orange-500/30 bg-orange-500/10 text-orange-200';
    if (p === 'medium') return 'border-amber-500/30 bg-amber-500/10 text-amber-200';
    if (p === 'low') return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-200';

    return 'border-white/10 bg-white/5 text-slate-300';
  }
}
