import type { InvoiceKindFilter, InvoiceStatusFilter, IssuanceFilter } from './types';
import { statusLabel, statusClass } from './presentation';
import { ArrowRightOutlined, FileTextOutlined, MessageOutlined, PlusOutlined, SearchOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import { Button, Col, DatePicker, Input, Row, Select, Space, Tag, Tooltip } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { currentPeriod, formatVnd, periodLabel } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { EmptyPanel, LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { type Invoice } from './index';
import InvoiceDrawer from './InvoiceDrawer';
import ManualPaymentRequests from './ManualPaymentRequests';
import { useInvoicesPage } from './useInvoicesPage';

function invoiceKindLabel(kind: Invoice['kind']) {
  if (kind === 'Deposit') return t("Tiền cọc nhận phòng");
  if (kind === 'Supplemental') return t("Hóa đơn bổ sung");
  return t("Hóa đơn tháng");
}

export default function InvoicesPage() {
const state = useInvoicesPage();
  const { kindFilter, setKindFilter, issuanceFilter, setIssuanceFilter, period, setPeriod, statusFilter, setStatusFilter, searchText, setSearchText, page, setPage, listQuery, invoices, hasActiveFilters, draftCount, issuedCount, totals, generateMutation, showInvoice, copyReminderMessage } = state;
  const columns: TableProps<Invoice>['columns'] = [
    { title: t("PHÒNG / NGƯỜI THUÊ"), key: 'room', width: 220, render: (_, item) => <div className="room-name-cell"><div className="room-avatar room-avatar-invoice"><FileTextOutlined /></div><div><strong>{item.roomName}</strong><span>{item.tenantName || t("Người thuê")} · {item.buildingName || t("Dãy trọ")}</span></div></div> },
    { title: t("LOẠI HÓA ĐƠN"), key: 'kind', width: 140, render: (_, item) => <Tag className={`invoice-kind-tag invoice-kind-${item.kind.toLowerCase()}`}>{invoiceKindLabel(item.kind)}</Tag> },
    { title: t("KỲ HÓA ĐƠN"), dataIndex: 'period', width: 120, render: (value: string) => periodLabel(value) },
    { title: t("TỔNG TIỀN"), dataIndex: 'total', width: 90, align: 'right', render: (value: number, item) => item.lifecycleState === 'Draft' ? <span title={t("Bản nháp chưa phát hành")}>{formatVnd(value)}</span> : <strong>{formatVnd(value)}</strong> },
    { title: t("ĐÃ THU"), dataIndex: 'paid', width: 90, align: 'right', render: (value: number, item) => item.lifecycleState === 'Draft' ? <span>—</span> : <span>{formatVnd(value)}</span> },
    { title: t("CÒN LẠI"), dataIndex: 'outstanding', width: 90, align: 'right', render: (value: number, item) => item.lifecycleState === 'Draft' ? <span>—</span> : <strong className={value > 0 ? 'balance-due' : 'balance-clear'}>{formatVnd(value)}</strong> },
    { title: t("TRẠNG THÁI"), key: 'status', width: 130, render: (_, item) => item.lifecycleState === 'Draft' ? <Tag className="draft-state-tag">{t("Bản nháp")}</Tag> : <Tag className={`status-pill ${statusClass(item.status)}`}>{statusLabel(item.status)}</Tag> },
    {
      title: t("THAO TÁC"),
      key: 'action',
      width: 170,
      align: 'right',
      render: (_, item) => (
        <Space size={4}>
          {item.lifecycleState !== 'Draft' && item.tenantToken && (
            <>
              <Tooltip title={t("Copy tin nhắn thu tiền")}>
                <Button
                  size="small"
                  icon={<MessageOutlined />}
                  onClick={(e) => { e.stopPropagation(); void copyReminderMessage(item); }}
                />
              </Tooltip>
            </>
          )}
          <Button type="link" size="small" style={{ whiteSpace: 'nowrap' }} onClick={() => showInvoice(item.id)}>
            {t("Chi tiết")} <ArrowRightOutlined />
          </Button>
        </Space>
      ),
    },
  ];
  const renderCompactInvoice = (item: Invoice) => (
    <div className="invoice-mobile-card">
      <div className="invoice-mobile-head">
        <div className="room-name-cell">
          <div className="room-avatar room-avatar-invoice"><FileTextOutlined /></div>
          <div>
            <strong>{item.roomName}</strong>
            <span>{item.tenantName || t("Người thuê")} · {item.buildingName || t("Dãy trọ")}</span>
          </div>
        </div>
        {item.lifecycleState === 'Draft' ? (
          <Tag className="draft-state-tag">{t("Bản nháp")}</Tag>
        ) : (
          <Tag className={`status-pill ${statusClass(item.status)}`}>{statusLabel(item.status)}</Tag>
        )}
      </div>
      <div className="invoice-mobile-meta">
        <Tag className={`invoice-kind-tag invoice-kind-${item.kind.toLowerCase()}`}>{invoiceKindLabel(item.kind)}</Tag>
        <span>{periodLabel(item.period)}</span>
      </div>
      <div className="invoice-mobile-amounts">
        <div>
          <span>{t("TỔNG TIỀN")}</span>
          {item.lifecycleState === 'Draft' ? (
            <strong className="invoice-draft-amount" title={t("Bản nháp chưa phát hành")}>{formatVnd(item.total)}</strong>
          ) : (
            <strong>{formatVnd(item.total)}</strong>
          )}
        </div>
        {item.lifecycleState !== 'Draft' && item.paid > 0 && (
          <div>
            <span>{t("ĐÃ THU")}</span>
            <strong>{formatVnd(item.paid)}</strong>
          </div>
        )}
        <div>
          <span>{t("CÒN LẠI")}</span>
          {item.lifecycleState === 'Draft' ? (
            <strong className="invoice-draft-amount">—</strong>
          ) : (
            <strong className={item.outstanding > 0 ? 'balance-due' : 'balance-clear'}>{formatVnd(item.outstanding)}</strong>
          )}
        </div>
      </div>
      <div className="invoice-mobile-actions">
        {item.lifecycleState !== 'Draft' && item.tenantToken && (
          <Tooltip title={t("Copy tin nhắn thu tiền")}>
            <Button
              icon={<MessageOutlined />}
              aria-label={t("Copy tin nhắn thu tiền")}
              onClick={(e) => { e.stopPropagation(); void copyReminderMessage(item); }}
            />
          </Tooltip>
        )}
        <Button type="default" className="invoice-mobile-detail-btn" onClick={() => showInvoice(item.id)}>
          {t("Chi tiết")} <ArrowRightOutlined />
        </Button>
      </div>
    </div>
  );

  const clearFilters = () => { setKindFilter('All'); setIssuanceFilter('All'); setPeriod(null); setStatusFilter('All'); setSearchText(''); };
  const setMonth = (value: Dayjs | null) => setPeriod(value?.format('YYYY-MM') ?? null);

  return <>
    <PageTitle eyebrow={t("THU TIỀN RÕ RÀNG")} title={t("Hóa đơn")} subtitle={t("Theo dõi tiền thuê, điện, nước và các khoản đã thu theo từng phòng.")} extra={(kindFilter === 'All' || kindFilter === 'Monthly') && period ? <Button title={t("Chỉ tạo hóa đơn còn thiếu; không phát hành các bản nháp.")} type="primary" icon={<PlusOutlined />} loading={generateMutation.isPending} onClick={() => generateMutation.mutate(period)}>{t("Tạo hóa đơn còn thiếu")}</Button> : undefined} />
    <ManualPaymentRequests />
    {listQuery.isError ? <LoadError error={listQuery.error} onRetry={() => void listQuery.refetch()} /> : listQuery.isPending ? <div className="content-card"><div className="loading-placeholder" /></div> : <>
      <Row gutter={[12, 12]} className="invoice-totals-row"><Col xs={12} md={8}><div className="invoice-total-tile"><span>{t("Tổng giá trị hóa đơn")}</span><strong>{formatVnd(totals.billed)}</strong></div></Col><Col xs={12} md={8}><div className="invoice-total-tile invoice-total-paid"><span>{t("Đã ghi nhận trên hóa đơn")}</span><strong>{formatVnd(totals.paid)}</strong></div></Col><Col xs={24} md={8}><div className="invoice-total-tile invoice-total-due"><span>{t("Còn phải thu")}</span><strong>{formatVnd(totals.outstanding)}</strong></div></Col></Row>
      <p className="invoice-summary-scope">{t("Ba số liệu tính từ {0} hóa đơn đã phát hành phù hợp bộ lọc. Tiền cọc không phải doanh thu.", [issuedCount])}{draftCount > 0 && <> · {t("{0} bản nháp chưa phát hành", [draftCount])}</>}</p>
      <SectionCard className="invoice-list-card" title={t("Hóa đơn")} extra={<div className="invoice-list-actions" data-filtered={hasActiveFilters}><span className="section-caption">{t("Kết quả bộ lọc: {0} hóa đơn", [listQuery.data?.total ?? 0])}</span>{hasActiveFilters && invoices.length > 0 && <Button type="text" onClick={clearFilters}>{t("Xóa lọc")}</Button>}</div>}>
        <div className="invoice-filter-toolbar">
          <label className="invoice-filter"><span>{t("Loại hóa đơn")}</span><Select aria-label={t("Loại hóa đơn")} value={kindFilter} onChange={(value) => setKindFilter(value as InvoiceKindFilter)} options={[{ value: 'All', label: t("Tất cả loại hóa đơn") }, { value: 'Monthly', label: t("Hóa đơn tháng") }, { value: 'Deposit', label: t("Tiền cọc các lượt thuê") }, { value: 'Supplemental', label: t("Hóa đơn bổ sung") }]} /></label>
          <label className="invoice-filter"><span>{t("Trạng thái phát hành")}</span><Select aria-label={t("Trạng thái phát hành")} value={issuanceFilter} onChange={(value) => setIssuanceFilter(value as IssuanceFilter)} options={[{ value: 'All', label: t("Tất cả trạng thái phát hành") }, { value: 'Issued', label: t("Đã phát hành") }, { value: 'Draft', label: t("Bản nháp") }]} /></label>
          <label className="invoice-filter"><span>{t("Kỳ hóa đơn")}</span><DatePicker aria-label={t("Kỳ hóa đơn")} picker="month" value={period ? dayjs(`${period}-01`) : null} onChange={setMonth} format="MM/YYYY" maxDate={dayjs(`${currentPeriod()}-01`)} placeholder={t("Tất cả kỳ")} allowClear /></label>
          <label className="invoice-filter"><span>{t("Trạng thái thanh toán")}</span><Select aria-label={t("Trạng thái thanh toán")} value={statusFilter} onChange={(value) => setStatusFilter(value as InvoiceStatusFilter)} options={[{ value: 'All', label: t("Tất cả trạng thái") }, { value: 'Unpaid', label: t("Chưa thanh toán") }, { value: 'Partial', label: t("Thanh toán một phần") }, { value: 'Paid', label: t("Đã thanh toán") }]} /></label>
          <label className="invoice-filter invoice-filter-search"><span>{t("Tìm hóa đơn")}</span><Input aria-label={t("Tìm hóa đơn")} prefix={<SearchOutlined />} value={searchText} onChange={(event) => setSearchText(event.target.value)} placeholder={t("Phòng, dãy trọ hoặc người thuê")} allowClear /></label>
        </div>
        {invoices.length ? <ResponsiveTable className="invoice-table" rowKey="id" columns={columns} dataSource={invoices} renderCompact={renderCompactInvoice} pagination={{ current:page, total:listQuery.data?.total ?? 0, onChange:setPage, pageSize:10, showSizeChanger:false, hideOnSinglePage:true }} scroll={{ x: 980 }} /> : hasActiveFilters ? <EmptyPanel title={t("Không có hóa đơn khớp bộ lọc")} description={t("Hãy đổi bộ lọc hoặc từ khóa để xem hóa đơn.")} action={<Button onClick={clearFilters}>{t("Xóa lọc")}</Button>} /> : <EmptyPanel title={t("Chưa có hóa đơn")} description={t("Hóa đơn sẽ xuất hiện sau khi bạn ghi nhận tiền cọc, tạo khoản bổ sung hoặc chốt chỉ số điện.")} />}
      </SectionCard>
    </>}
    <InvoiceDrawer state={state} />
  </>;
}
