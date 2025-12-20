import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import {
  AuthResponseDto,
  LoginDto,
  RegisterDto,
  UpdateUserDto,
  UserDto,
  UserQueryDto,
} from '../models/account.models';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AccountApiService {
  private base = `${environment.apiUrl}Account/`;

  constructor(private http: HttpClient) {}

  register(dto: RegisterDto): Observable<string> {
    return this.http.post(`${this.base}Register`, dto, { responseType: 'text' });
  }

  login(dto: LoginDto): Observable<AuthResponseDto> {
    return this.http.post<AuthResponseDto>(`${this.base}Login`, dto);
  }

  createUser(dto: RegisterDto): Observable<void> {
    return this.http.post<void>(`${this.base}CreateUser`, dto);
  }

  getUsers(query: UserQueryDto): Observable<UserDto[]> {
    let params = new HttpParams();
    if (query.name) params = params.set('name', query.name);
    if (query.email) params = params.set('email', query.email);
    params = params.set('page', String(query.page ?? 1));
    params = params.set('pageSize', String(query.pageSize ?? 20));

    return this.http.get<UserDto[]>(`${this.base}Users`, { params });
  }

  updateUser(userId: string, dto: UpdateUserDto): Observable<void> {
    return this.http.put<void>(`${this.base}Users/${userId}`, dto);
  }

  deleteUser(userId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}Users/${userId}`);
  }
}
