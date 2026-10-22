import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { App } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { t } from '../../shared/i18n/runtime';
import { useAuth } from '../auth/session';
import { type TenantCheckout } from '../payments/index';
import { subscriptionApi } from './index';


export function useSubscriptionDashboard() {
  const { message } = App.useApp();
  const { refresh: refreshUser } = useAuth();
  const client = useQueryClient();
  const [selectedPackageId, setSelectedPackageId] = useState<string>('pro_1m');
  const [checkout, setCheckout] = useState<TenantCheckout | null>(null);
  const [paymentSuccess, setPaymentSuccess] = useState(false);
  const pollingRef = useRef<number | null>(null);

  const subQuery = useQuery({
    queryKey: ['subscription'],
    queryFn: subscriptionApi.getStatus,
  });

  const upgradeMutation = useMutation({
    mutationFn: (packageId: string) => subscriptionApi.upgrade(packageId),
    onSuccess: (data) => {
      setCheckout(data);
      setPaymentSuccess(false);
    },
    onError: (err: Error) => message.error(err.message),
  });

  const reconcileMutation = useMutation({
    mutationFn: (vars: number | { orderCode: number; manual?: boolean }) => {
      const orderCode = typeof vars === 'number' ? vars : vars.orderCode;
      return subscriptionApi.reconcile(orderCode);
    },
    onSuccess: async (data, vars) => {
      const isManual = typeof vars === 'object' && vars !== null && 'manual' in vars ? vars.manual : false;
      if (data.status === 'Paid' || data.isPro) {
        setPaymentSuccess(true);
        message.success(t("Nâng cấp gói Pro thành công!"));
        await Promise.all([
          subQuery.refetch(),
          refreshUser(),
          client.invalidateQueries({ queryKey: ['rooms'] }),
          client.invalidateQueries({ queryKey: ['dashboard'] }),
        ]);
        if (pollingRef.current) {
          window.clearInterval(pollingRef.current);
          pollingRef.current = null;
        }
      } else if (isManual) {
        message.info(t("Chưa ghi nhận thanh toán. Hệ thống vẫn đang tự động kiểm tra..."));
      }
    },
    onError: (err: Error, vars) => {
      const isManual = typeof vars === 'object' && vars !== null && 'manual' in vars ? vars.manual : true;
      if (isManual) message.error(err.message);
    },
  });

  const redeemMutation = useMutation({
    mutationFn: (code: string) => subscriptionApi.redeemCode(code),
    onSuccess: async (data) => {
      message.success(t("Kích hoạt thành công! Bạn nhận được {0} tháng sử dụng gói Pro.", [data.addedMonths]));
      await Promise.all([
        subQuery.refetch(),
        refreshUser(),
        client.invalidateQueries({ queryKey: ['rooms'] }),
        client.invalidateQueries({ queryKey: ['dashboard'] }),
      ]);
    },
    onError: (err: Error) => message.error(err.message),
  });

  // Setup auto-reconciliation polling when modal is open
  useEffect(() => {
    if (checkout && checkout.orderCode && !paymentSuccess) {
      pollingRef.current = window.setInterval(() => {
        reconcileMutation.mutate({ orderCode: checkout.orderCode, manual: false });
      }, 5000);
    }
    return () => {
      if (pollingRef.current) {
        window.clearInterval(pollingRef.current);
        pollingRef.current = null;
      }
    };
  }, [checkout?.orderCode, paymentSuccess]);

  const copyToClipboard = async (text: string, msg: string) => {
    try {
      await navigator.clipboard.writeText(text);
      message.success(msg);
    } catch {
      message.info(text);
    }
  };

  const status = subQuery.data;
  const isPro = status?.isPro ?? false;
  const currentRooms = status?.roomCount ?? status?.currentRooms ?? 0;
  const maxRooms = status?.maxRooms ?? 5;
  const isLimitReached = !isPro && currentRooms >= maxRooms;

  return { message, selectedPackageId, setSelectedPackageId, checkout, setCheckout, paymentSuccess, setPaymentSuccess, upgradeMutation, reconcileMutation, redeemMutation, copyToClipboard, status, isPro, currentRooms, maxRooms, isLimitReached, subQuery };
}
