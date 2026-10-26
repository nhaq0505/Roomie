import type { Invoice } from '../invoices';
import type { Occupancy, Room } from '../rooms';

export interface TenantOverview {
  confirmationMode?: 'Automatic' | 'Manual';
  total: number;
  totals: {outstanding: number; unpaidCount?: number};
  room: Room;
  occupancy: Occupancy;
  invoices: Invoice[];
  currentPeriod?: string;
}
