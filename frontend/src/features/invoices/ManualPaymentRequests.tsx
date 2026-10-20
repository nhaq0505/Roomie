import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Form, Modal, Pagination, Skeleton, Typography } from 'antd';
import { useEffect, useState } from 'react';
import { ClockCircleOutlined, ReloadOutlined, CheckOutlined } from '@ant-design/icons';
import { formatVnd } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import { LoadError } from '../../shared/ui/Ui';
import { paymentSettingsApi, type ManualPayment } from '../payments';

export default function ManualPaymentRequests() {
  const client = useQueryClient(); const { message } = App.useApp();
  const [selected, setSelected] = useState<ManualPayment | null>(null);
  const [page, setPage] = useState(1);
  const query = useQuery({ queryKey: ['manual-payments', page], queryFn: () => paymentSettingsApi.manualRequests(page) });
  useEffect(() => {
    if (query.data) setPage(current => Math.min(current, Math.max(1, Math.ceil(query.data.total / 20))));
  }, [query.data]);
  const confirm = useMutation({ mutationFn: () => paymentSettingsApi.confirmManual(selected!.id, selected!.amount), onSuccess: async () => {
    setSelected(null); message.success(t("Đã ghi nhận khoản thu vào các hóa đơn."));
    await Promise.all(['manual-payments', 'invoices', 'invoice', 'dashboard', 'rooms'].map(key => client.invalidateQueries({ queryKey: [key] })));
  }, onError: (error: Error) => message.error(error.message) });
  if (query.isSuccess && query.data.total === 0) return null;
  return <Card className="manual-payment-confirmations" bordered={false}>
    <div className="manual-payment-heading">
      <div className="manual-payment-title"><ClockCircleOutlined aria-hidden /><Typography.Title level={4}>{t("Chuyển khoản chờ xác nhận")}</Typography.Title>{!!query.data?.total && <span className="manual-payment-count">{query.data.total}</span>}</div>
      <Button icon={<ReloadOutlined aria-hidden />} onClick={() => void query.refetch()} loading={query.isFetching}>{t("Làm mới")}</Button>
    </div>
    <Typography.Paragraph type="secondary">{t("Đối chiếu nội dung chuyển khoản, tài khoản và số tiền thực nhận trước khi xác nhận.")}</Typography.Paragraph>
    {query.isPending ? <Skeleton active paragraph={{ rows: 2 }} /> : query.isError ? <LoadError error={query.error} onRetry={() => void query.refetch()} /> : !(query.data?.items ?? []).length ? <Typography.Paragraph>{t("Chưa có yêu cầu chuyển khoản.")}</Typography.Paragraph> : <div className="manual-payment-requests">{query.data!.items.map(item => <div className="manual-payment-request" key={item.id}>
      <div><strong>{item.description}</strong>{item.roomName && <div>{item.roomName} · {item.tenantName}</div>}<div className="manual-payment-amount">{formatVnd(item.amount)} <span>· {t("{0} hóa đơn", [item.invoices.length])}</span></div><small>{item.accountNumber} · {item.accountName}</small></div>
      <Button type="primary" icon={<CheckOutlined aria-hidden />} onClick={() => { setSelected(item); }}>{t("Xác nhận đã nhận tiền")}</Button>
    </div>)}</div>}
    <Pagination current={page} total={query.data?.total ?? 0} pageSize={20} onChange={setPage} showSizeChanger={false} hideOnSinglePage />
    <Modal open={!!selected} title={t("Xác nhận đã nhận tiền")} onCancel={() => { if (!confirm.isPending) setSelected(null); }} footer={null}>
      <Typography.Paragraph>{selected?.description} · {formatVnd(selected?.amount ?? 0)}</Typography.Paragraph>
      <Typography.Paragraph>{t("Xác nhận này thanh toán toàn bộ số tiền còn lại của các hóa đơn đã chọn.")}</Typography.Paragraph>
      <Alert type="warning" showIcon message={t("Chỉ xác nhận sau khi đã kiểm tra tiền thực nhận trong tài khoản ngân hàng.")} />
      <Form layout="vertical" onFinish={() => confirm.mutate()} style={{ marginTop: 16 }}>
        <Button type="primary" htmlType="submit" disabled={!selected} loading={confirm.isPending}>{t("Ghi nhận khoản thu")}</Button>
      </Form>
    </Modal>
  </Card>;
}
