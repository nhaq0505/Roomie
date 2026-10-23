import { post, request } from '../../shared/http/client';
import type {
  Expense, ExpenseAttachment, ExpenseAttachmentUsage, ExpenseFilters, ExpenseListPage, ExpenseRequest,
  RecurringExpense, RecurringExpenseRequest,
} from './types';

const id = (value: string) => encodeURIComponent(value);
const withKey = (key: string): HeadersInit => ({ 'Idempotency-Key': key });

function expenseQuery(filters: ExpenseFilters) {
  const query = new URLSearchParams();
  query.set('dateFrom', filters.dateFrom);
  query.set('dateTo', filters.dateTo);
  if (filters.buildingId) query.set('buildingId', filters.buildingId);
  if (filters.roomId) query.set('roomId', filters.roomId);
  if (filters.category) query.set('category', filters.category);
  query.set('page', String(filters.page));
  query.set('pageSize', String(filters.pageSize));
  return query.toString();
}

export const expensesApi = {
  list: (filters: ExpenseFilters) => request<ExpenseListPage>(`/api/expenses?${expenseQuery(filters)}`),
  create: (body: ExpenseRequest, idempotencyKey: string) => request<Expense>('/api/expenses', { ...post(body), headers: withKey(idempotencyKey) }),
  update: (expenseId: string, body: ExpenseRequest) => request<Expense>(`/api/expenses/${id(expenseId)}`, { method: 'PUT', body: JSON.stringify(body) }),
  remove: (expenseId: string, expectedVersion: number) =>
    request<{ deleted: boolean }>(`/api/expenses/${id(expenseId)}?expectedVersion=${expectedVersion}`, { method: 'DELETE' }),
};

export const attachmentsApi = {
  list: (expenseId: string) => request<ExpenseAttachment[]>(`/api/expenses/${id(expenseId)}/attachments`),
  upload: (expenseId: string, file: Blob, fileName: string) => {
    const form = new FormData();
    form.append('file', file, fileName);
    return request<ExpenseAttachment>(`/api/expenses/${id(expenseId)}/attachments`, { method: 'POST', body: form });
  },
  remove: (expenseId: string, attachmentId: string) =>
    request<{ deleted: boolean }>(`/api/expenses/${id(expenseId)}/attachments/${id(attachmentId)}`, { method: 'DELETE' }),
  usage: () => request<ExpenseAttachmentUsage>('/api/expenses/attachments/usage'),
  url: (expenseId: string, attachmentId: string, download = false) =>
    `/api/expenses/${id(expenseId)}/attachments/${id(attachmentId)}${download ? '?download=true' : ''}`,
};

export const recurringExpensesApi = {
  list: () => request<RecurringExpense[]>('/api/recurring-expenses'),
  create: (body: RecurringExpenseRequest, idempotencyKey: string) =>
    request<RecurringExpense>('/api/recurring-expenses', { ...post(body), headers: withKey(idempotencyKey) }),
  update: (templateId: string, body: RecurringExpenseRequest) =>
    request<RecurringExpense>(`/api/recurring-expenses/${id(templateId)}`, { method: 'PUT', body: JSON.stringify(body) }),
  pause: (templateId: string, expectedVersion: number) =>
    request<RecurringExpense>(`/api/recurring-expenses/${id(templateId)}/pause`, post({ expectedVersion })),
  resume: (templateId: string, expectedVersion: number) =>
    request<RecurringExpense>(`/api/recurring-expenses/${id(templateId)}/resume`, post({ expectedVersion })),
  remove: (templateId: string, expectedVersion: number) =>
    request<{ deleted: boolean }>(`/api/recurring-expenses/${id(templateId)}?expectedVersion=${expectedVersion}`, { method: 'DELETE' }),
};
