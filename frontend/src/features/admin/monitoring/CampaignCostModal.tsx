import { DatePicker, Form, Input, InputNumber, Modal, Space, Typography } from 'antd';
import dayjs from 'dayjs';
import { useState } from 'react';
import { formatVnd } from '../../../shared/format';
import { dateFormat, t } from '../../../shared/i18n/runtime';
import MoneyInput from '../../../shared/ui/MoneyInput';
import type { CreateCampaignCostInput } from './types';

interface CampaignCostModalProps {
  open: boolean;
  onClose: () => void;
  onSubmit: (values: CreateCampaignCostInput) => void;
  defaultUpdater?: string;
}

export function CampaignCostModal({ open, onClose, onSubmit, defaultUpdater = 'admin' }: CampaignCostModalProps) {
  const [form] = Form.useForm();
  const [cashExpense, setCashExpense] = useState<number>(0);
  const [personHours, setPersonHours] = useState<number>(0);
  const [hourlyRate, setHourlyRate] = useState<number>(0);

  const totalCost = (cashExpense || 0) + (personHours || 0) * (hourlyRate || 0);

  const handleFinish = (values: {
    campaignName: string;
    channel?: string;
    recordedDate: dayjs.Dayjs;
    cashExpenseVnd: number;
    personHours: number;
    hourlyRateVnd?: number;
    notes?: string;
    updatedBy: string;
  }) => {
    onSubmit({
      campaignName: values.campaignName.trim(),
      channel: values.channel?.trim(),
      recordedDate: values.recordedDate.format('YYYY-MM-DD'),
      cashExpenseVnd: values.cashExpenseVnd || 0,
      personHours: values.personHours || 0,
      hourlyRateVnd: values.hourlyRateVnd,
      notes: values.notes?.trim(),
      updatedBy: values.updatedBy.trim(),
    });
    form.resetFields();
    onClose();
  };

  return (
    <Modal
      title={t("Thêm chi phí chiến dịch")}
      open={open}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t("Lưu chi phí")}
      cancelText={t("Hủy")}
      destroyOnClose
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={{
          recordedDate: dayjs(),
          cashExpenseVnd: 0,
          personHours: 0,
          hourlyRateVnd: 0,
          updatedBy: defaultUpdater,
        }}
        onFinish={handleFinish}
      >
        <Form.Item
          name="campaignName"
          label={t("Tên chiến dịch")}
          rules={[{ required: true, message: t("Vui lòng nhập tên chiến dịch.") }]}
        >
          <Input placeholder="vd: Facebook Ads Q4" />
        </Form.Item>

        <Form.Item name="channel" label={t("Kênh")}>
          <Input placeholder="Facebook, Google, Zalo..." />
        </Form.Item>

        <Form.Item
          name="recordedDate"
          label={t("Ngày ghi nhận")}
          rules={[{ required: true }]}
        >
          <DatePicker format={dateFormat()} style={{ width: '100%' }} />
        </Form.Item>

        <Space style={{ display: 'flex', width: '100%' }} size={12}>
          <Form.Item
            name="cashExpenseVnd"
            label={t("Chi phí tiền mặt (VND)")}
            style={{ flex: 1 }}
          >
            <MoneyInput
              style={{ width: '100%' }}
              min={0}
              step={50000}
              onChange={(val) => setCashExpense(Number(val) || 0)}
            />
          </Form.Item>

          <Form.Item
            name="personHours"
            label={t("Số giờ công")}
            style={{ flex: 1 }}
          >
            <InputNumber
              style={{ width: '100%' }}
              min={0}
              step={0.5}
              onChange={(val) => setPersonHours(Number(val) || 0)}
            />
          </Form.Item>
        </Space>

        <Form.Item name="hourlyRateVnd" label={t("Đơn giá giờ công (VND/giờ)")}>
          <MoneyInput
            style={{ width: '100%' }}
            min={0}
            step={10000}
            onChange={(val) => setHourlyRate(Number(val) || 0)}
          />
        </Form.Item>

        <div
          style={{
            padding: '12px 16px',
            background: '#f8faf9',
            borderRadius: 8,
            marginBottom: 16,
            border: '1px solid #eef2f1',
          }}
        >
          <Typography.Text type="secondary" style={{ fontSize: 13, display: 'block', marginBottom: 4 }}>
            {t("Chi phí tính bằng tiền mặt cộng số giờ công nhân đơn giá.")}
          </Typography.Text>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <strong>{t("Tổng chi phí")}:</strong>
            <span style={{ fontSize: 18, fontWeight: 700, color: '#17a486' }}>
              {formatVnd(totalCost)}
            </span>
          </div>
        </div>

        <Form.Item
          name="updatedBy"
          label={t("Người cập nhật")}
          rules={[{ required: true, message: t("Vui lòng nhập người cập nhật.") }]}
        >
          <Input />
        </Form.Item>

        <Form.Item name="notes" label={t("Ghi chú")}>
          <Input.TextArea rows={2} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
