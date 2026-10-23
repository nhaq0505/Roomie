import { WarningOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import type { TableProps } from 'antd';
import { Empty, Spin, Tag, Tooltip } from 'antd';
import { formatVnd, periodLabel } from '../../shared/format';
import { ApiError } from '../../shared/http/client';
import { t } from '../../shared/i18n/runtime';
import ProUpsell from '../../shared/ui/ProUpsell';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { LoadError } from '../../shared/ui/Ui';
import { reportsApi, type ElectricityReconciliationRow, type ReportFilters } from './index';

const kwh = (value: number) => `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value)} kWh`;

function nullReasonText(reason: ElectricityReconciliationRow['lossNullReason']) {
  return reason === 'ZeroEvnKwh' ? t("Số kWh EVN bằng 0") : t("Chưa có dữ liệu: chưa nhập hóa đơn EVN cho kỳ này");
}

function LossCell({ row }: { row: ElectricityReconciliationRow }) {
  if (row.lossPercent === null) return <Tooltip title={nullReasonText(row.lossNullReason)}><span className="report-muted">{t("Chưa có dữ liệu")}</span></Tooltip>;
  return <span className={row.lossPercent > 10 ? 'reconcile-loss-high' : undefined}>{row.lossPercent.toFixed(2)}% · {kwh(row.lossKwh ?? 0)}</span>;
}

export default function ElectricityReconciliationPanel({ filters, isPro }: { filters: ReportFilters; isPro: boolean }) {
  const query = useQuery({
    queryKey: ['reports', 'electricity', filters.dateFrom.slice(0, 7), filters.dateTo.slice(0, 7), filters.buildingId],
    queryFn: () => reportsApi.electricity(filters),
    enabled: isPro && filters.dateFrom <= filters.dateTo,
    retry: (count, error) => !(error instanceof ApiError && (error.status === 403 || error.status === 400)) && count < 2,
  });
  const forbidden = query.error instanceof ApiError && query.error.status === 403;
  if (!isPro || forbidden) return <ProUpsell title={t("Đối chiếu điện EVN với gói Pro")} description={t("So số kWh trên hóa đơn EVN với số điện đã lập cho các phòng để phát hiện thất thoát và chênh lệch giá.")} />;
  if (query.isError) return <LoadError error={query.error} onRetry={() => void query.refetch()} />;
  if (query.isPending) return <div className="reports-loading"><Spin /> <span>{t("Đang tải báo cáo…")}</span></div>;
  const rows = query.data.rows;
  const columns: TableProps<ElectricityReconciliationRow>['columns'] = [
    { title: t("KỲ / DÃY"), key: 'period', render: (_, row) => <div className="report-room-cell"><strong>{periodLabel(row.period)}</strong><span>{row.buildingName}</span></div> },
    { title: t("EVN"), key: 'evn', align: 'right', render: (_, row) => row.evnKwh === null ? <span className="report-muted">{t("Chưa nhập")}</span> : <div className="report-room-cell"><strong>{kwh(row.evnKwh)}</strong><span>{formatVnd(row.evnAmount)}{row.evnBillCount > 1 ? ` · ${t("{0} hóa đơn", [row.evnBillCount])}` : ''}</span></div> },
    { title: t("ĐÃ LẬP CHO PHÒNG"), key: 'billed', align: 'right', render: (_, row) => <div className="report-room-cell"><strong>{kwh(row.billedKwh)}</strong><span>{formatVnd(row.billedAmount)}</span></div> },
    { title: t("THẤT THOÁT"), key: 'loss', align: 'right', render: (_, row) => <LossCell row={row} /> },
    { title: t("CHÊNH LỆCH TIỀN"), dataIndex: 'margin', key: 'margin', align: 'right', render: (value: number, row) => row.evnKwh === null ? '—' : <strong className={value < 0 ? 'profit-negative' : undefined}>{formatVnd(value)}</strong> },
    { title: '', key: 'drafts', render: (_, row) => row.draftInvoiceCount > 0 ? <Tooltip title={t("Còn hóa đơn nháp chưa phát hành trong kỳ này nên số đã lập có thể thiếu.")}><Tag color="orange" icon={<WarningOutlined />}>{t("{0} nháp", [row.draftInvoiceCount])}</Tag></Tooltip> : null },
  ];
  return <div className="electricity-panel">
    <p className="reports-scope-note">{t("Kỳ đối chiếu lấy theo tháng của khoảng ngày đã chọn. Chỉ tính hóa đơn phòng đã phát hành; nhập số kWh EVN khi ghi khoản chi \"Điện tổng\" có chọn dãy trọ.")}</p>
    {rows.length === 0 ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t("Chưa có dữ liệu điện trong các kỳ này")} />
      : <ResponsiveTable<ElectricityReconciliationRow> className="reports-table" rowKey={(row) => `${row.buildingId}-${row.period}`} columns={columns} dataSource={rows} pagination={false} scroll={{ x: 900 }}
        renderCompact={(row) => <div className="report-mobile-row">
          <div className="report-mobile-row-head"><div><strong>{periodLabel(row.period)}</strong><span>{row.buildingName}</span></div><strong className="report-mobile-amount"><LossCell row={row} /></strong></div>
          <div><span>{t("EVN")}</span><strong>{row.evnKwh === null ? t("Chưa nhập") : `${kwh(row.evnKwh)} · ${formatVnd(row.evnAmount)}`}</strong></div>
          <div><span>{t("Đã lập cho phòng")}</span><strong>{kwh(row.billedKwh)} · {formatVnd(row.billedAmount)}</strong></div>
          {row.evnKwh !== null && <div><span>{t("Chênh lệch tiền")}</span><strong className={row.margin < 0 ? 'profit-negative' : undefined}>{formatVnd(row.margin)}</strong></div>}
          {row.draftInvoiceCount > 0 && <div><span>{t("Hóa đơn nháp")}</span><strong>{row.draftInvoiceCount}</strong></div>}
        </div>} />}
  </div>;
}
