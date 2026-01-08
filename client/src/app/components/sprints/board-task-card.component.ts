import { Component, EventEmitter, Input, Output } from '@angular/core';
import { NgFor } from '@angular/common';
import {UserDto} from '../../core/models/account.models';
import {TaskEntityDto} from '../../core/models/sprints.models';

@Component({
  selector: 'app-board-task-card',
  standalone: true,
  imports: [NgFor],
  templateUrl: './board-task-card.component.html',
})
export class BoardTaskCardComponent {
  @Input({ required: true }) t!: TaskEntityDto;
  @Input({ required: true }) statuses!: string[];
  @Input({ required: true }) users!: UserDto[];
  @Input() taskTitle: (x: any) => string = (x) => x?.title ?? '';
  @Input() taskStatus: (x: any) => string = (x) => x?.status ?? '';

  @Output() statusChange = new EventEmitter<{ t: any; status: string }>();
  @Output() assigneeChange = new EventEmitter<{ t: any; assigneeUserId: string}>();
}
