import { Component, EventEmitter, Input, Output } from '@angular/core';
import {CommonModule, NgFor, NgIf} from '@angular/common';
import {TaskDto} from '../../core/models/tasks.models';

@Component({
  selector: 'app-task-card',
  standalone: true,
  imports: [NgFor, NgIf,CommonModule],
  templateUrl: './task-card.component.html',
})
export class TaskCardComponent {
  @Input({ required: true }) task!: TaskDto;
  @Input({ required: true }) statuses!: string[];
  @Input() assigneeName: (id: string | null) => string = () => 'Unassigned';

  @Input() moveLabel: string = '';
  @Input() moveDisabled = false;

  @Output() edit = new EventEmitter<any>();
  @Output() remove = new EventEmitter<any>();
  @Output() setStatus = new EventEmitter<{ task: any; status: string }>();
  @Output() move = new EventEmitter<any>();
}
