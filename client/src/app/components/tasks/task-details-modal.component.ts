import { Component, EventEmitter, HostListener, Input, Output, signal, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { UserDto } from '../../core/models/account.models';
import { TasksApiService } from '../../core/services/tasks-api.service';
import {UpdateTaskDto} from '../../core/models/tasks.models';

@Component({
  selector: 'app-task-details-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './task-details-modal.component.html'
})
export class TaskDetailsModalComponent {
  @Input() task!: any;
  @Input() users: UserDto[] = [];
  @Input() sprints: any[] = [];
  @Input() canEditSprint = false;

  form: FormGroup;

  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  loading = signal(false);
  error = signal<string | null>(null);

  constructor(
    private fb: FormBuilder,
    private tasksApi: TasksApiService
  ) {
    this.form = this.fb.group({
      title: ['', [Validators.required]],
      description: [''],
      status: ['Open', [Validators.required]],
      priority: [''],
      storyPoints: [''],
      sprintId: [''],
      assigneeUserId: [''],
    });
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['task']?.currentValue) {
      const t = changes['task'].currentValue;

      this.form.reset({
        title: t.title ?? '',
        description: t.description ?? '',
        status: t.status ?? 'Open',
        priority: t.priority ?? '',
        storyPoints: t.storyPoints ?? '',
        sprintId: t.sprintId ? String(t.sprintId) : '',
        assigneeUserId: t.assigneeUserId ?? '',
      });

      this.error.set(null);
      this.loading.set(false);
    }
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(ev: KeyboardEvent) {
    if (ev.key === 'Escape') this.close();
  }

  save() {
    if (!this.task?.id) return;
    if (this.form.invalid) return;

    this.loading.set(true);
    this.error.set(null);

    const v = this.form.value;

    const dto: UpdateTaskDto = {
      title: (v.title ?? '').trim(),
      description: (v.description ?? '') || null,
      status: v.status ?? 'Open',
      priority: (v.priority ?? '') || null,
      storyPoints: this.toNullableNumber(v.storyPoints),
      sprintId: v.sprintId ? Number(v.sprintId) : null,
      assigneeUserId: (v.assigneeUserId ?? '') || null
    };

    if (!dto.title) {
      this.loading.set(false);
      return;
    }

    this.tasksApi.update(this.task.id, dto).subscribe({
      next: () => {
        this.saved.emit();
        this.closed.emit();
      },
      error: () => {
        this.error.set('Failed to save task.');
        this.loading.set(false);
      }
    });
  }

  close() {
    this.closed.emit();
  }

  private toNullableNumber(v: any): number | null {
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  }
}
