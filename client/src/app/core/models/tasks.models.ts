export interface CreateTaskDto {
  title: string;
  description?: string | null;
  status: string;
  priority?: string | null;
  storyPoints?: number | null;
  projectId: number;
  sprintId?: number | null;
  assigneeUserId?: string | null;
}

export interface UpdateTaskDto {
  title: string;
  description?: string | null;
  status: string;
  priority?: string | null;
  storyPoints?: number | null;
  assigneeUserId?: string | null;
  sprintId?: number | null;
}

export interface TaskDto {
  id: number;
  title: string;
  description?: string | null;
  status: string;
  priority?: string | null;
  storyPoints?: number | null;
  projectId?: number | null;
  sprintId?: number | null;
  assigneeUserId?: string | null;
  createdByUserId?: string | null;
  createdAtUtc: string;
}
