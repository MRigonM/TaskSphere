import { Component, effect, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService } from '../core/services/chat.service';
import { ChatPanelService } from '../core/services/chat-panel.service';
import { AuthStoreService } from '../core/services/auth-store.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-chat-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './chat-panel.component.html',
})
export class ChatPanelComponent {
  @ViewChild('messagesContainer') messagesContainer!: ElementRef<HTMLDivElement>;

  messageInput = '';
  uploading = false;

  constructor(
    protected chatService: ChatService,
    protected panelService: ChatPanelService,
    protected authStore: AuthStoreService,
  ) {
    effect(() => {
      const id = this.panelService.projectId();
      if (id) {
        this.chatService.connect(id);
      } else {
        this.chatService.disconnect();
      }
    });

    effect(() => {
      this.chatService.messages();
      setTimeout(() => this.scrollToBottom(), 0);
    });

    effect(() => {
      if (this.panelService.isOpen()) {
        setTimeout(() => this.scrollToBottom(), 0);
      }
    });
  }

  private scrollToBottom(): void {
    const el = this.messagesContainer?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
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
      error: () => { this.uploading = false; },
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
