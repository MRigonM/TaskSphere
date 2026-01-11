export interface CreateSprintDto {
  name: string;
  startDate: string;
  endDate: string;
  isActive: boolean;
  projectId: number;
}

export interface UpdateSprintDto {
  name: string;
  startDate: string;
  endDate: string;
}

export interface SprintDto {
  id: number;
  name: string;
  startDate: string;
  endDate: string;
  isActive: boolean;
  projectId: number | null;
}

export interface TaskEntityDto {
  id: number;
  title?: string | null;
  name?: string | null;
  description?: string | null;
  status?: string | null;
  assigneeUserId?: string | null;
  priority?: string | null;
  storyPoints?: number | null;
  projectId?: number | null;
  sprintId?: number | null;
}


export interface SprintBoardDto {
  sprintId: number;
  sprintName: string;
  projectId: number | null;
  low: TaskEntityDto[];
  medium: TaskEntityDto[];
  high: TaskEntityDto[];
  critical: TaskEntityDto[];
  open: TaskEntityDto[];
  inProgress: TaskEntityDto[];
  blocked: TaskEntityDto[];
  done: TaskEntityDto[];
}
