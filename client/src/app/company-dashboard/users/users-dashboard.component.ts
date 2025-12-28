import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { catchError, finalize, of, switchMap, tap } from 'rxjs';

import { AccountApiService } from '../../core/services/account-api.service';
import { RegisterDto, UpdateUserDto, UserDto, UserQueryDto } from '../../core/models/account.models';

@Component({
  selector: 'app-company-dashboard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './users-dashboard.component.html',
})
export class UsersDashboardComponent {
  private fb = inject(FormBuilder);
  private account = inject(AccountApiService);

  loading = signal(false);
  error = signal<string | null>(null);

  users = signal<UserDto[]>([]);

  page = signal(1);
  pageSize = signal(20);
  pageSizeOptions = [10, 20, 50, 100];

  showingRange = computed(() => {
    const p = this.page();
    const ps = this.pageSize();
    const start = this.users().length ? (p - 1) * ps + 1 : 0;
    const end = (p - 1) * ps + this.users().length;
    return { start, end };
  });

  canPrev = computed(() => this.page() > 1);
  canNext = computed(() => this.users().length === this.pageSize());


  searchForm = this.fb.nonNullable.group({
    name: [''],
    email: [''],
  });

  modalOpen = signal(false);
  modalMode = signal<'create' | 'edit'>('create');
  editingUserId = signal<string | null>(null);

  title = computed(() => (this.modalMode() === 'create' ? 'Create user' : 'Edit user'));

  userForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2)]],
    email: ['', [Validators.required, Validators.email]],

    password: [''],
    confirmPassword: [''],

    newPassword: [''],
    confirmNewPassword: [''],
  });

  ngOnInit() {
    this.load();
  }

  load() {
    const query: UserQueryDto = {
      name: this.searchForm.value.name?.trim() || undefined,
      email: this.searchForm.value.email?.trim() || undefined,
      page: this.page(),
      pageSize: this.pageSize(),
    };

    this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.account.getUsers(query)),
        tap((res) => this.users.set(res ?? [])),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to load users.'));
          this.users.set([]);
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  search() {
    this.page.set(1);
    this.load();
  }

  clearSearch() {
    this.searchForm.reset({ name: '', email: '' });
    this.page.set(1);
    this.load();
  }

  prev() {
    if (!this.canPrev()) return;
    this.page.update(p => p - 1);
    this.load();
  }

  next() {
    if (!this.canNext()) return;
    this.page.update(p => p + 1);
    this.load();
  }

  onPageSizeChange(ps: number) {
    this.pageSize.set(ps);
    this.page.set(1);
    this.load();
  }

  openCreate() {
    this.modalMode.set('create');
    this.editingUserId.set(null);
    this.error.set(null);

    this.userForm.reset({
      name: '',
      email: '',
      password: '',
      confirmPassword: '',
      newPassword: '',
      confirmNewPassword: '',
    });

    this.modalOpen.set(true);
  }

  openEdit(u: UserDto) {
    this.modalMode.set('edit');
    this.editingUserId.set(u.id);
    this.error.set(null);

    this.userForm.reset({
      name: u.name ?? '',
      email: u.email ?? '',
      password: '',
      confirmPassword: '',
      newPassword: '',
      confirmNewPassword: '',
    });

    this.modalOpen.set(true);
  }

  closeModal() {
    this.modalOpen.set(false);
    this.error.set(null);
  }

  submitModal() {
    if (this.userForm.controls.name.invalid || this.userForm.controls.email.invalid) {
      this.userForm.controls.name.markAsTouched();
      this.userForm.controls.email.markAsTouched();
      return;
    }

    if (this.modalMode() === 'create') {
      const pw = (this.userForm.value.password ?? '').trim();
      const cpw = (this.userForm.value.confirmPassword ?? '').trim();

      if (pw.length < 6) {
        this.error.set('Password must be at least 8 characters.');
        return;
      }
      if (pw !== cpw) {
        this.error.set('Password and confirm password do not match.');
        return;
      }

      const dto: RegisterDto = {
        name: this.userForm.value.name!,
        email: this.userForm.value.email!,
        password: pw,
        confirmPassword: cpw,
      };

      this.loading.set(true);
      this.error.set(null);

      this.account
        .createUser(dto)
        .pipe(
          tap(() => {
            this.closeModal();
            this.load();
          }),
          catchError((err) => {
            this.error.set(this.toMsg(err, 'Failed to create user.'));
            return of(null);
          }),
          finalize(() => this.loading.set(false))
        )
        .subscribe();

      return;
    }

    const userId = this.editingUserId();
    if (!userId) return;

    const npw = (this.userForm.value.newPassword ?? '').trim();
    const cnpw = (this.userForm.value.confirmNewPassword ?? '').trim();

    if (npw.length > 0 || cnpw.length > 0) {
      if (npw.length < 6) {
        this.error.set('New password must be at least 8 characters.');
        return;
      }
      if (npw !== cnpw) {
        this.error.set('New password and confirm new password do not match.');
        return;
      }
    }

    const dto: UpdateUserDto = {
      name: this.userForm.value.name!,
      email: this.userForm.value.email!,
      newPassword: npw.length ? npw : null,
      confirmNewPassword: cnpw.length ? cnpw : null,
    };

    this.loading.set(true);
    this.error.set(null);

    this.account
      .updateUser(userId, dto)
      .pipe(
        tap(() => {
          this.closeModal();
          this.load();
        }),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to update user.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  deleteUser(user: UserDto) {
    if (!confirm(`Delete user "${user.name}" (${user.email})?`)) return;

    this.loading.set(true);
    this.error.set(null);

    this.account
      .deleteUser(user.id)
      .pipe(
        tap(() => {
          this.users.update((arr) => arr.filter((u) => u.id !== user.id));
        }),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to delete user.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  private toMsg(err: any, fallback: string) {
    return (
      (Array.isArray(err?.error) && err.error.join('\n')) ||
      err?.error?.message ||
      err?.message ||
      fallback
    );
  }
}
