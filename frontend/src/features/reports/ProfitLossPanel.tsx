import { useQuery } from '@tanstack/react-query';
import type { TableProps } from 'antd';
import { Spin } from 'antd';
import { formatVnd, periodLabel } from '../../shared/format';
import { ApiError } from '../../shared/http/client';
import { t } from '../../shared/i18n/runtime';
import ProUpsell from '../../shared/ui/ProUpsell';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { LoadError } from '../../shared/ui/Ui';
import { expenseCategoryLabel } from '../expenses/index';
import { reportsApi, type ProfitLossMonth, type ReportFilters } from './index';

export default function ProfitLossPanel({ filters, isPro }: { filters: ReportFilters; isPro: boolean }) {
  const query = useQuery({
    queryKey: ['reports', 'profit', filters.dateFrom, filters.dateTo, filters.buildingId],
    queryFn: () => reportsApi.profitLoss(filters),
    enabled: isPro && filters.dateFrom <= filters.dateTo,
    retry: (count, error) => !(error instanceof ApiError && (error.status === 403 || error.status === 400)) && count < 2,
  });
  const forbidden = query.error instanceof ApiError && query.error.status === 403;
  if (!isPro || forbidden) return <ProUpsell title={t("Xem lãi ròng theo tháng với gói Pro")} description={t("Roomie lấy tiền thực thu trừ đi chi phí theo từng tháng và từng danh mục, xuất được ra Excel.")} />;
  if (query.isError) return <LoadError error={query.error} onRetry={() => void query.refetch()} />;
  if (query.isPending) return <div className="reports-loading"><Spin /> <span>{t("Đang tải báo cáo…")}</span></div>;
  const report = query.data;
  const peak = Math.max(1, ...report.months.flatMap((month) => [month.income, month.expenses]));
  const columns: TableProps<ProfitLossMonth>['columns'] = [
    { title: t("THÁNG"), dataIndex: 'month', key: 'month', render: (value: string) => periodLabel(value) },
    { title: t("THỰC THU"), dataIndex: 'income', key: 'income', align: 'right', render: (value: number) => formatVnd(value) },
    { title: t("CHI PHÍ"), dataIndex: 'expenses', key: 'expenses', align: 'right', render: (value: number) => formatVnd(value) },
    { title: t("LÃI RÒNG"), dataIndex: 'net', key: 'net', align: 'right', render: (value: number) => <strong className={value < 0 ? 'profit-negative' : undefined}>{formatVnd(value)}</strong> },
  ];

  return <div className="profit-loss-panel">
    <div className="report-total-grid">
      <div className="report-total-tile"><span>{t("Thực thu (không gồm tiền cọc)")}</span><strong>{formatVnd(report.totals.income)}</strong></div>
      <div className="report-total-tile"><span>{t("Chi phí")}</span><strong>{formatVnd(report.totals.expenses)}</strong></div>
      <div className="report-total-tile report-total-accent"><span>{t("Lãi ròng")}</span><strong className={report.totals.net < 0 ? 'profit-negative' : undefined}>{formatVnd(report.totals.net)}</strong></div>
    </div>
    {report.sharedExpensesExcluded > 0 && <p className="reports-scope-note reports-scope-warning">{t("Có {0} chi chung (không gắn dãy) không được tính khi lọc theo dãy.", [formatVnd(report.sharedExpensesExcluded)])}</p>}
    <div className="profit-chart profit-loss-chart" aria-label={t("Biểu đồ thu chi theo tháng")} role="img">
      <div className="profit-chart-legend"><span><i className="profit-chart-income" />{t("Thực thu")}</span><span><i className="profit-chart-expense" />{t("Chi phí")}</span></div>
      <div className="profit-chart-bars">{report.months.map((month) => <div className="profit-chart-month" key={month.month} title={`${periodLabel(month.month)}: ${formatVnd(month.income)} / ${formatVnd(month.expenses)}`}>
        <div className="profit-chart-pair">
          <span className="profit-chart-income" style={{ height: `${(month.income / peak) * 100}%` }} />
          <span className="profit-chart-expense" style={{ height: `${(month.expenses / peak) * 100}%` }} />
        </div>
        <small>{month.month.slice(5)}/{month.month.slice(2, 4)}</small>
      </div>)}</div>
    </div>
    <ResponsiveTable<ProfitLossMonth> className="reports-table" rowKey="month" columns={columns} dataSource={report.months} pagination={false} scroll={{ x: 640 }}
      renderCompact={(row) => <div className="report-mobile-row">
        <div className="report-mobile-row-head"><div><strong>{periodLabel(row.month)}</strong></div><strong className={`report-mobile-amount ${row.net < 0 ? 'profit-negative' : ''}`}>{formatVnd(row.net)}</strong></div>
        <div><span>{t("Thực thu")}</span><strong>{formatVnd(row.income)}</strong></div>
        <div><span>{t("Chi phí")}</span><strong>{formatVnd(row.expenses)}</strong></div>
      </div>} />
    {report.expensesByCategory.length > 0 && <>
      <h3 className="profit-section-title">{t("Chi phí theo danh mục")}</h3>
      <ResponsiveTable size="small" className="reports-table" rowKey="category" pagination={false} scroll={{ x: 480 }} dataSource={report.expensesByCategory} columns={[
        { title: t("DANH MỤC"), dataIndex: 'category', key: 'category', render: (value: string) => expenseCategoryLabel(value) },
        { title: t("SỐ KHOẢN"), dataIndex: 'count', key: 'count', align: 'right' },
        { title: t("SỐ TIỀN"), dataIndex: 'amount', key: 'amount', align: 'right', render: (value: number) => formatVnd(value) },
      ]}
      renderCompact={(row) => <div className="report-mobile-row">
        <div className="report-mobile-row-head"><div><strong>{expenseCategoryLabel(row.category)}</strong></div><strong className="report-mobile-amount">{formatVnd(row.amount)}</strong></div>
        <div><span>{t("SỐ KHOẢN")}</span><strong>{row.count}</strong></div>
      </div>} />
    </>}
  </div>;
}
