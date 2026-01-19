import { Injectable, signal } from '@angular/core';
import { AuthResponseDto } from '../models/account.models';

const AUTH_KEY = 'tasksphere_auth';

@Injectable({ providedIn: 'root' })
export class AuthStoreService {
  private _auth = signal<AuthResponseDto | null>(this.read());

  private read(): AuthResponseDto | null {
    const raw = localStorage.getItem(AUTH_KEY);
    return raw ? (JSON.parse(raw) as AuthResponseDto) : null;
  }

  setAuth(auth: AuthResponseDto) {
    localStorage.setItem(AUTH_KEY, JSON.stringify(auth));
    this._auth.set(auth);
  }

  clear() {
    localStorage.removeItem(AUTH_KEY);
    this._auth.set(null);
  }

  auth() {
    return this._auth();
  }

  getToken(): string | null {
    return this._auth()?.token ?? null;
  }

  getName(): string | null {
    return this._auth()?.name ?? null;
  }

  getRole(): string | null {
    return this._auth()?.role ?? null;
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
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
