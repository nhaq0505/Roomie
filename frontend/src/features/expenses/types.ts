export type ExpenseCategory = 'Electricity' | 'Water' | 'Internet' | 'Repair' | 'Cleaning' | 'Salary' | 'LeaseRent' | 'Tax' | 'Other';

export interface Expense {
  id: string;
  expenseDate: string;
  category: ExpenseCategory;
  amount: number;
  description: string | null;
  buildingId: string | null;
  buildingName: string | null;
  roomId: string | null;
  roomName: string | null;
  utilityPeriod: string | null;
  utilityKwh: number | null;
  recurringExpenseId: string | null;
  recurrencePeriod: string | null;
  attachmentCount: number;
  version: number;
  createdAt: string;
  updatedAt: string;
}

export interface ExpenseRequest {
  expenseDate: string;
  category: ExpenseCategory;
  amount: number;
  description?: string | null;
  buildingId?: string | null;
  roomId?: string | null;
  utilityPeriod?: string | null;
  utilityKwh?: number | null;
  expectedVersion?: number;
}

export interface ExpenseCategoryTotal { category: ExpenseCategory; amount: number; count: number; }

export interface ExpenseListPage {
  items: Expense[];
  page: number;
  pageSize: number;
  totalCount: number;
  totals: { total: number; count: number; byCategory: ExpenseCategoryTotal[] };
}

export interface ExpenseFilters {
  dateFrom: string;
  dateTo: string;
  buildingId?: string;
  roomId?: string;
  category?: ExpenseCategory;
  page: number;
  pageSize: number;
}

export type RecurringSkipReason = 'ProRequired' | 'LocationArchived' | 'Ended' | 'Paused' | 'NotStarted';

export interface RecurringExpense {
  id: string;
  category: ExpenseCategory;
  amount: number;
  description: string | null;
  buildingId: string | null;
  buildingName: string | null;
  roomId: string | null;
  roomName: string | null;
  dayOfMonth: number;
  startPeriod: string;
  endPeriod: string | null;
  isPaused: boolean;
  skipReason: RecurringSkipReason | null;
  lastGeneratedPeriod: string | null;
  version: number;
  createdAt: string;
}

export interface RecurringExpenseRequest {
  category: ExpenseCategory;
  amount: number;
  description?: string | null;
  buildingId?: string | null;
  roomId?: string | null;
  dayOfMonth: number;
  startPeriod: string;
  endPeriod?: string | null;
  expectedVersion?: number;
}

export interface ExpenseAttachment { id: string; expenseId: string; fileName: string; contentType: string; sizeBytes: number; createdAt: string; }

export interface ExpenseAttachmentUsage { usedBytes: number; limitBytes: number; maxFileBytes: number; maxFilesPerExpense: number; }
