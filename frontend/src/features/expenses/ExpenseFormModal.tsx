import { Alert, DatePicker, Form, Input, InputNumber, Modal, Select, Tag, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useEffect, useMemo } from 'react';
import { dateFormat, t } from '../../shared/i18n/runtime';
import type { Building } from '../buildings/index';
import type { Room } from '../rooms/index';
import { expenseCategoryOptions, todayInVietnam } from './categories';
import type { Expense, ExpenseCategory, ExpenseRequest } from './types';

interface FormValues {
  expenseDate: Dayjs;
  category: ExpenseCategory;
  amount: number;
  buildingId?: string;
  roomId?: string;
  description?: string;
  utilityPeriod?: Dayjs | null;
  utilityKwh?: number | null;
}

export const vndFormatter = (value?: string | number) => `${value ?? ''}`.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
export const vndParser = (value?: string) => Number((value ?? '').replace(/[^\d]/g, '')) || 0;

export function locationOptions(buildings: Building[], rooms: Room[], buildingId: string | undefined, keepBuildingId?: string | null, keepRoomId?: string | null) {
  const buildingOptions = buildings.filter((building) => !building.isArchived || building.id === keepBuildingId)
    .map((building) => ({ value: building.id, label: building.isArchived ? `${building.name} · ${t("Đã lưu trữ")}` : building.name }));
  const roomOptions = rooms.filter((room) => room.buildingId === buildingId && (!room.isArchived || room.id === keepRoomId))
    .map((room) => ({ value: room.id, label: room.isArchived ? `${room.name} · ${t("Đã lưu trữ")}` : room.name }));
  return { buildingOptions, roomOptions };
}

export default function ExpenseFormModal({ open, expense, buildings, rooms, isPro, saving, onSubmit, onCancel }: {
  open: boolean; expense: Expense | null; buildings: Building[]; rooms: Room[]; isPro: boolean; saving: boolean;
  onSubmit: (values: ExpenseRequest) => void; onCancel: () => void;
}) {
  const [form] = Form.useForm<FormValues>();
  const category = Form.useWatch('category', form);
  const buildingId = Form.useWatch('buildingId', form);
  const today = useMemo(() => dayjs(todayInVietnam()), []);
  const { buildingOptions, roomOptions } = locationOptions(buildings, rooms, buildingId, expense?.buildingId, expense?.roomId);
  const hasUtility = !!expense?.utilityPeriod;
  const utilityLocked = !isPro && !hasUtility;

  useEffect(() => {
    if (!open) return;
    form.resetFields();
    form.setFieldsValue(expense ? {
      expenseDate: dayjs(expense.expenseDate), category: expense.category, amount: expense.amount,
      buildingId: expense.buildingId ?? undefined, roomId: expense.roomId ?? undefined, description: expense.description ?? undefined,
      utilityPeriod: expense.utilityPeriod ? dayjs(`${expense.utilityPeriod}-01`) : null, utilityKwh: expense.utilityKwh,
    } : { expenseDate: today, category: 'Repair' });
  }, [open, expense, form, today]);

  const submit = (values: FormValues) => {
    const showUtility = values.category === 'Electricity' && !!values.buildingId;
    onSubmit({
      expenseDate: values.expenseDate.format('YYYY-MM-DD'),
      category: values.category,
      amount: values.amount,
      description: values.description?.trim() || null,
      buildingId: values.buildingId ?? null,
      roomId: values.roomId ?? null,
      utilityPeriod: showUtility && values.utilityPeriod ? values.utilityPeriod.format('YYYY-MM') : null,
      utilityKwh: showUtility && values.utilityPeriod && values.utilityKwh !== undefined ? values.utilityKwh : null,
    });
  };

  return <Modal title={expense ? t("Sửa khoản chi") : t("Thêm khoản chi")} open={open} onCancel={onCancel} onOk={() => form.submit()}
    okText={expense ? t("Lưu thay đổi") : t("Ghi khoản chi")} cancelText={t("Hủy")} confirmLoading={saving} destroyOnClose>
    {expense?.recurringExpenseId && <Alert className="expense-form-note" type="info" showIcon message={t("Khoản chi này được tạo tự động từ mẫu chi định kỳ. Sửa ở đây chỉ thay đổi tháng này.")} />}
    <Form form={form} layout="vertical" requiredMark={false} onFinish={submit} className="expense-form">
      <div className="expense-form-grid">
        <Form.Item label={t("Ngày chi")} name="expenseDate" rules={[{ required: true, message: t("Chọn ngày chi.") }]}>
          <DatePicker format={dateFormat()} allowClear={false} disabledDate={(value) => value.isAfter(today, 'day')} style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item label={t("Danh mục")} name="category" rules={[{ required: true, message: t("Chọn danh mục.") }]}>
          <Select options={expenseCategoryOptions()} />
        </Form.Item>
      </div>
      <Form.Item label={t("Số tiền (VND)")} name="amount" rules={[{ required: true, message: t("Nhập số tiền chi.") }, { type: 'number', min: 1, max: 10_000_000_000, message: t("Số tiền từ 1 đến 10 tỷ đồng.") }]}>
        <InputNumber min={1} max={10_000_000_000} precision={0} step={10_000} formatter={vndFormatter} parser={vndParser} style={{ width: '100%' }} inputMode="numeric" />
      </Form.Item>
      <div className="expense-form-grid">
        <Form.Item label={t("Dãy trọ")} name="buildingId" extra={t("Để trống nếu là chi chung cho mọi dãy.")}>
          <Select allowClear showSearch optionFilterProp="label" placeholder={t("Chi chung")} options={buildingOptions} onChange={() => form.setFieldValue('roomId', undefined)} />
        </Form.Item>
        <Form.Item label={t("Phòng")} name="roomId">
          <Select allowClear showSearch optionFilterProp="label" placeholder={t("Không gắn phòng")} options={roomOptions} disabled={!buildingId} />
        </Form.Item>
      </div>
      {category === 'Electricity' && <div className="expense-utility-box">
        <Typography.Text strong>{t("Đối chiếu điện EVN")} {utilityLocked && <Tag color="gold">Pro</Tag>}</Typography.Text>
        <Typography.Paragraph type="secondary">{buildingId
          ? t("Nhập kỳ và số kWh trên hóa đơn EVN để so với số điện đã lập cho các phòng.")
          : t("Chọn dãy trọ để nhập số kWh trên hóa đơn EVN.")}</Typography.Paragraph>
        <div className="expense-form-grid">
          <Form.Item label={t("Kỳ đối chiếu")} name="utilityPeriod" dependencies={['utilityKwh']}
            rules={[({ getFieldValue }) => ({ validator: (_, value) => (value || getFieldValue('utilityKwh') == null) ? Promise.resolve() : Promise.reject(new Error(t("Chọn kỳ đối chiếu."))) })]}>
            <DatePicker picker="month" format="MM/YYYY" style={{ width: '100%' }} disabled={!buildingId || utilityLocked} />
          </Form.Item>
          <Form.Item label={t("Số kWh EVN")} name="utilityKwh" dependencies={['utilityPeriod']}
            rules={[({ getFieldValue }) => ({ validator: (_, value) => (value != null || !getFieldValue('utilityPeriod')) ? Promise.resolve() : Promise.reject(new Error(t("Nhập số kWh EVN."))) })]}>
            <InputNumber min={0} max={10_000_000} precision={2} style={{ width: '100%' }} disabled={!buildingId || utilityLocked} />
          </Form.Item>
        </div>
      </div>}
      <Form.Item label={t("Ghi chú")} name="description">
        <Input.TextArea maxLength={500} showCount autoSize={{ minRows: 2, maxRows: 4 }} placeholder={t("Ví dụ: Thay bóng đèn hành lang tầng 2")} />
      </Form.Item>
    </Form>
  </Modal>;
}
