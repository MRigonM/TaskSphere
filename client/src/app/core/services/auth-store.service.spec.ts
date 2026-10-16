import { describe, it, expect, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { AuthStoreService } from './auth-store.service';

describe('AuthStoreService.isPersonal', () => {
  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  const auth = (isPersonal?: boolean) => ({
    token: 't', name: 'Rigon', userId: 'u', email: 'r@example.com', role: 'Company', companyId: 'c', isPersonal,
  });

  it('is true for a personal workspace', () => {
    const store = TestBed.inject(AuthStoreService);
    store.setAuth(auth(true));
    expect(store.isPersonal()).toBe(true);
  });

  it('is false for a company workspace and for auth stored before the flag existed', () => {
    const store = TestBed.inject(AuthStoreService);
    store.setAuth(auth(false));
    expect(store.isPersonal()).toBe(false);
    store.setAuth(auth(undefined));
    expect(store.isPersonal()).toBe(false);
  });
});
