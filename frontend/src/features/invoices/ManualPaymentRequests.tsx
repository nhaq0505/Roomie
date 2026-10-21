import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Form, Modal, Pagination, Popconfirm, Skeleton, Typography } from 'antd';
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
  const refresh = () => Promise.all(['manual-payments', 'invoices', 'invoice', 'dashboard', 'rooms', 'reports', 'tenant'].map(key => client.invalidateQueries({ queryKey: [key] })));
  useEffect(() => {
    if (selected && query.data) setSelected(query.data.items.find(item => item.id === selected.id) ?? null);
  }, [query.data, selected]);
  const confirm = useMutation({ mutationFn: (payment: ManualPayment) => paymentSettingsApi.confirmManual(payment.id, payment.amount), onSuccess: async () => {
    setSelected(null); message.success(t("Đã ghi nhận khoản thu vào các hóa đơn."));
    await refresh();
  }, onError: async () => { await refresh(); } });
  const dismiss = useMutation({ mutationFn: (id: string) => paymentSettingsApi.dismissManual(id), onSuccess: async () => {
    setSelected(null); await refresh(); message.success(t("Đã bỏ yêu cầu khỏi danh sách. Không ghi thêm khoản thu."));
  }, onError: async (error: Error) => { await refresh(); message.error(error.message); } });
  if (query.isSuccess && query.data.total === 0) return null;
  return <Card className="manual-payment-confirmations" bordered={false}>
    <div className="manual-payment-heading">
      <div className="manual-payment-title"><ClockCircleOutlined aria-hidden /><Typography.Title level={4}>{t("Yêu cầu chuyển khoản")}</Typography.Title>{!!query.data?.total && <span className="manual-payment-count">{query.data.total}</span>}</div>
      <Button icon={<ReloadOutlined aria-hidden />} onClick={() => void query.refetch()} loading={query.isFetching}>{t("Làm mới")}</Button>
    </div>
    <Typography.Paragraph type="secondary">{t("Danh sách mã QR đã tạo, không phải bằng chứng chuyển tiền. Đối chiếu tài khoản và số tiền thực nhận trước khi xác nhận.")}</Typography.Paragraph>
    {query.isPending ? <Skeleton active paragraph={{ rows: 2 }} /> : query.isError ? <LoadError error={query.error} onRetry={() => void query.refetch()} /> : !(query.data?.items ?? []).length ? <Typography.Paragraph>{t("Chưa có yêu cầu chuyển khoản.")}</Typography.Paragraph> : <div className="manual-payment-requests">{query.data!.items.map(item => <div className="manual-payment-request" key={item.id}>
      <div><strong>{item.description}</strong>{item.roomName && <div>{item.roomName} · {item.tenantName}</div>}<div className="manual-payment-amount">{formatVnd(item.amount)} <span>· {t("{0} hóa đơn", [item.invoices.length])}</span></div><small>{item.accountNumber} · {item.accountName}</small></div>
      <div>{item.statusMessage && <Typography.Paragraph type="warning">{t(item.statusMessage)}</Typography.Paragraph>}
      {item.canConfirm !== false ? <Button type="primary" icon={<CheckOutlined aria-hidden />} onClick={() => { confirm.reset(); setSelected(item); }}>{t("Xác nhận đã nhận tiền")}</Button> : item.canDismiss ?
        <Popconfirm title={t("Bỏ yêu cầu này khỏi danh sách?")} description={t("Chỉ bỏ yêu cầu đã đối chiếu; không ghi thêm khoản thu.")} onConfirm={() => dismiss.mutateAsync(item.id)} okText={t("Bỏ khỏi danh sách")} cancelText={t("Hủy")}>
          <Button loading={dismiss.isPending && dismiss.variables === item.id}>{t("Bỏ khỏi danh sách")}</Button>
        </Popconfirm> : null}</div>
    </div>)}</div>}
    <Pagination current={page} total={query.data?.total ?? 0} pageSize={20} onChange={setPage} showSizeChanger={false} hideOnSinglePage />
    <Modal open={!!selected} title={t("Xác nhận đã nhận tiền")} onCancel={() => { if (!confirm.isPending) setSelected(null); }} footer={null}>
      <Typography.Paragraph>{selected?.description} · {formatVnd(selected?.amount ?? 0)}</Typography.Paragraph>
      <Typography.Paragraph>{t("Xác nhận này thanh toán toàn bộ số tiền còn lại của các hóa đơn đã chọn.")}</Typography.Paragraph>
      <Alert type="warning" showIcon message={t("Chỉ xác nhận sau khi đã kiểm tra tiền thực nhận trong tài khoản ngân hàng.")} />
      {(selected?.statusMessage || confirm.error) && <Alert type="error" showIcon message={selected?.statusMessage ? t(selected.statusMessage) : confirm.error?.message} />}
      <Form layout="vertical" onFinish={() => { if (selected && selected.canConfirm !== false) confirm.mutate(selected); }} style={{ marginTop: 16 }}>
        <Button type="primary" htmlType="submit" disabled={!selected || selected.canConfirm === false || query.isFetching} loading={confirm.isPending}>{t("Ghi nhận khoản thu")}</Button>
      </Form>
    </Modal>
  </Card>;
}
