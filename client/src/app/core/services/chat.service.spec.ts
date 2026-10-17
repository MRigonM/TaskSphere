import { describe, it, expect, vi } from 'vitest';
import { ChatService } from './chat.service';

function setup(rejection: unknown) {
  const toast = { show: vi.fn() };
  const svc = new ChatService({} as any, {} as any, toast as any);
  (svc as any).hubConnection = { invoke: vi.fn().mockRejectedValue(rejection) };
  svc.currentProjectId.set(7);
  return { svc, toast };
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('ChatService.sendMessage', () => {
  it('shows the server text of a refused message as an error toast', async () => {
    const { svc, toast } = setup(new Error(
      "An unexpected error occurred invoking 'SendMessage' on the server. HubException: You are sending messages too fast. Wait a moment."));

    svc.sendMessage('hi');
    await flush();

    expect(toast.show).toHaveBeenCalledWith('You are sending messages too fast. Wait a moment.', 'error');
  });

  it('falls back to a plain sentence when the error carries no server text', async () => {
    const { svc, toast } = setup(new Error('WebSocket closed with status code: 1006'));

    svc.sendMessage('hi');
    await flush();

    expect(toast.show).toHaveBeenCalledWith('Your message could not be sent.', 'error');
  });
});
