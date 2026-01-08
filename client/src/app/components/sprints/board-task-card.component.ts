import { Component, EventEmitter, Input, Output } from '@angular/core';
import { NgFor, NgIf } from '@angular/common';
import {UserDto} from '../../core/models/account.models';
import {TaskEntityDto} from '../../core/models/sprints.models';

@Component({
  selector: 'app-board-task-card',
  standalone: true,
  imports: [NgFor, NgIf],
  templateUrl: './board-task-card.component.html',
})
export class BoardTaskCardComponent {
  @Input({ required: true }) t!: TaskEntityDto;
  @Input({ required: true }) users!: UserDto[];
  @Input() taskTitle: (x: any) => string = (x) => x?.title ?? '';
  @Input() taskStatus: (x: any) => string = (x) => x?.status ?? '';

  @Output() assigneeChange = new EventEmitter<{ t: any; assigneeUserId: string | null }>();

  protected readonly String = String;

  hasUser(id: string | null | undefined): boolean {
    if (!id) return false;
    return this.users?.some(u => String(u.id) === String(id)) ?? false;
  }
}
