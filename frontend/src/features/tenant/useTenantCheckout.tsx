import { useMutation, useQuery } from '@tanstack/react-query';
import { App } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { t } from '../../shared/i18n/runtime';
import { type CheckoutSession, type CheckoutStatus, checkoutStatus, httpStatus, invoiceIsPaid, isAccessError } from './checkout';
import { tenantApi } from './index';


export function useTenantCheckout(token: string, invoiceId: string) {
  const { message } = App.useApp();
  const navigate = useNavigate();
  const location = useLocation();
  const [checkout, setCheckout] = useState<CheckoutSession | null>(null);
  const [createError, setCreateError] = useState('');
  const [checkoutError, setCheckoutError] = useState('');
  const [tenantAccessError, setTenantAccessError] = useState<{ status: number; message: string } | null>(null);
  const [tabVisible, setTabVisible] = useState(() => typeof document === 'undefined' || document.visibilityState === 'visible');
  const [clockNow, setClockNow] = useState(() => Date.now());
  const [reconcileDelayMs, setReconcileDelayMs] = useState(20_000);
  const [overviewDelayMs, setOverviewDelayMs] = useState(5000);
  const [autoReconcilePaused, setAutoReconcilePaused] = useState(false);
  const [overviewPollingPaused, setOverviewPollingPaused] = useState(false);
  const checkoutRef = useRef<CheckoutSession | null>(null);
  const requestGeneration = useRef(0);
  const mounted = useRef(false);
  const autoCreateStarted = useRef(false);
  const reconcileFailures = useRef(0);
  const overviewFailures = useRef(0);

  function setActiveCheckout(next: CheckoutSession | null) {
    checkoutRef.current = next;
    setCheckout(next);
  }
  function closeForAccessError(error: Error) {
    const status = httpStatus(error);
    if (![401, 403, 404].includes(status)) return false;
    requestGeneration.current += 1;
    setActiveCheckout(null);
    setCheckoutError('');
    setCreateError('');
    setTenantAccessError({ status, message: error.message });
    return true;
  }

  const query = useQuery({
    queryKey: ['tenant', token, 'invoice', invoiceId],
    queryFn: () => tenantApi.overview(token, 1, invoiceId === 'all' ? undefined : invoiceId),
    enabled: !!token,
    retry: false,
    refetchOnMount: 'always',
    refetchInterval: (current) => {
      const overview = current.state.data;
      const manualPending = overview?.confirmationMode === 'Manual' && (invoiceId === 'all' ? overview.totals.outstanding > 0 : overview.invoices.some(item => item.id === invoiceId && item.outstanding > 0));
      return tabVisible && !overviewPollingPaused && (manualPending || checkout?.status === 'pending') ? overviewDelayMs : false;
    },
    refetchIntervalInBackground: false,
  });
  const first = query.data?.invoices[0];
  const invoice = invoiceId === 'all' && first ? { ...first, id: 'all', paid: 0,
    total: query.data!.totals.outstanding, outstanding: query.data!.totals.outstanding,
    status: query.data!.totals.outstanding > 0 ? 'Unpaid' as const : 'Paid' as const } : query.data?.invoices.find((item) => item.id === invoiceId);

  useEffect(() => {
    const onVisibilityChange = () => setTabVisible(document.visibilityState === 'visible');
    document.addEventListener('visibilitychange', onVisibilityChange);
    return () => document.removeEventListener('visibilitychange', onVisibilityChange);
  }, []);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      checkoutRef.current = null;
    };
  }, []);
  useEffect(() => {
    if (!checkout) return;
    if (invoiceIsPaid(invoice) && checkout.status !== 'paid') {
      setActiveCheckout({ ...checkout, status: 'paid', verificationPending: false });
    }
  }, [checkout, invoice]);
  useEffect(() => {
    if (!checkout || checkout.status !== 'pending' || checkout.expiresAt === null) return;
    const remaining = checkout.expiresAt * 1000 - Date.now();
    if (remaining <= 0) return;
    const timer = window.setTimeout(() => setClockNow(Date.now()), remaining);
    return () => window.clearTimeout(timer);
  }, [checkout?.generation, checkout?.expiresAt, checkout?.status]);
  useEffect(() => {
    if (!query.isError || !query.errorUpdatedAt) return;
    const status = httpStatus(query.error);
    if ([401, 403, 404].includes(status)) {
      requestGeneration.current += 1;
      setActiveCheckout(null);
      setCheckoutError('');
      setCreateError('');
      setTenantAccessError({ status, message: query.error.message });
      return;
    }
    if (!checkout && query.data?.confirmationMode !== 'Manual') return;
    overviewFailures.current += 1;
    setOverviewDelayMs(status === 429 ? 60_000 : Math.min(60_000, 5000 * (2 ** Math.min(overviewFailures.current, 4))));
    if (overviewFailures.current >= 4) setOverviewPollingPaused(true);
  }, [query.error, query.errorUpdatedAt, query.isError, checkout?.generation, query.data?.confirmationMode]);
  useEffect(() => {
    if (!query.isSuccess) return;
    overviewFailures.current = 0;
    setOverviewDelayMs(5000);
    setOverviewPollingPaused(false);
  }, [query.dataUpdatedAt, query.isSuccess]);

  const paymentMutation = useMutation({
    mutationFn: (_variables: { generation: number }) => invoiceId === 'all' ? tenantApi.createAllPayment(token) : tenantApi.createPayment(token, invoiceId),
    onSuccess: async (result, variables) => {
      if (!mounted.current || requestGeneration.current !== variables.generation || !invoiceId) return;
      const generation = variables.generation;
      const status = checkoutStatus(result.status);
      const session: CheckoutSession = { ...result, invoiceId, generation, status: status === 'paid' ? 'pending' : status, verificationPending: status === 'paid' };
      reconcileFailures.current = 0;
      setReconcileDelayMs(20_000);
      setAutoReconcilePaused(false);
      setCreateError('');
      setCheckoutError('');
      setClockNow(Date.now());
      setActiveCheckout(session);
      await query.refetch();
    },
    onError: (error: Error, variables) => {
      if (!mounted.current || requestGeneration.current !== variables.generation) return;
      if (closeForAccessError(error)) return;
      setCreateError(error.message);
    },
  });
  const reconcileMutation = useMutation({
    mutationFn: async (_variables: { generation: number }) => invoiceId === 'all' ? { ...await tenantApi.reconcileOrder(token, checkoutRef.current!.orderCode), invoice: null } : await tenantApi.reconcile(token, invoiceId),
    onSuccess: async (result, variables) => {
      const current = checkoutRef.current;
      if (!mounted.current || !current || current.invoiceId !== invoiceId || current.generation !== variables.generation || requestGeneration.current !== variables.generation) return;
      const status = checkoutStatus(result.status);
      const paidByInvoice = invoiceId === 'all' ? status === 'paid' : result.invoice?.id === invoiceId && invoiceIsPaid(result.invoice);
      const nextStatus: CheckoutStatus = paidByInvoice ? 'paid' : status === 'paid' ? 'pending' : status;
      setActiveCheckout({ ...current, status: nextStatus, verificationPending: !paidByInvoice && status === 'paid' });
      setCheckoutError('');
      reconcileFailures.current = 0;
      setReconcileDelayMs(20_000);
      setAutoReconcilePaused(false);
      await query.refetch();
    },
    onError: (error: Error, variables) => {
      const current = checkoutRef.current;
      if (!mounted.current || !current || current.invoiceId !== invoiceId || current.generation !== variables.generation || requestGeneration.current !== variables.generation) return;
      if (closeForAccessError(error)) return;
      reconcileFailures.current += 1;
      const status = httpStatus(error);
      setReconcileDelayMs(status === 429 ? 60_000 : Math.min(120_000, 20_000 * (2 ** Math.min(reconcileFailures.current, 3))));
      if (reconcileFailures.current >= 4) setAutoReconcilePaused(true);
      setCheckoutError(t("Không thể kiểm tra trạng thái lúc này. Bạn có thể thử lại sau ít phút."));
    },
  });

  function beginCreate() {
    if (paymentMutation.isPending || query.data?.confirmationMode === 'Manual') return;
    const generation = ++requestGeneration.current;
    setActiveCheckout(null);
    setCreateError('');
    setCheckoutError('');
    paymentMutation.mutate({ generation });
  }
  useEffect(() => {
    if (query.data?.confirmationMode === 'Manual') return;
    if (!query.isSuccess || !query.data || query.isFetching || tenantAccessError || isAccessError(query.error) || autoCreateStarted.current || !invoice) return;
    autoCreateStarted.current = true;
    if (!invoiceIsPaid(invoice) && invoice.outstanding > 0) beginCreate();
  }, [query.isSuccess, query.dataUpdatedAt, query.isFetching, invoice?.id, invoice?.outstanding, invoice?.status, tenantAccessError]);
  useEffect(() => {
    if (!checkout || checkout.status !== 'pending' || !tabVisible || autoReconcilePaused || reconcileMutation.isPending) return;
    const timer = window.setInterval(() => {
      const current = checkoutRef.current;
      if (!current || current.status !== 'pending' || current.generation !== requestGeneration.current || document.visibilityState !== 'visible' || reconcileMutation.isPending) return;
      reconcileMutation.mutate({ generation: current.generation });
    }, reconcileDelayMs);
    return () => window.clearInterval(timer);
  }, [checkout, tabVisible, reconcileDelayMs, autoReconcilePaused, reconcileMutation.isPending]);

  function checkNow() {
    if (!checkout || checkout.status !== 'pending' || reconcileMutation.isPending) return;
    setAutoReconcilePaused(false);
    setCheckoutError('');
    reconcileMutation.mutate({ generation: checkout.generation });
  }
  async function copyText(text: string, successMessage?: string) {
    try {
      if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
      await navigator.clipboard.writeText(text);
      message.success(successMessage || t("Đã sao chép nội dung chuyển khoản."));
    } catch {
      message.info(t("Không sao chép được. Hãy chọn nội dung để sao chép."));
    }
  }

  return { message, navigate, location, checkout, createError, checkoutError, tenantAccessError, clockNow, query, invoice, paymentMutation, reconcileMutation, beginCreate, checkNow, copyText };
}
