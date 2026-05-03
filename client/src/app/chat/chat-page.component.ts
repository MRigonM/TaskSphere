import { Component, OnDestroy, OnInit, ViewChild, ElementRef } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService } from '../core/services/chat.service';
import { AuthStoreService } from '../core/services/auth-store.service';

@Component({
  selector: 'app-chat-page',
  standalone: true,
  templateUrl: './chat-page.component.html',
  imports: [CommonModule, FormsModule],
})
export class ChatPageComponent implements OnInit, OnDestroy {
  @ViewChild('messagesContainer') messagesContainer!: ElementRef<HTMLDivElement>;

  messageInput = '';
  projectId = 0;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    protected chatService: ChatService,
    protected authStore: AuthStoreService,
  ) {}

  ngOnInit(): void {
    this.projectId = +this.route.snapshot.params['projectId'];
    this.chatService.checkAccess(this.projectId).subscribe((hasAccess) => {
      if (!hasAccess) {
        this.router.navigate(['/dashboard/projects']);
        return;
      }
      this.chatService.connect(this.projectId);
    });
  }

  ngOnDestroy(): void {
    this.chatService.disconnect();
  }

  send(): void {
    const content = this.messageInput.trim();
    if (!content) return;
    this.chatService.sendMessage(content);
    this.messageInput = '';
  }

  isOwnMessage(senderId: string): boolean {
    return senderId === this.authStore.auth()?.userId;
  }
}