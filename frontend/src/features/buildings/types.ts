
export interface Building {
  id: string;
  name: string;
  address: string | null;
  roomCount?: number;
  isArchived?: boolean;
  archivedAt?: string | null;
  archiveBatchId?: string | null;
}
