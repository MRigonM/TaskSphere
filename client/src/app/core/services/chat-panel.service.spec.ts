import { describe, it, expect } from 'vitest';
import { ChatPanelService } from './chat-panel.service';

describe('ChatPanelService', () => {
  it('starts closed and not fullscreen', () => {
    const svc = new ChatPanelService();
    expect(svc.isOpen()).toBe(false);
    expect(svc.isFullscreen()).toBe(false);
    expect(svc.projectId()).toBeNull();
  });

  it('toggle() opens when closed', () => {
    const svc = new ChatPanelService();
    svc.toggle();
    expect(svc.isOpen()).toBe(true);
  });

  it('toggle() closes when open', () => {
    const svc = new ChatPanelService();
    svc.toggle();
    svc.toggle();
    expect(svc.isOpen()).toBe(false);
  });

  it('close() sets isOpen false and clears fullscreen', () => {
    const svc = new ChatPanelService();
    svc.toggle();
    svc.toggleFullscreen();
    svc.close();
    expect(svc.isOpen()).toBe(false);
    expect(svc.isFullscreen()).toBe(false);
  });

  it('toggleFullscreen() flips isFullscreen', () => {
    const svc = new ChatPanelService();
    svc.toggleFullscreen();
    expect(svc.isFullscreen()).toBe(true);
    svc.toggleFullscreen();
    expect(svc.isFullscreen()).toBe(false);
  });

  it('setProjectId() updates projectId signal', () => {
    const svc = new ChatPanelService();
    svc.setProjectId(42);
    expect(svc.projectId()).toBe(42);
    svc.setProjectId(null);
    expect(svc.projectId()).toBeNull();
  });
});
