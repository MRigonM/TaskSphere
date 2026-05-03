export interface ChatMessageDto {
  id: number;
  projectId: number;
  senderId: string;
  senderName: string;
  content: string;
  imageUrl: string | null;
  sentAt: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}