import { Injectable } from '@angular/core';
import { AuthResponseDto } from '../models/account.models';

const AUTH_KEY = 'tasksphere_auth';

@Injectable({ providedIn: 'root' })
export class AuthStoreService {
  setAuth(auth: AuthResponseDto) {
    localStorage.setItem(AUTH_KEY, JSON.stringify(auth));
  }

  getAuth(): AuthResponseDto | null {
    const raw = localStorage.getItem(AUTH_KEY);
    return raw ? (JSON.parse(raw) as AuthResponseDto) : null;
  }

  getToken(): string | null {
    return this.getAuth()?.token ?? null;
  }

  getName(): string | null {
    return this.getAuth()?.name ?? null;
  }

  clear() {
    localStorage.removeItem(AUTH_KEY);
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  getRole(): string | null {
    return this.getAuth()?.role ?? null;
  }

  isCompany(): boolean {
    return this.getRole()?.toLowerCase() === 'company';
  }

  isCompanyUser(): boolean {
    return this.getRole()?.toLowerCase() === 'user';
  }

  isCompanyMember(): boolean {
    const role = this.getRole()?.toLowerCase();
    return role === 'company' || role === 'user';
  }
}
