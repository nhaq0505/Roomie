
export interface DashboardSummary {
  totalRooms: number;
  occupiedRooms: number;
  totalBilled: number;
  totalPaid: number;
  totalOutstanding: number;
  pendingReadings: number;
  period: string;
  monthlyBilled: number;
  monthlyPaid: number;
  monthlyOutstanding: number;
  collectionRate: number;
}
