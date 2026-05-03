import { Component, OnDestroy, OnInit, ViewChild, ElementRef } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService } from '../core/services/chat.service';
import { AuthStoreService } from '../core/services/auth-store.service';
import { environment } from '../../environments/environment';

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
  uploading = false;

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

  onPaste(event: ClipboardEvent): void {
    const items = event.clipboardData?.items;
    if (!items) return;

    for (let i = 0; i < items.length; i++) {
      if (items[i].type.startsWith('image/')) {
        event.preventDefault();
        const file = items[i].getAsFile();
        if (file) this.uploadAndSend(file);
        return;
      }
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.uploadAndSend(file);
    input.value = '';
  }

  private uploadAndSend(file: File): void {
    this.uploading = true;
    this.chatService.uploadImage(file).subscribe({
      next: (res) => {
        this.chatService.sendMessage(this.messageInput.trim(), res.url);
        this.messageInput = '';
        this.uploading = false;
      },
      error: () => {
        this.uploading = false;
      },
    });
  }

  resolveImageUrl(url: string): string {
    if (url.startsWith('http')) return url;
    return environment.apiUrl.replace('/api/', '') + url;
  }

  openImage(url: string): void {
    window.open(this.resolveImageUrl(url), '_blank');
  }

  isOwnMessage(senderId: string): boolean {
    return senderId === this.authStore.auth()?.userId;
  }
}
