import { Component, EventEmitter, Input, Output } from '@angular/core';
import { NgFor, NgIf } from '@angular/common';
import { BoardTaskCardComponent } from './board-task-card.component';
import { CdkDropList, CdkDrag, CdkDragDrop, } from '@angular/cdk/drag-drop';

@Component({
  selector: 'app-board-column',
  standalone: true,
  imports: [NgFor, NgIf, BoardTaskCardComponent, CdkDropList, CdkDrag],
  templateUrl: './board-column.component.html'
})
export class BoardColumnComponent {
  @Input({ required: true }) title!: string;
  @Input({ required: true }) tasks!: any[];
  @Input({ required: true }) statuses!: string[];
  @Input({ required: true }) users!: any[];
  @Input({ required: true }) listId!: string;
  @Input({ required: true }) connectedTo!: string[];

  @Input() taskTitle: (x: any) => string = (x) => x?.title ?? '';
  @Input() taskStatus: (x: any) => string = (x) => x?.status ?? '';

  @Output() statusChange = new EventEmitter<{ t: any; status: string }>();
  @Output() assigneeChange = new EventEmitter<{ t: any; assigneeUserId: string}>();
  @Output() dropped = new EventEmitter<CdkDragDrop<any[]>>();

  trackByTask = (_: number, t: any) => t?.id ?? t?.taskId ?? t;
  private scrollY = 0;

  onDrop(ev: CdkDragDrop<any[]>) {
    this.dropped.emit(ev);
  }

  onDragStarted() {
    this.scrollY = window.scrollY;
    document.body.style.position = 'fixed';
    document.body.style.top = `-${this.scrollY}px`;
    document.body.style.width = '100%';
  }

  onDragEnded() {
    document.body.style.position = '';
    document.body.style.top = '';
    document.body.style.width = '';
    window.scrollTo(0, this.scrollY);
  }
}
