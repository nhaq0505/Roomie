import { listFrom, post, request } from '../../shared/http/client';
import type { PaymentReconcileResult } from '../payments';
import type { Invoice, InvoiceCorrection, ReceiptResult } from './types';

export const invoicesApi = {
    page: (filters: Record<string, string>) => request<{items: Invoice[]; total: number; totals: {billed:number; paid:number; outstanding:number}; draftCount:number}>(`/api/invoices?${new URLSearchParams(filters)}`),
    createSupplemental: (roomId: string, body: { requestId: string; occupancyId: string; description: string; amount: number; dueDate: string }) => request<Invoice>(`/api/rooms/${encodeURIComponent(roomId)}/supplemental-invoices`, post(body)),
    settleDeposit: (id: string, body: { amount: number; kind: 'Refund' | 'Deduction'; reason: string; idempotencyKey: string; expectedVersion?: number; targetInvoiceId?: string; targetExpectedVersion?: number }) => request<Invoice>(`/api/invoices/${encodeURIComponent(id)}/deposit-settlements`, post(body)),
    // Secondary screens need the whole scoped period/occupancy. Fetch bounded server pages,
    // while the main invoice screen remains truly paginated.
    list: async (period?: string, occupancyId?: string): Promise<Invoice[]> => {
      const query = new URLSearchParams({page:'1',pageSize:'100'});
      if (period) query.set('period',period);
      if (occupancyId) query.set('occupancyId',occupancyId);
      const items = new Map<string,Invoice>();
      for (let page=1; ; page++) {
        query.set('page',String(page));
        const result=await request<{items:Invoice[];total:number}>(`/api/invoices?${query}`);
        result.items.forEach(item=>items.set(item.id,item));
        if (page*100>=result.total || !result.items.length) return [...items.values()];
      }
    },
    publishDraft: (id: string, body: { expectedVersion: number; requestId: string; rent: number; electricityAmount: number; waterAmount: number; serviceAmount: number }) => request<Invoice>(`/api/invoices/${encodeURIComponent(id)}/publish`, post(body)),
    correct: (id: string, body: { expectedVersion: number; requestId: string; reason: string; amount?: number; rent?: number; electricityAmount?: number; waterAmount?: number; serviceAmount?: number; currentReading?: number; dueDate?: string; description?: string }) => request<Invoice>(`/api/invoices/${encodeURIComponent(id)}/corrections`, post(body)),
    corrections: (id: string) => request<unknown>(`/api/invoices/${encodeURIComponent(id)}/corrections`).then(listFrom<InvoiceCorrection>),
    generate: (period: string) => request<unknown>('/api/invoices/generate', post({ period })),
    get: (id: string) => request<Invoice>(`/api/invoices/${encodeURIComponent(id)}`),
    reconcile: (id: string) => request<PaymentReconcileResult>(`/api/invoices/${encodeURIComponent(id)}/reconcile`, post()),
    addReceipt: (id: string, body: { amount: number; note: string; idempotencyKey: string; expectedVersion?: number }) => request<ReceiptResult>(`/api/invoices/${encodeURIComponent(id)}/receipts`, post(body)),
  };
