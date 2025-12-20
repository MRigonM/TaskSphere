export interface RegisterDto {
  name: string;
  email: string;
  password: string;
  confirmPassword: string;
}

export interface LoginDto {
  email: string;
  password: string;
}

export interface AuthResponseDto {
  token: string;
  name: string;
  userId: string;
  email: string;
  role: string;
  companyId?: string | null;
}

export interface UserDto {
  id: string;
  name: string;
  email: string;
  companyId?: string | null;
  isDeleted: boolean;
}

export interface UpdateUserDto {
  name: string;
  email: string;
  newPassword?: string | null;
  confirmNewPassword?: string | null;
}

export interface UserQueryDto {
  name?: string | null;
  email?: string | null;
  page?: number;
  pageSize?: number;
}
