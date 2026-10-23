import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { App } from 'antd';
import { useMemo, useRef, useState } from 'react';
import { currentPeriod } from '../../shared/format';
import { ApiError } from '../../shared/http/client';
import { t } from '../../shared/i18n/runtime';
import { buildingsApi } from '../buildings/index';
import { roomsApi } from '../rooms/index';
import { subscriptionApi } from '../subscription/index';
import { expensesApi } from './api';
import { monthRange } from './categories';
import type { Expense, ExpenseCategory, ExpenseFilters, ExpenseRequest } from './types';

export const expensePageSize = 50;

export type ExpenseEditor = { mode: 'create'; key: string } | { mode: 'edit'; expense: Expense } | null;

export function useExpensesPage() {
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const defaultRange = useMemo(() => monthRange(currentPeriod()), []);
  const [dateFrom, setDateFrom] = useState<string>(defaultRange[0]);
  const [dateTo, setDateTo] = useState<string>(defaultRange[1]);
  const [buildingId, setBuildingId] = useState<string>();
  const [roomId, setRoomId] = useState<string>();
  const [category, setCategory] = useState<ExpenseCategory>();
  const [page, setPage] = useState(1);
  const [editor, setEditor] = useState<ExpenseEditor>(null);
  const [attachmentsFor, setAttachmentsFor] = useState<Expense | null>(null);
  // The create key survives retries of the same form so a lost response cannot record the expense twice.
  const createKey = useRef<string | null>(null);

  const filters: ExpenseFilters = { dateFrom, dateTo, buildingId, roomId, category, page, pageSize: expensePageSize };
  const buildingsQuery = useQuery({ queryKey: ['buildings', 'expenses', 'all'], queryFn: () => buildingsApi.list(true) });
  const roomsQuery = useQuery({ queryKey: ['rooms', 'expenses', 'all'], queryFn: () => roomsApi.list(undefined, true) });
  const subscriptionQuery = useQuery({ queryKey: ['subscription'], queryFn: subscriptionApi.getStatus });
  const expensesQuery = useQuery({
    queryKey: ['expenses', 'list', dateFrom, dateTo, buildingId, roomId, category, page],
    queryFn: () => expensesApi.list(filters),
    enabled: dateFrom <= dateTo,
  });
  const isPro = subscriptionQuery.data?.isPro === true;

  const refreshAfterConflict = async (error: Error) => {
    if (error instanceof ApiError && error.status === 409) {
      await queryClient.invalidateQueries({ queryKey: ['expenses'] });
      message.warning(t("Khoản chi đã thay đổi ở nơi khác. Danh sách đã được tải lại."));
      return true;
    }
    return false;
  };
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['expenses'] }),
    queryClient.invalidateQueries({ queryKey: ['reports'] }),
  ]);

  const saveMutation = useMutation({
    mutationFn: async (values: ExpenseRequest) => {
      if (editor?.mode === 'edit') return expensesApi.update(editor.expense.id, { ...values, expectedVersion: editor.expense.version });
      if (!createKey.current) createKey.current = crypto.randomUUID();
      return expensesApi.create(values, createKey.current);
    },
    onSuccess: async () => {
      const wasEdit = editor?.mode === 'edit';
      createKey.current = null;
      setEditor(null);
      await invalidate();
      message.success(wasEdit ? t("Đã cập nhật khoản chi.") : t("Đã ghi khoản chi."));
    },
    onError: async (error: Error) => {
      if (editor?.mode === 'edit' && await refreshAfterConflict(error)) { setEditor(null); return; }
      message.error(error.message);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (expense: Expense) => expensesApi.remove(expense.id, expense.version),
    onSuccess: async () => { await invalidate(); message.success(t("Đã xóa khoản chi.")); },
    onError: async (error: Error) => { if (!await refreshAfterConflict(error)) message.error(error.message); },
  });

  const openCreate = () => { createKey.current = crypto.randomUUID(); setEditor({ mode: 'create', key: createKey.current }); };
  const openEdit = (expense: Expense) => setEditor({ mode: 'edit', expense });
  const closeEditor = () => { if (saveMutation.isPending) return; createKey.current = null; setEditor(null); };
  const setRange = (from: string, to: string) => { setDateFrom(from); setDateTo(to); setPage(1); };
  const clearFilters = () => { setDateFrom(defaultRange[0]); setDateTo(defaultRange[1]); setBuildingId(undefined); setRoomId(undefined); setCategory(undefined); setPage(1); };

  return {
    filters, buildingsQuery, roomsQuery, expensesQuery, subscriptionQuery, isPro,
    setRange, setBuildingId: (value?: string) => { setBuildingId(value); setRoomId(undefined); setPage(1); },
    setRoomId: (value?: string) => { setRoomId(value); setPage(1); },
    setCategory: (value?: ExpenseCategory) => { setCategory(value); setPage(1); },
    setPage, clearFilters,
    editor, openCreate, openEdit, closeEditor, saveMutation, deleteMutation,
    attachmentsFor, setAttachmentsFor,
  };
}
