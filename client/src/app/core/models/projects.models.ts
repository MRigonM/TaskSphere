export interface CreateProjectDto {
  name: string;
}

export interface ProjectDto {
  id: number;
  name: string;
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
