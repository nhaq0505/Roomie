import { DeleteOutlined, EditOutlined, PauseCircleOutlined, PlayCircleOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, DatePicker, Empty, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Spin, Tag, Typography } from 'antd';
import type { TableProps } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useEffect, useRef, useState } from 'react';
import { currentPeriod, formatVnd, periodLabel } from '../../shared/format';
import { ApiError } from '../../shared/http/client';
import { t } from '../../shared/i18n/runtime';
import ProUpsell from '../../shared/ui/ProUpsell';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { LoadError } from '../../shared/ui/Ui';
import type { Building } from '../buildings/index';
import type { Room } from '../rooms/index';
import { recurringExpensesApi } from './api';
import { expenseCategoryColor, expenseCategoryLabel, expenseCategoryOptions, skipReasonLabel } from './categories';
import { locationOptions, vndFormatter, vndParser } from './ExpenseFormModal';
import type { ExpenseCategory, RecurringExpense, RecurringExpenseRequest } from './types';

interface FormValues {
  category: ExpenseCategory; amount: number; description?: string; buildingId?: string; roomId?: string;
  dayOfMonth: number; startPeriod: Dayjs; endPeriod?: Dayjs | null;
}

type Editor = { mode: 'create' } | { mode: 'edit'; template: RecurringExpense } | null;

export default function RecurringExpensesTab({ isPro, buildings, rooms, createSignal }: { isPro: boolean; buildings: Building[]; rooms: Room[]; createSignal: number }) {
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<FormValues>();
  const buildingId = Form.useWatch('buildingId', form);
  const [editor, setEditor] = useState<Editor>(null);
  const createKey = useRef<string | null>(null);
  const listQuery = useQuery({ queryKey: ['expenses', 'recurring'], queryFn: recurringExpensesApi.list });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['expenses'] });
  const onError = async (error: Error) => {
    if (error instanceof ApiError && error.status === 409) {
      await invalidate();
      message.warning(t("Mẫu chi định kỳ đã thay đổi ở nơi khác. Danh sách đã được tải lại."));
      setEditor(null);
      return;
    }
    message.error(error.message);
  };

  const openCreate = () => { createKey.current = crypto.randomUUID(); setEditor({ mode: 'create' }); };
  useEffect(() => { if (createSignal > 0 && isPro) openCreate(); }, [createSignal, isPro]);
  useEffect(() => {
    if (!editor) return;
    form.resetFields();
    if (editor.mode === 'edit') {
      const template = editor.template;
      form.setFieldsValue({
        category: template.category, amount: template.amount, description: template.description ?? undefined,
        buildingId: template.buildingId ?? undefined, roomId: template.roomId ?? undefined, dayOfMonth: template.dayOfMonth,
        startPeriod: dayjs(`${template.startPeriod}-01`), endPeriod: template.endPeriod ? dayjs(`${template.endPeriod}-01`) : null,
      });
    } else {
      form.setFieldsValue({ category: 'Internet', dayOfMonth: 5, startPeriod: dayjs(`${currentPeriod()}-01`) });
    }
  }, [editor, form]);

  const saveMutation = useMutation({
    mutationFn: (body: RecurringExpenseRequest) => {
      if (editor?.mode === 'edit') return recurringExpensesApi.update(editor.template.id, { ...body, expectedVersion: editor.template.version });
      if (!createKey.current) createKey.current = crypto.randomUUID();
      return recurringExpensesApi.create(body, createKey.current);
    },
    onSuccess: async () => {
      const wasEdit = editor?.mode === 'edit';
      createKey.current = null; setEditor(null); await invalidate();
      await queryClient.invalidateQueries({ queryKey: ['reports'] });
      message.success(wasEdit ? t("Đã cập nhật mẫu chi định kỳ.") : t("Đã tạo mẫu chi định kỳ. Các tháng đến hạn đã được ghi vào sổ chi."));
    },
    onError,
  });
  const stateMutation = useMutation({
    mutationFn: ({ template, action }: { template: RecurringExpense; action: 'pause' | 'resume' | 'delete' }): Promise<unknown> =>
      action === 'pause' ? recurringExpensesApi.pause(template.id, template.version)
        : action === 'resume' ? recurringExpensesApi.resume(template.id, template.version)
          : recurringExpensesApi.remove(template.id, template.version),
    onSuccess: async (_, { action }) => {
      await invalidate();
      message.success(action === 'pause' ? t("Đã tạm ngừng mẫu chi.") : action === 'resume' ? t("Đã tiếp tục mẫu chi.") : t("Đã xóa mẫu chi. Các khoản chi đã ghi vẫn được giữ."));
    },
    onError,
  });

  const submit = (values: FormValues) => saveMutation.mutate({
    category: values.category, amount: values.amount, description: values.description?.trim() || null,
    buildingId: values.buildingId ?? null, roomId: values.roomId ?? null, dayOfMonth: values.dayOfMonth,
    startPeriod: values.startPeriod.format('YYYY-MM'), endPeriod: values.endPeriod ? values.endPeriod.format('YYYY-MM') : null,
  });
  const editingTemplate = editor?.mode === 'edit' ? editor.template : null;
  const { buildingOptions, roomOptions } = locationOptions(buildings, rooms, buildingId, editingTemplate?.buildingId, editingTemplate?.roomId);
  const templates = listQuery.data ?? [];
  const busy = (template: RecurringExpense) => stateMutation.isPending && stateMutation.variables?.template.id === template.id;

  const columns: TableProps<RecurringExpense>['columns'] = [
    { title: t("KHOẢN CHI"), key: 'category', render: (_, row) => <div className="expense-cell"><Tag color={expenseCategoryColor[row.category]}>{expenseCategoryLabel(row.category)}</Tag>{row.description && <span>{row.description}</span>}</div> },
    { title: t("ÁP DỤNG"), key: 'location', render: (_, row) => <span>{row.buildingName ?? t("Chi chung")}{row.roomName ? ` · ${row.roomName}` : ''}</span> },
    { title: t("LỊCH"), key: 'schedule', render: (_, row) => <div className="expense-cell"><strong>{t("Ngày {0} hàng tháng", [row.dayOfMonth])}</strong><span>{periodLabel(row.startPeriod)}{row.endPeriod ? ` → ${periodLabel(row.endPeriod)}` : ''}</span></div> },
    { title: t("TRẠNG THÁI"), key: 'status', render: (_, row) => <div className="expense-cell">{row.skipReason ? <Tag color={row.skipReason === 'Paused' ? 'default' : 'orange'}>{skipReasonLabel(row.skipReason)}</Tag> : <Tag color="green">{t("Đang chạy")}</Tag>}{row.lastGeneratedPeriod && <span>{t("Đã ghi đến {0}", [periodLabel(row.lastGeneratedPeriod)])}</span>}</div> },
    { title: t("SỐ TIỀN"), dataIndex: 'amount', key: 'amount', align: 'right', render: (value: number) => <strong>{formatVnd(value)}</strong> },
    { title: '', key: 'actions', align: 'right', render: (_, row) => <Space size={0} wrap>
      <Button type="text" icon={<EditOutlined />} disabled={!isPro} onClick={() => setEditor({ mode: 'edit', template: row })} aria-label={t("Sửa mẫu chi")}>{t("Sửa")}</Button>
      {row.isPaused
        ? <Button type="text" icon={<PlayCircleOutlined />} disabled={!isPro} loading={busy(row)} onClick={() => stateMutation.mutate({ template: row, action: 'resume' })}>{t("Tiếp tục")}</Button>
        : <Button type="text" icon={<PauseCircleOutlined />} loading={busy(row)} onClick={() => stateMutation.mutate({ template: row, action: 'pause' })}>{t("Tạm ngừng")}</Button>}
      <Popconfirm title={t("Xóa mẫu chi định kỳ?")} description={t("Các khoản chi đã ghi vẫn được giữ.")} okText={t("Xóa")} cancelText={t("Hủy")} okButtonProps={{ danger: true }} onConfirm={() => stateMutation.mutateAsync({ template: row, action: 'delete' })}>
        <Button type="text" danger icon={<DeleteOutlined />} aria-label={t("Xóa mẫu chi")} />
      </Popconfirm>
    </Space> },
  ];

  return <div className="recurring-expenses">
    {!isPro && <ProUpsell title={t("Chi định kỳ tự động với gói Pro")} description={t("Tạo một lần cho internet, lương quản lý, tiền thuê nhà… Roomie tự ghi vào sổ chi mỗi tháng.")} />}
    {isPro && <div className="recurring-toolbar"><Typography.Text type="secondary">{t("Roomie tự ghi khoản chi vào ngày đã chọn mỗi tháng (giờ Việt Nam). Sửa mẫu không thay đổi các tháng đã ghi.")}</Typography.Text></div>}
    {listQuery.isError ? <LoadError error={listQuery.error} onRetry={() => void listQuery.refetch()} />
      : listQuery.isPending ? <div className="reports-loading"><Spin /></div>
        : templates.length === 0 ? (isPro ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t("Chưa có mẫu chi định kỳ")} /> : null)
          : <ResponsiveTable<RecurringExpense> rowKey="id" columns={columns} dataSource={templates} pagination={false} scroll={{ x: 900 }} />}
    <Modal title={editingTemplate ? t("Sửa mẫu chi định kỳ") : t("Thêm mẫu chi định kỳ")} open={!!editor} onCancel={() => { if (!saveMutation.isPending) { createKey.current = null; setEditor(null); } }}
      onOk={() => form.submit()} okText={editingTemplate ? t("Lưu thay đổi") : t("Tạo mẫu")} cancelText={t("Hủy")} confirmLoading={saveMutation.isPending} destroyOnClose>
      <Form form={form} layout="vertical" requiredMark={false} onFinish={submit}>
        <div className="expense-form-grid">
          <Form.Item label={t("Danh mục")} name="category" rules={[{ required: true, message: t("Chọn danh mục.") }]}><Select options={expenseCategoryOptions()} /></Form.Item>
          <Form.Item label={t("Số tiền mỗi tháng (VND)")} name="amount" rules={[{ required: true, message: t("Nhập số tiền chi.") }, { type: 'number', min: 1, max: 10_000_000_000, message: t("Số tiền từ 1 đến 10 tỷ đồng.") }]}>
            <InputNumber min={1} max={10_000_000_000} precision={0} step={10_000} formatter={vndFormatter} parser={vndParser} style={{ width: '100%' }} inputMode="numeric" />
          </Form.Item>
        </div>
        <div className="expense-form-grid">
          <Form.Item label={t("Dãy trọ")} name="buildingId"><Select allowClear showSearch optionFilterProp="label" placeholder={t("Chi chung")} options={buildingOptions} onChange={() => form.setFieldValue('roomId', undefined)} /></Form.Item>
          <Form.Item label={t("Phòng")} name="roomId"><Select allowClear showSearch optionFilterProp="label" placeholder={t("Không gắn phòng")} options={roomOptions} disabled={!buildingId} /></Form.Item>
        </div>
        <div className="expense-form-grid expense-form-grid-3">
          <Form.Item label={t("Ngày trong tháng")} name="dayOfMonth" rules={[{ required: true, message: t("Chọn ngày từ 1 đến 28.") }]}><InputNumber min={1} max={28} precision={0} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t("Từ kỳ")} name="startPeriod" rules={[{ required: true, message: t("Chọn kỳ bắt đầu.") }]}><DatePicker picker="month" format="MM/YYYY" allowClear={false} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t("Đến kỳ")} name="endPeriod" dependencies={['startPeriod']} rules={[({ getFieldValue }) => ({ validator: (_, value?: Dayjs | null) => !value || !getFieldValue('startPeriod') || !value.isBefore(getFieldValue('startPeriod'), 'month') ? Promise.resolve() : Promise.reject(new Error(t("Kỳ kết thúc phải từ kỳ bắt đầu trở đi."))) })]}>
            <DatePicker picker="month" format="MM/YYYY" placeholder={t("Không giới hạn")} style={{ width: '100%' }} />
          </Form.Item>
        </div>
        <Typography.Paragraph type="secondary" className="expense-form-hint">{t("Khi tạo mẫu, các tháng đã đến hạn (tối đa 2 tháng trước) sẽ được ghi ngay.")}</Typography.Paragraph>
        <Form.Item label={t("Ghi chú")} name="description"><Input.TextArea maxLength={500} showCount autoSize={{ minRows: 2, maxRows: 3 }} placeholder={t("Ví dụ: Internet FPT 300 Mbps")} /></Form.Item>
      </Form>
    </Modal>
  </div>;
}
