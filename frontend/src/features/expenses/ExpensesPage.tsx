import { DeleteOutlined, EditOutlined, PaperClipOutlined, PlusOutlined, ReloadOutlined, SyncOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import { Button, DatePicker, Empty, Pagination, Popconfirm, Select, Space, Spin, Tabs, Tag, Tooltip } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useMemo, useState } from 'react';
import { formatVnd, periodLabel } from '../../shared/format';
import { dateFormat, formatDate, t } from '../../shared/i18n/runtime';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import AttachmentDrawer from './AttachmentDrawer';
import { expenseCategoryColor, expenseCategoryLabel, expenseCategoryOptions } from './categories';
import ExpenseFormModal from './ExpenseFormModal';
import RecurringExpensesTab from './RecurringExpensesTab';
import type { Expense } from './types';
import { expensePageSize, useExpensesPage } from './useExpensesPage';

type ExpenseTab = 'ledger' | 'recurring';

export default function ExpensesPage() {
  const page = useExpensesPage();
  const [tab, setTab] = useState<ExpenseTab>('ledger');
  const [recurringCreate, setRecurringCreate] = useState(0);
  const { filters, expensesQuery, buildingsQuery, roomsQuery } = page;
  const buildings = buildingsQuery.data ?? [];
  const rooms = roomsQuery.data ?? [];
  const buildingOptions = buildings.map((building) => ({ value: building.id, label: `${building.name}${building.isArchived ? ` · ${t("Đã lưu trữ")}` : ''}` }));
  const roomOptions = useMemo(() => rooms.filter((room) => room.buildingId === filters.buildingId)
    .map((room) => ({ value: room.id, label: `${room.name}${room.isArchived ? ` · ${t("Đã lưu trữ")}` : ''}` })), [rooms, filters.buildingId]);
  const data = expensesQuery.data;
  const totals = data?.totals;
  const topCategory = totals?.byCategory[0];

  const columns: TableProps<Expense>['columns'] = [
    { title: t("NGÀY CHI"), dataIndex: 'expenseDate', key: 'expenseDate', width: 130, render: (value: string) => formatDate(value) },
    { title: t("DANH MỤC"), key: 'category', width: 220, render: (_, row) => <div className="expense-cell"><Tag color={expenseCategoryColor[row.category]}>{expenseCategoryLabel(row.category)}</Tag>{row.recurringExpenseId && <span><SyncOutlined /> {t("Định kỳ")}</span>}</div> },
    { title: t("ÁP DỤNG"), key: 'location', render: (_, row) => <div className="expense-cell"><strong>{row.buildingName ?? t("Chi chung")}</strong>{row.roomName && <span>{row.roomName}</span>}</div> },
    { title: t("GHI CHÚ"), key: 'description', ellipsis: true, render: (_, row) => <div className="expense-cell"><span>{row.description || '—'}</span>{row.utilityPeriod && <span>{t("EVN {0}: {1} kWh", [periodLabel(row.utilityPeriod), new Intl.NumberFormat().format(row.utilityKwh ?? 0)])}</span>}</div> },
    { title: t("SỐ TIỀN"), dataIndex: 'amount', key: 'amount', align: 'right', width: 150, render: (value: number) => <strong>{formatVnd(value)}</strong> },
    { title: '', key: 'actions', align: 'right', width: 170, render: (_, row) => <RowActions expense={row} /> },
  ];

  function RowActions({ expense }: { expense: Expense }) {
    return <Space size={0}>
      <Tooltip title={t("Chứng từ")}><Button type="text" icon={<PaperClipOutlined />} onClick={() => page.setAttachmentsFor(expense)} aria-label={t("Chứng từ của khoản chi")}>{expense.attachmentCount > 0 ? expense.attachmentCount : null}</Button></Tooltip>
      <Button type="text" icon={<EditOutlined />} onClick={() => page.openEdit(expense)} aria-label={t("Sửa khoản chi")} />
      <Popconfirm title={t("Xóa khoản chi này?")} description={t("Khoản chi và chứng từ kèm theo sẽ bị xóa khỏi sổ.")} okText={t("Xóa")} cancelText={t("Hủy")} okButtonProps={{ danger: true }} onConfirm={() => page.deleteMutation.mutateAsync(expense)}>
        <Button type="text" danger icon={<DeleteOutlined />} aria-label={t("Xóa khoản chi")} loading={page.deleteMutation.isPending && page.deleteMutation.variables?.id === expense.id} />
      </Popconfirm>
    </Space>;
  }

  const rangeChanged = (value: [Dayjs | null, Dayjs | null] | null) => {
    if (!value?.[0] || !value[1]) return;
    page.setRange(value[0].format('YYYY-MM-DD'), value[1].format('YYYY-MM-DD'));
  };
  const loadError = buildingsQuery.isError ? buildingsQuery : roomsQuery.isError ? roomsQuery : expensesQuery.isError ? expensesQuery : null;

  return <>
    <PageTitle eyebrow={t("SỔ CHI PHÍ")} title={t("Chi phí")} subtitle={t("Ghi lại tiền điện nước tổng, sửa chữa, lương và các khoản chi khác để biết lãi ròng.")}
      extra={tab === 'ledger'
        ? <Button type="primary" icon={<PlusOutlined />} onClick={page.openCreate}>{t("Thêm khoản chi")}</Button>
        : page.isPro ? <Button type="primary" icon={<PlusOutlined />} onClick={() => setRecurringCreate((value) => value + 1)}>{t("Thêm mẫu định kỳ")}</Button> : undefined} />
    <SectionCard className="expenses-workspace">
      <Tabs activeKey={tab} onChange={(key) => setTab(key as ExpenseTab)} items={[
        { key: 'ledger', label: t("Khoản chi") },
        { key: 'recurring', label: <span>{t("Chi định kỳ")} {!page.isPro && <Tag color="gold">Pro</Tag>}</span> },
      ]} />
      {tab === 'recurring' ? <RecurringExpensesTab isPro={page.isPro} buildings={buildings} rooms={rooms} createSignal={recurringCreate} /> : <>
        <div className="expenses-filter-toolbar">
          <label><span>{t("Khoảng ngày")}</span><DatePicker.RangePicker aria-label={t("Khoảng ngày")} format={dateFormat()} value={[dayjs(filters.dateFrom), dayjs(filters.dateTo)]} onChange={rangeChanged} allowClear={false} /></label>
          <label><span>{t("Dãy trọ")}</span><Select aria-label={t("Lọc theo dãy trọ")} value={filters.buildingId} allowClear showSearch optionFilterProp="label" placeholder={t("Tất cả dãy trọ")} options={buildingOptions} onChange={page.setBuildingId} /></label>
          <label><span>{t("Phòng")}</span><Select aria-label={t("Lọc theo phòng")} value={filters.roomId} allowClear showSearch optionFilterProp="label" placeholder={t("Tất cả phòng")} options={roomOptions} disabled={!filters.buildingId} onChange={page.setRoomId} /></label>
          <label><span>{t("Danh mục")}</span><Select aria-label={t("Lọc theo danh mục")} value={filters.category} allowClear placeholder={t("Tất cả danh mục")} options={expenseCategoryOptions()} onChange={page.setCategory} /></label>
          <Button icon={<ReloadOutlined />} onClick={page.clearFilters}>{t("Đặt lại")}</Button>
        </div>
        {filters.buildingId && <p className="reports-scope-note">{t("Khi lọc theo dãy, các khoản chi chung không gắn dãy sẽ không được tính.")}</p>}
        <div className="report-total-grid">
          {totals ? <>
            <div className="report-total-tile report-total-accent"><span>{t("Tổng chi")}</span><strong>{formatVnd(totals.total)}</strong></div>
            <div className="report-total-tile"><span>{t("Số khoản chi")}</span><strong>{totals.count}</strong></div>
            <div className="report-total-tile"><span>{t("Danh mục chi nhiều nhất")}</span><strong>{topCategory ? expenseCategoryLabel(topCategory.category) : '—'}</strong>{topCategory && <small>{formatVnd(topCategory.amount)}</small>}</div>
          </> : <><div className="report-total-tile report-total-loading" /><div className="report-total-tile report-total-loading" /><div className="report-total-tile report-total-loading" /></>}
        </div>
        {loadError ? <LoadError error={loadError.error} onRetry={() => void loadError.refetch()} />
          : expensesQuery.isPending && !data ? <div className="reports-loading"><Spin /> <span>{t("Đang tải khoản chi…")}</span></div>
            : (data?.items.length ?? 0) === 0 ? <Empty className="expenses-empty" image={Empty.PRESENTED_IMAGE_SIMPLE} description={<span><strong>{t("Chưa có khoản chi trong khoảng này")}</strong><span className="empty-description">{t("Bấm \"Thêm khoản chi\" để ghi khoản đầu tiên.")}</span></span>} />
            : <div className="reports-table-wrap">
              <ResponsiveTable<Expense> className="reports-table expenses-table" rowKey="id" columns={columns} dataSource={data?.items ?? []}
                loading={expensesQuery.isFetching} pagination={false} scroll={{ x: 960 }} disableCompactPagination
                renderCompact={(row) => <div className="report-mobile-row">
                  <div className="report-mobile-row-head"><div><strong>{expenseCategoryLabel(row.category)}</strong><span>{formatDate(row.expenseDate)} · {row.buildingName ?? t("Chi chung")}{row.roomName ? ` · ${row.roomName}` : ''}</span></div><strong className="report-mobile-amount">{formatVnd(row.amount)}</strong></div>
                  {row.description && <div><span>{t("Ghi chú")}</span><strong>{row.description}</strong></div>}
                  {row.utilityPeriod && <div><span>{t("Hóa đơn EVN")}</span><strong>{periodLabel(row.utilityPeriod)} · {new Intl.NumberFormat().format(row.utilityKwh ?? 0)} kWh</strong></div>}
                  <div className="expense-mobile-actions"><RowActions expense={row} /></div>
                </div>} />
              {(data?.totalCount ?? 0) > expensePageSize && <div className="reports-pagination"><Pagination current={filters.page} pageSize={expensePageSize} total={data?.totalCount ?? 0} showSizeChanger={false} showTotal={(total) => t("{0} kết quả", [total])} onChange={page.setPage} /></div>}
            </div>}
      </>}
    </SectionCard>
    <ExpenseFormModal open={!!page.editor} expense={page.editor?.mode === 'edit' ? page.editor.expense : null} buildings={buildings} rooms={rooms}
      isPro={page.isPro} saving={page.saveMutation.isPending} onSubmit={(values) => page.saveMutation.mutate(values)} onCancel={page.closeEditor} />
    <AttachmentDrawer expense={page.attachmentsFor} isPro={page.isPro} onClose={() => page.setAttachmentsFor(null)} />
  </>;
}
