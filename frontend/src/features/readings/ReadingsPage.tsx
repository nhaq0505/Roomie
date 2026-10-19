import { CheckCircleFilled, ClockCircleOutlined, InfoCircleOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { TableProps } from 'antd';
import { Alert, App, Button, DatePicker, Form, InputNumber, Modal, Tag, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useEffect, useMemo, useRef, useState } from 'react';
import { currentPeriod, formatVnd, periodLabel } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import MoneyInput from '../../shared/ui/MoneyInput';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { EmptyPanel, LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { invoicesApi, type Invoice } from '../invoices/index';
import { roomsApi, type Room } from '../rooms/index';
import { readingsApi, type Reading } from './index';

interface ReadingRow { room: Room; saved?: Reading; draftInvoice?: Invoice; }
interface DraftSession { room: Room; currentReading: number; invoice: Invoice; }
interface DraftAmounts { rent: number; electricityAmount: number; waterAmount: number; serviceAmount: number; }

function lastDay(period: string) { const [year, month] = period.split('-').map(Number); return `${period}-${String(new Date(Date.UTC(year, month, 0)).getUTCDate()).padStart(2, '0')}`; }

export default function ReadingsPage() {
  const { message, modal } = App.useApp();
  const client = useQueryClient();
  const [period, setPeriod] = useState(currentPeriod());
  const [drafts, setDrafts] = useState<Record<string, number | null>>({});
  const [draftSession, setDraftSession] = useState<DraftSession | null>(null);
  const [draftForm] = Form.useForm<DraftAmounts>();
  const publishKeys = useRef(new Map<string, string>());
  const roomsQuery = useQuery({ queryKey: ['rooms', period], queryFn: () => roomsApi.list(period) });
  const readingsQuery = useQuery({ queryKey: ['readings', period], queryFn: () => readingsApi.list(period) });
  const invoicesQuery = useQuery({ queryKey: ['invoices', 'period', period], queryFn: () => invoicesApi.list(period) });
  const rows = useMemo<ReadingRow[]>(() => (roomsQuery.data ?? [])
    .filter((room) => room.occupancy && room.occupancy.startDate <= lastDay(period))
    .map((room) => ({
      room,
      saved: (readingsQuery.data ?? []).find((reading) => reading.roomId === room.id && reading.occupancyId === room.occupancy?.id),
      draftInvoice: (invoicesQuery.data ?? []).find((invoice) => invoice.kind === 'Monthly' && invoice.roomId === room.id && invoice.period === period && invoice.lifecycleState === 'Draft' && invoice.occupancyId === room.occupancy?.id),
    })), [roomsQuery.data, readingsQuery.data, invoicesQuery.data, period]);
  useEffect(() => { setDrafts(Object.fromEntries(rows.map(({ room, saved }) => [room.id, saved?.currentReading ?? null]))); }, [rows]);

  const refresh = async () => Promise.all([
    client.invalidateQueries({ queryKey: ['readings', period] }), client.invalidateQueries({ queryKey: ['invoices'] }),
    client.invalidateQueries({ queryKey: ['rooms'] }), client.invalidateQueries({ queryKey: ['dashboard'] }),
  ]);
  const readingMutation = useMutation({
    mutationFn: (body: { roomId: string; period: string; currentReading: number }) => readingsApi.save(body),
    onSuccess: async () => {
      await refresh();
      message.success(t("Đã chốt chỉ số và lập hóa đơn."));
    },
    onError: (error: Error) => message.error(error.message),
  });
  const publishMutation = useMutation({
    mutationFn: ({ invoice, values, requestId }: { invoice: Invoice; values: DraftAmounts; requestId: string; key: string }) => invoicesApi.publishDraft(invoice.id, { ...values, expectedVersion: invoice.version ?? 0, requestId }),
    onSuccess: async (_, variables) => { await refresh(); setDraftSession(null); publishKeys.current.delete(variables.key); message.success(t("Đã phát hành hóa đơn nháp.")); },
    onError: (error: Error) => message.error(error.message),
  });

  const openDraft = (row: ReadingRow) => {
    if (row.saved?.currentReading == null || !row.draftInvoice) return;
    setDraftSession({ room: row.room, currentReading: row.saved.currentReading, invoice: row.draftInvoice });
    draftForm.setFieldsValue({ rent: row.draftInvoice.rent, electricityAmount: row.draftInvoice.electricityAmount, waterAmount: row.draftInvoice.waterAmount, serviceAmount: row.draftInvoice.serviceAmount });
  };
  const confirmReading = (row: ReadingRow, currentReading: number) => {
    modal.confirm({
      title: t("Chốt chỉ số và lập hóa đơn?"),
      content: t("Tiền thuê được tính đủ theo tháng, không phụ thuộc ngày bắt đầu thuê."),
      okText: t("Chốt & lập hóa đơn"), cancelText: t("Kiểm tra lại"),
      onOk: () => readingMutation.mutateAsync({ roomId: row.room.id, period, currentReading }),
    });
  };
  const publishDraft = (values: DraftAmounts) => {
    if (!draftSession?.invoice) return;
    const key = `${draftSession.invoice.id}:${JSON.stringify(values)}`;
    let requestId = publishKeys.current.get(key);
    if (!requestId) { requestId = crypto.randomUUID(); publishKeys.current.set(key, requestId); }
    modal.confirm({
      title: t("Phát hành hóa đơn nháp?"),
      content: t("Khách sẽ thấy hóa đơn sau khi phát hành. Kiểm tra đủ tiền thuê, điện, nước và phí dịch vụ trước khi tiếp tục."),
      okText: t("Phát hành hóa đơn"), cancelText: t("Kiểm tra lại"),
      onOk: () => publishMutation.mutateAsync({ invoice: draftSession.invoice!, values, requestId: requestId!, key }),
    });
  };

  const columns: TableProps<ReadingRow>['columns'] = [
    { title: t("PHÒNG / NGƯỜI THUÊ"), key: 'room', render: (_, row) => <div className="room-name-cell"><div className="room-avatar"><span>⚡</span></div><div><strong>{row.room.name}</strong><span>{row.room.occupancy?.tenantName} · {row.room.buildingName || t("Dãy trọ")}</span></div></div> },
    { title: t("CHỈ SỐ TRƯỚC"), key: 'previous', render: (_, row) => <span className="meter-value">{row.saved?.previousReading ?? row.room.previousReading ?? row.room.initialReading ?? 0} <small>kWh</small></span> },
    { title: t("CHỈ SỐ MỚI"), key: 'current', render: (_, row) => row.saved?.currentReading !== undefined ? <div className="saved-reading"><strong>{row.saved.currentReading} <small>kWh</small></strong></div> : <InputNumber aria-label={t("Chỉ số mới {0}", [row.room.name])} min={row.room.previousReading ?? row.room.initialReading ?? 0} precision={3} step={0.001} value={drafts[row.room.id] ?? undefined} onChange={(value) => setDrafts((current) => ({ ...current, [row.room.id]: value }))} addonAfter="kWh" style={{ width: 170 }} placeholder={t("Nhập chỉ số")} /> },
    { title: t("SỬ DỤNG"), key: 'consumption', render: (_, row) => {
      const previous = row.saved?.previousReading ?? row.room.previousReading ?? row.room.initialReading ?? 0;
      const current = row.saved?.currentReading ?? drafts[row.room.id];
      const usage = current == null ? null : Math.round(Math.max(0, current - previous) * 1000) / 1000;
      const rate = row.room.electricityRate ?? 0;
      const estimatedCost = usage == null ? null : Math.round(usage * rate);
      return (
        <div>
          <span className="consumption-value">{usage === null ? '—' : `${usage} kWh`}</span>
          {estimatedCost != null && usage !== null && (
            <div style={{ color: '#8a999d', fontSize: '11px', marginTop: 2 }}>
              ≈ {formatVnd(estimatedCost)}
            </div>
          )}
        </div>
      );
    } },
    { title: t("TRẠNG THÁI PHÁT HÀNH"), key: 'lifecycle', width: 140, render: (_, row) => row.draftInvoice ? <Tag className="draft-state-tag" icon={<ClockCircleOutlined />}>{t("Bản nháp")}</Tag> : row.saved ? <Tag icon={<CheckCircleFilled />} className="submitted-tag">{t("Đã phát hành")}</Tag> : <span>—</span> },
    { title: '', key: 'action', fixed: 'right', width: 190, align: 'right', render: (_, row) => {
      if (row.draftInvoice) return <Button type="primary" size="small" onClick={() => openDraft(row)}>{t("Sửa bản nháp")}</Button>;
      if (row.saved) return <span className="already-done">{t("Đã phát hành")}</span>;
      const currentReading = drafts[row.room.id];
      const invalid = currentReading == null || currentReading < (row.room.previousReading ?? row.room.initialReading ?? 0);
      return (
        <Button
          type="primary"
          size="small"
          loading={readingMutation.isPending && readingMutation.variables?.roomId === row.room.id}
          disabled={invalid || readingMutation.isPending}
          onClick={() => {
            if (currentReading != null) confirmReading(row, currentReading);
          }}
        >
          {t("Chốt & lập hóa đơn")}
        </Button>
      );
    } },
  ];
  const setMonth = (value: Dayjs | null) => { if (value) setPeriod(value.format('YYYY-MM')); };
  const occupied = rows.length;
  const done = rows.filter((row) => row.saved?.currentReading !== undefined).length;
  const draftSessionAmounts = Form.useWatch([], draftForm) as DraftAmounts | undefined;
  const draftTotal = (draftSessionAmounts?.rent ?? draftSession?.invoice.rent ?? 0) +
    (draftSessionAmounts?.electricityAmount ?? draftSession?.invoice.electricityAmount ?? 0) +
    (draftSessionAmounts?.waterAmount ?? draftSession?.invoice.waterAmount ?? 0) +
    (draftSessionAmounts?.serviceAmount ?? draftSession?.invoice.serviceAmount ?? 0);

  return <>
    <PageTitle eyebrow={t("CHỐT SỐ ĐIỆN HÀNG THÁNG")} title={t("Chỉ số điện")} subtitle={t("Ghi nhận chỉ số cuối kỳ. Hóa đơn sẽ được lập ngay sau khi bạn chốt.")} extra={<DatePicker aria-label={t("Tháng ghi chỉ số")} picker="month" value={dayjs(`${period}-01`)} onChange={setMonth} format="MM/YYYY" maxDate={dayjs(`${currentPeriod()}-01`)} allowClear={false} />} />
    {roomsQuery.isError ? <LoadError error={roomsQuery.error} onRetry={() => void roomsQuery.refetch()} /> : readingsQuery.isError ? <LoadError error={readingsQuery.error} onRetry={() => void readingsQuery.refetch()} /> : invoicesQuery.isError ? <LoadError error={invoicesQuery.error} onRetry={() => void invoicesQuery.refetch()} /> : roomsQuery.isPending || readingsQuery.isPending || invoicesQuery.isPending ? <div className="content-card"><div className="loading-placeholder" /></div> : <>
      <div className="reading-progress-card"><div className="reading-progress-mark"><InfoCircleOutlined /></div><div className="reading-progress-copy"><strong>{periodLabel(period)} <span>·</span> {done}/{occupied} {t("phòng đã chốt")}</strong><span>{t("Chỉ số trước được lấy từ lần ghi gần nhất hoặc lúc tạo phòng.")}</span></div><div className="reading-progress-meter"><div style={{ width: `${occupied ? (done / occupied) * 100 : 0}%` }} /></div></div>
      <SectionCard className="reading-section" title={t("Danh sách phòng đang thuê")} extra={<span className="section-caption">{periodLabel(period)}</span>}>
        <div className="reading-notice"><InfoCircleOutlined /><span><strong>{t("Tiền thuê tính đủ theo tháng")}</strong> {t("Tiền thuê được tính đủ theo tháng, không phụ thuộc ngày bắt đầu thuê.")}</span></div>
        {rows.length ? <ResponsiveTable className="reading-table" rowKey={(row) => row.room.id} columns={columns} dataSource={rows} pagination={{ pageSize: 10, showSizeChanger: false, hideOnSinglePage: true }} scroll={{ x: 900 }} /> : <EmptyPanel title={occupied === 0 ? t("Chưa có phòng đang thuê") : t("Chưa có phòng cần chốt cho {0}", [periodLabel(period)])} description={occupied === 0 ? t("Thêm người thuê vào phòng trước khi ghi chỉ số điện.") : t("Tất cả phòng hiện đã được chốt chỉ số trong kỳ này.")} />}
        {rows.length > 0 && done === rows.length && <div className="all-readings-done"><CheckCircleFilled /> {t("Tất cả phòng đang thuê đã có hóa đơn cho kỳ này.")}</div>}
      </SectionCard>
    </>}
    <Modal className="legacy-draft-modal" title={t("Xem và phát hành bản nháp cũ")} open={!!draftSession} onCancel={() => { if (!publishMutation.isPending) setDraftSession(null); }} footer={null} confirmLoading={publishMutation.isPending} width={620} destroyOnClose>
      {draftSession && <>
        <Alert type="warning" showIcon message={t("Bản nháp chưa phát hành") } description={t("Đây là bản nháp đã lưu trước thay đổi cách tính tiền thuê. Kiểm tra các khoản hiện có; nội dung cũ không tự đổi. Hóa đơn chỉ gửi cho khách sau khi bạn phát hành.")} />
        <div className="legacy-draft-summary"><span>{draftSession.room.name} · {draftSession.room.occupancy?.tenantName}</span><span>{t("Kỳ hóa đơn")} · {periodLabel(draftSession.invoice.period)}</span><span>{t("Chỉ số điện")} {draftSession.invoice.previousReading ?? draftSession.room.previousReading ?? draftSession.room.initialReading ?? 0} → {draftSession.currentReading} kWh</span></div>
        <Form form={draftForm} layout="vertical" requiredMark={false} onFinish={publishDraft} initialValues={{ rent: draftSession.invoice.rent, electricityAmount: draftSession.invoice.electricityAmount, waterAmount: draftSession.invoice.waterAmount, serviceAmount: draftSession.invoice.serviceAmount }}>
          <Form.Item label={t("Tiền thuê phòng") } name="rent" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t("Tiền điện")} name="electricityAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t("Tiền nước") } name="waterAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t("Phí dịch vụ")} name="serviceAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
          <div className="legacy-draft-total"><span>{t("Tổng tiền sau khi chỉnh")}</span><strong>{formatVnd(draftTotal)}</strong></div>
        </Form>
        <Typography.Paragraph className="draft-version-note">{t("Phiên bản bản nháp")} {draftSession.invoice.version ?? 0}</Typography.Paragraph>
        <Button type="primary" block loading={publishMutation.isPending} onClick={() => draftForm.submit()}>{t("Phát hành hóa đơn")}</Button>
      </>}
    </Modal>
  </>;
}
