import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthStoreService } from './auth-store.service';
import { ChatMessageDto, PagedResult } from '../models/chat.models';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private hubConnection: signalR.HubConnection | null = null;
  private hubUrl = environment.apiUrl.replace('/api/', '/hubs/chat');

  messages = signal<ChatMessageDto[]>([]);
  connected = signal(false);
  currentProjectId = signal<number | null>(null);

  constructor(
    private http: HttpClient,
    private authStore: AuthStoreService,
  ) {}

  connect(projectId: number): void {
    if (this.hubConnection && this.currentProjectId() === projectId) return;

    this.disconnect();
    this.currentProjectId.set(projectId);
    this.messages.set([]);

    const token = this.authStore.getToken();
    if (!token) return;

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(this.hubUrl, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('ReceiveMessage', (message: ChatMessageDto) => {
      this.messages.update((msgs) => [...msgs, message]);
    });

    this.hubConnection.on('Error', (error: string) => {
      console.error('Chat hub error:', error);
    });

    this.hubConnection
      .start()
      .then(() => {
        this.connected.set(true);
        this.hubConnection!.invoke('JoinProject', projectId);
        this.loadHistory(projectId);
      })
      .catch((err) => console.error('SignalR connection error:', err));
  }

  disconnect(): void {
    if (this.hubConnection) {
      const projectId = this.currentProjectId();
      if (projectId) {
        this.hubConnection.invoke('LeaveProject', projectId).catch(() => {});
      }
      this.hubConnection.stop();
      this.hubConnection = null;
      this.connected.set(false);
      this.currentProjectId.set(null);
    }
  }

  sendMessage(content: string): void {
    const projectId = this.currentProjectId();
    if (!this.hubConnection || !projectId) return;

    this.hubConnection.invoke('SendMessage', { projectId, content });
  }

  checkAccess(projectId: number): Observable<boolean> {
    const url = `${environment.apiUrl}Chat/projects/${projectId}/messages?page=1&pageSize=1`;
    return this.http.get<PagedResult<ChatMessageDto>>(url).pipe(
      map(() => true),
      catchError((err: HttpErrorResponse) => of(err.status !== 403)),
    );
  }

  private loadHistory(projectId: number): void {
    const url = `${environment.apiUrl}Chat/projects/${projectId}/messages?page=1&pageSize=100`;
    this.http.get<PagedResult<ChatMessageDto>>(url).subscribe({
      next: (result) => {
        this.messages.set(result.items.reverse());
      },
    });
  }
}
