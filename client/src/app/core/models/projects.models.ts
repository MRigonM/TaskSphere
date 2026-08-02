export interface CreateProjectDto {
  name: string;
  key: string;
}

export interface ProjectDto {
  id: number;
  name: string;
  key: string;
}

export interface AddMemberDto {
  userId: string;
}

export interface MemberDto {
  id: number;
  projectId: number;
  userId: string;
  userName: string;
  email: string;
}
