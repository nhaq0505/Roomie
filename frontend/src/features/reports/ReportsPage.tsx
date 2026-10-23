import { DownloadOutlined, ReloadOutlined } from '@ant-design/icons';
import { useMutation, useQuery } from '@tanstack/react-query';
import type { TableProps } from 'antd';
import { App, Button, DatePicker, Empty, Pagination, Select, Spin, Tabs, Tag, Tooltip, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useMemo, useState } from 'react';
import { currentPeriod, formatVnd, periodLabel } from '../../shared/format';
import { dateFormat, formatDate, t } from '../../shared/i18n/runtime';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { buildingsApi } from '../buildings/index';
import { roomsApi } from '../rooms/index';
import { subscriptionApi } from '../subscription/index';
import ElectricityReconciliationPanel from './ElectricityReconciliationPanel';
import ProfitLossPanel from './ProfitLossPanel';
import { reportsApi, type DebtReportRow, type DebtReportTotals, type DepositReportRow, type DepositsReportTotals, type ExportReportType, type ReceiptReportRow, type ReceiptsReportTotals, type ReportFilters, type ReportPage } from './index';

type ReportTab = 'receipts' | 'debt' | 'deposits' | 'profit' | 'electricity';
const proTabs = new Set<ReportTab>(['profit', 'electricity']);
const exportType = (tab: ReportTab): ExportReportType => tab === 'profit' ? 'profit-loss' : tab;
type ReportRow = ReceiptReportRow | DebtReportRow | DepositReportRow;
type ReportData = ReportPage<ReceiptReportRow, ReceiptsReportTotals> | ReportPage<DebtReportRow, DebtReportTotals> | ReportPage<DepositReportRow, DepositsReportTotals>;
const pageSize = 50;

function todayInVietnam() {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
  return `${parts.find((part) => part.type === 'year')?.value}-${parts.find((part) => part.type === 'month')?.value}-${parts.find((part) => part.type === 'day')?.value}`;
}
function currentMonthRange() {
  const period = currentPeriod();
  const [year, month] = period.split('-').map(Number);
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate();
  return [`${period}-01`, `${period}-${String(lastDay).padStart(2, '0')}`] as const;
}
function formatTimestamp(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return formatDate(value);
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Ho_Chi_Minh' }).format(date);
}

function invoiceKindLabel(kind: string) {
  return kind === 'Deposit' ? t("Tiền cọc") : kind === 'Supplemental' ? t("Bổ sung") : t("Hóa đơn tháng");
}

export default function ReportsPage() {
  const { message } = App.useApp();
  const defaultRange = useMemo(() => currentMonthRange(), []);
  const [tab, setTab] = useState<ReportTab>('receipts');
  const [dateFrom, setDateFrom] = useState<string>(defaultRange[0]);
  const [dateTo, setDateTo] = useState<string>(defaultRange[1]);
  const [buildingId, setBuildingId] = useState<string>();
  const [roomId, setRoomId] = useState<string>();
  const [page, setPage] = useState(1);
  const buildingsQuery = useQuery({ queryKey: ['buildings', 'reports', 'all'], queryFn: () => buildingsApi.list(true) });
  const roomsQuery = useQuery({ queryKey: ['rooms', 'reports', 'all'], queryFn: () => roomsApi.list(undefined, true) });
  const subscriptionQuery = useQuery({ queryKey: ['subscription'], queryFn: subscriptionApi.getStatus });
  const isPro = subscriptionQuery.data?.isPro === true;
  const isProTab = proTabs.has(tab);
  const filters: ReportFilters = { dateFrom, dateTo, buildingId, roomId: isProTab ? undefined : roomId, page, pageSize };
  const queryKey = ['reports', tab, dateFrom, dateTo, buildingId, roomId, page, pageSize];
  const reportQuery = useQuery<ReportData>({
    queryKey,
    queryFn: async (): Promise<ReportData> => tab === 'receipts' ? reportsApi.receipts(filters) : tab === 'debt' ? reportsApi.debt(filters) : reportsApi.deposits(filters),
    enabled: dateFrom <= dateTo && !isProTab,
  });
  const exportMutation = useMutation({
    mutationFn: () => reportsApi.export(exportType(tab), filters),
    onSuccess: (blob) => {
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `roomie-${exportType(tab)}-${dateFrom}-${dateTo}.xlsx`;
      document.body.append(anchor); anchor.click(); anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
      message.success(t("Đã tải tệp Excel."));
    },
    onError: (error: Error) => message.error(error.message),
  });
  const allBuildings = buildingsQuery.data ?? [];
  const allRooms = roomsQuery.data ?? [];
  const selectedRooms = useMemo(() => allRooms.filter((room) => !buildingId || room.buildingId === buildingId), [allRooms, buildingId]);
  const buildingOptions = allBuildings.map((building) => ({ value: building.id, label: `${building.name}${building.isArchived ? ` · ${t("Đã lưu trữ")}` : ''}` }));
  const roomOptions = selectedRooms.map((room) => ({ value: room.id, label: `${room.name} · ${room.buildingName || allBuildings.find((building) => building.id === room.buildingId)?.name || t("Dãy trọ")}${room.isArchived ? ` · ${t("Đã lưu trữ")}` : ''}` }));
  const rangeChanged = (value: [Dayjs | null, Dayjs | null] | null) => {
    if (!value?.[0] || !value[1]) return;
    setDateFrom(value[0].format('YYYY-MM-DD')); setDateTo(value[1].format('YYYY-MM-DD')); setPage(1);
  };
  const setFilterTab = (key: string) => { setTab(key as ReportTab); setPage(1); };
  const clearFilters = () => { setDateFrom(defaultRange[0]); setDateTo(defaultRange[1]); setBuildingId(undefined); setRoomId(undefined); setPage(1); };
  const tabItems = [
    { key: 'receipts', label: t("Thu tiền") },
    { key: 'debt', label: t("Công nợ") },
    { key: 'deposits', label: t("Tiền cọc") },
    { key: 'profit', label: <span className="reports-tab-label">{t("Lãi/lỗ")}{!isPro && <Tag color="gold">Pro</Tag>}</span> },
    { key: 'electricity', label: <span className="reports-tab-label">{t("Đối chiếu điện")}{!isPro && <Tag color="gold">Pro</Tag>}</span> },
  ];
  const receiptColumns: TableProps<ReceiptReportRow>['columns'] = [
    { title: t("NGÀY NHẬN TIỀN"), dataIndex: 'receiptDate', key: 'receiptDate', width: 190, render: formatTimestamp },
    { title: t("PHÒNG / NGƯỜI THUÊ"), key: 'room', render: (_, row) => <div className="report-room-cell"><strong>{row.roomName}</strong><span>{row.tenantName} · {row.buildingName}</span></div> },
    { title: t("KỲ / LOẠI HÓA ĐƠN"), key: 'invoice', render: (_, row) => <span>{periodLabel(row.period)} · {row.invoiceKind === 'Deposit' ? t("Tiền cọc") : row.invoiceKind === 'Supplemental' ? t("Bổ sung") : t("Hóa đơn tháng")}</span> },
    { title: t("NGUỒN THU"), dataIndex: 'source', key: 'source', width: 140 },
    { title: t("GHI CHÚ"), dataIndex: 'note', key: 'note', ellipsis: true },
    { title: t("SỐ TIỀN"), dataIndex: 'amount', key: 'amount', align: 'right', width: 150, render: (value: number) => <strong>{formatVnd(value)}</strong> },
  ];
  const debtColumns: TableProps<DebtReportRow>['columns'] = [
    { title: t("KỲ HÓA ĐƠN"), dataIndex: 'period', key: 'period', width: 140, render: (value: string) => periodLabel(value) },
    { title: t("PHÒNG / NGƯỜI THUÊ"), key: 'room', render: (_, row) => <div className="report-room-cell"><strong>{row.roomName}</strong><span>{row.tenantName} · {row.buildingName}</span></div> },
    { title: t("LOẠI HÓA ĐƠN"), dataIndex: 'invoiceKind', key: 'invoiceKind', width: 145, render: (value: string) => <Tag className={`invoice-kind-tag invoice-kind-${value.toLowerCase()}`}>{invoiceKindLabel(value)}</Tag> },
    { title: t("HẠN THANH TOÁN"), dataIndex: 'dueDate', key: 'dueDate', width: 145, render: formatDate },
    { title: t("TỔNG HÓA ĐƠN"), dataIndex: 'total', key: 'total', align: 'right', width: 145, render: formatVnd },
    { title: t("ĐÃ THU"), dataIndex: 'paid', key: 'paid', align: 'right', width: 145, render: formatVnd },
    { title: t("CÒN NỢ HIỆN TẠI"), dataIndex: 'outstanding', key: 'outstanding', align: 'right', width: 160, render: (value: number, row) => <strong className={dayjs(row.dueDate).isBefore(dayjs(todayInVietnam()), 'day') ? 'balance-due' : undefined}>{formatVnd(value)}</strong> },
  ];
  const depositColumns: TableProps<DepositReportRow>['columns'] = [
    { title: t("PHÒNG / NGƯỜI THUÊ"), key: 'room', render: (_, row) => <div className="report-room-cell"><strong>{row.roomName}</strong><span>{row.tenantName} · {row.buildingName}</span></div> },
    { title: t("TIỀN CỌC YÊU CẦU"), dataIndex: 'depositAmount', key: 'depositAmount', align: 'right', width: 155, render: formatVnd },
    { title: t("THU TRONG KỲ"), dataIndex: 'periodReceived', key: 'periodReceived', align: 'right', width: 145, render: formatVnd },
    { title: t("HOÀN TRONG KỲ"), dataIndex: 'periodRefunded', key: 'periodRefunded', align: 'right', width: 145, render: formatVnd },
    { title: t("KHẤU TRỪ TRONG KỲ"), dataIndex: 'periodDeducted', key: 'periodDeducted', align: 'right', width: 155, render: formatVnd },
    { title: t("ĐANG GIỮ HIỆN TẠI"), dataIndex: 'currentHeld', key: 'currentHeld', align: 'right', width: 155, render: (value: number) => <strong>{formatVnd(value)}</strong> },
  ];

  const totals = reportQuery.data?.totals as Record<string, number> | undefined;
  const totalCount = reportQuery.data?.totalCount ?? 0;
  const tableData = (reportQuery.data?.items ?? []) as ReportRow[];
  const columns = (tab === 'receipts' ? receiptColumns : tab === 'debt' ? debtColumns : depositColumns) as TableProps<ReportRow>['columns'];
  const selectedRange = [dayjs(dateFrom), dayjs(dateTo)] as [Dayjs, Dayjs];

  return <>
    <PageTitle eyebrow={t("BÁO CÁO VÀ ĐỐI SOÁT")} title={t("Báo cáo") } subtitle={t("Theo dõi tiền thu, công nợ, tiền cọc, lãi ròng và điện tổng.")} extra={<Tooltip title={isProTab && !isPro ? t("Xuất Excel báo cáo này dành cho gói Pro.") : undefined}><Button icon={<DownloadOutlined />} loading={exportMutation.isPending} disabled={dateFrom > dateTo || (isProTab && !isPro)} onClick={() => exportMutation.mutate()}>{t("Xuất Excel")}</Button></Tooltip>} />
    <SectionCard className="reports-workspace">
      <Tabs activeKey={tab} items={tabItems} onChange={setFilterTab} />
      <div className="reports-filter-toolbar">
        <label><span>{t("Khoảng ngày")}</span><DatePicker.RangePicker aria-label={t("Khoảng ngày")} format={dateFormat()} value={selectedRange} onChange={rangeChanged} allowClear={false} /></label>
        <label><span>{t("Dãy trọ")}</span><Select aria-label={t("Lọc theo dãy trọ")} value={buildingId} allowClear showSearch optionFilterProp="label" placeholder={t("Tất cả dãy trọ")} options={buildingOptions} onChange={(value) => { setBuildingId(value); setRoomId(undefined); setPage(1); }} /></label>
        {!isProTab && <label><span>{t("Phòng")}</span><Select aria-label={t("Lọc theo phòng")} value={roomId} allowClear showSearch optionFilterProp="label" placeholder={t("Tất cả phòng")} options={roomOptions} disabled={roomsQuery.isPending || roomsQuery.isError} onChange={(value) => { setRoomId(value); setPage(1); }} /></label>}
        <Button icon={<ReloadOutlined />} onClick={clearFilters}>{t("Đặt lại")}</Button>
      </div>
      {tab === 'profit' && <Typography.Paragraph className="reports-scope-note">{t("Thực thu giống tab Thu tiền: tiền đã nhận cho hóa đơn, không gồm tiền cọc. Chi phí lấy từ Sổ chi phí theo ngày chi.")}</Typography.Paragraph>}
      {tab === 'profit' && <ProfitLossPanel filters={filters} isPro={isPro} />}
      {tab === 'electricity' && <ElectricityReconciliationPanel filters={filters} isPro={isPro} />}
      {tab === 'receipts' && <Typography.Paragraph className="reports-scope-note">{t("Tiền cọc thu được hiển thị riêng; tổng thu hóa đơn không cộng khoản cọc.")}</Typography.Paragraph>}
      {tab === 'debt' && <Typography.Paragraph className="reports-scope-note">{t("Công nợ là số dư hiện tại của hóa đơn có kỳ nằm trong khoảng đã chọn, không phải số dư tại ngày cuối kỳ.")}</Typography.Paragraph>}
      {tab === 'deposits' && <Typography.Paragraph className="reports-scope-note">{t("Biến động hoàn và khấu trừ theo khoảng ngày; tiền cọc đang giữ là số hiện tại.")}</Typography.Paragraph>}
      {!isProTab && !buildingsQuery.isError && !roomsQuery.isError && <ReportTotals tab={tab} totals={totals} />}
      {isProTab ? null : buildingsQuery.isError ? <LoadError error={buildingsQuery.error} onRetry={() => void buildingsQuery.refetch()} /> : roomsQuery.isError ? <LoadError error={roomsQuery.error} onRetry={() => void roomsQuery.refetch()} /> : reportQuery.isError ? <LoadError error={reportQuery.error} onRetry={() => void reportQuery.refetch()} /> : reportQuery.isPending && !reportQuery.data ? <div className="reports-loading"><Spin /> <span>{t("Đang tải báo cáo…")}</span></div> : <div className="reports-table-wrap">
        <ResponsiveTable<ReportRow>
          className="reports-table"
          rowKey={(row) => 'receiptId' in row ? row.receiptId : row.invoiceId}
          columns={columns}
          dataSource={tableData}
          loading={reportQuery.isPending || reportQuery.isFetching}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={<span><strong>{t("Chưa có dữ liệu trong khoảng này")}</strong><span className="empty-description">{t("Thử chọn khoảng ngày hoặc địa điểm khác.")}</span></span>} /> }}
          pagination={false}
          scroll={{ x: 1000 }}
          disableCompactPagination
          renderCompact={(row) => <ReportCompactRow tab={tab} row={row} />}
        />
        {totalCount > pageSize && <div className="reports-pagination"><Pagination current={page} pageSize={pageSize} total={totalCount} showSizeChanger={false} showTotal={(total) => t("{0} kết quả", [total])} onChange={(nextPage) => setPage(nextPage)} /></div>}
      </div>}
      {!isProTab && reportQuery.isError && <div className="reports-error-retry"><Button onClick={() => void reportQuery.refetch()}>{t("Thử lại")}</Button></div>}
    </SectionCard>
  </>;
}

function ReportTotals({ tab, totals }: { tab: ReportTab; totals?: Record<string, number> }) {
  if (!totals) return <div className="report-total-grid"><div className="report-total-tile report-total-loading" /><div className="report-total-tile report-total-loading" /><div className="report-total-tile report-total-loading" /></div>;
  const items = tab === 'receipts' ? [
    [t("Thu hóa đơn"), formatVnd(totals.invoiceCash)], [t("Thu tiền cọc (tách riêng)"), formatVnd(totals.depositCash)], [t("Tổng nhận (gồm tiền cọc)"), formatVnd(totals.totalCash)],
  ] : tab === 'debt' ? [
    [t("Nợ hiện tại"), formatVnd(totals.currentOutstanding)], [t("Nợ quá hạn hiện tại"), formatVnd(totals.overdueOutstanding)], [t("Hóa đơn còn nợ"), `${totals.invoiceCount ?? 0}`],
  ] : [
    [t("Thu cọc trong kỳ"), formatVnd(totals.periodReceived)], [t("Hoàn cọc trong kỳ"), formatVnd(totals.periodRefunded)], [t("Khấu trừ cọc trong kỳ"), formatVnd(totals.periodDeducted)], [t("Cọc đang giữ"), formatVnd(totals.currentHeld)],
  ];
  return <div className="report-total-grid">{items.map(([label, value], index) => <div className={`report-total-tile ${index === items.length - 1 ? 'report-total-accent' : ''}`} key={label}><span>{label}</span><strong>{value}</strong>{tab === 'debt' && index < 2 && <small>{t("Số dư hiện tại")}</small>}</div>)}</div>;
}

function ReportCompactRow({ tab, row }: { tab: ReportTab; row: ReceiptReportRow | DebtReportRow | DepositReportRow }) {
  if (tab === 'receipts' && 'receiptId' in row) return <div className="report-mobile-row"><div className="report-mobile-row-head"><div><strong>{row.roomName}</strong><span>{row.tenantName} · {row.buildingName}</span></div><strong className="report-mobile-amount">{formatVnd(row.amount)}</strong></div><div><span>{t("Ngày nhận tiền")}</span><strong>{formatTimestamp(row.receiptDate)}</strong></div><div><span>{t("Kỳ hóa đơn")}</span><strong>{periodLabel(row.period)} · {invoiceKindLabel(row.invoiceKind)}</strong></div><div><span>{t("Nguồn thu")}</span><strong>{row.source}</strong></div>{row.note && <div><span>{t("Ghi chú")}</span><strong>{row.note}</strong></div>}</div>;
  if (tab === 'debt' && 'invoiceId' in row && 'outstanding' in row) return <div className="report-mobile-row"><div className="report-mobile-row-head"><div><strong>{row.roomName}</strong><span>{row.tenantName} · {row.buildingName}</span></div><strong className="report-mobile-amount">{formatVnd(row.outstanding)}</strong></div><div><span>{t("Kỳ hóa đơn")}</span><strong>{periodLabel(row.period)}</strong></div><div><span>{t("Loại hóa đơn")}</span><strong>{invoiceKindLabel(row.invoiceKind)}</strong></div><div><span>{t("Hạn thanh toán")}</span><strong>{formatDate(row.dueDate)}</strong></div><div><span>{t("Tổng hóa đơn")}</span><strong>{formatVnd(row.total)}</strong></div><div><span>{t("Đã thu")}</span><strong>{formatVnd(row.paid)}</strong></div></div>;
  if ('currentHeld' in row) return <div className="report-mobile-row"><div className="report-mobile-row-head"><div><strong>{row.roomName}</strong><span>{row.tenantName} · {row.buildingName}</span></div><strong className="report-mobile-amount">{formatVnd(row.currentHeld)}</strong></div><div><span>{t("Tiền cọc yêu cầu")}</span><strong>{formatVnd(row.depositAmount)}</strong></div><div><span>{t("Thu trong kỳ")}</span><strong>{formatVnd(row.periodReceived)}</strong></div><div><span>{t("Hoàn trong kỳ")}</span><strong>{formatVnd(row.periodRefunded)}</strong></div><div><span>{t("Khấu trừ trong kỳ")}</span><strong>{formatVnd(row.periodDeducted)}</strong></div></div>;
  return null;
}
