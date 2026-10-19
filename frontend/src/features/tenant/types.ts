import type { Invoice } from '../invoices';
import type { Occupancy, Room } from '../rooms';
export interface TenantOverview {
  total: number;
  totals: {outstanding: number};
  room: Room;
  occupancy: Occupancy;
  invoices: Invoice[];
}
