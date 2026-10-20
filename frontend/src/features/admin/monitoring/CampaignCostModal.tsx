import { DatePicker, Form, Input, InputNumber, Modal, Space, Tag, Typography } from 'antd';
import dayjs from 'dayjs';
import { useEffect, useState } from 'react';
import { formatVnd } from '../../../shared/format';
import { dateFormat, t } from '../../../shared/i18n/runtime';
import MoneyInput from '../../../shared/ui/MoneyInput';
import type { AdminCampaignCostItemDto, UpsertCampaignCostRequest } from './types';

interface CampaignCostModalProps {
  open: boolean;
  onClose: () => void;
  onSubmit: (values: UpsertCampaignCostRequest) => void;
  initialItem?: AdminCampaignCostItemDto | null;
  isLoading?: boolean;
}

export function CampaignCostModal({
  open,
  onClose,
  onSubmit,
  initialItem,
  isLoading,
}: CampaignCostModalProps) {
  const [form] = Form.useForm();
  const [cashExpense, setCashExpense] = useState<number>(0);
  const [personHours, setPersonHours] = useState<number>(0);
  const [hourlyRate, setHourlyRate] = useState<number | null | undefined>(undefined);

  useEffect(() => {
    if (initialItem && open) {
      setCashExpense(initialItem.cashVnd || 0);
      setPersonHours(initialItem.personHours || 0);
      setHourlyRate(initialItem.hourlyRate ?? null);
      form.setFieldsValue({
        campaign: initialItem.campaign,
        channel: initialItem.channel,
        source: initialItem.source,
        date: dayjs(initialItem.date),
        cashVnd: initialItem.cashVnd || 0,
        personHours: initialItem.personHours || 0,
        hourlyRate: initialItem.hourlyRate ?? undefined,
        note: initialItem.note,
      });
    } else if (open) {
      setCashExpense(0);
      setPersonHours(0);
      setHourlyRate(undefined);
      form.resetFields();
      form.setFieldsValue({
        date: dayjs(),
        cashVnd: 0,
        personHours: 0,
        hourlyRate: undefined,
      });
    }
  }, [initialItem, open, form]);

  const hasMissingValuation = personHours > 0 && (hourlyRate === null || hourlyRate === undefined);
  const totalCost = (cashExpense || 0) + (personHours || 0) * (hourlyRate || 0);

  const handleFinish = (values: {
    campaign: string;
    channel?: string;
    source?: string;
    date: dayjs.Dayjs;
    cashVnd: number;
    personHours: number;
    hourlyRate?: number | string | null;
    note?: string;
  }) => {
    let parsedRate: number | null = null;
    if (values.hourlyRate !== undefined && values.hourlyRate !== null && String(values.hourlyRate).trim() !== '') {
      const n = Number(values.hourlyRate);
      parsedRate = Number.isFinite(n) ? n : null;
    }

    onSubmit({
      campaign: values.campaign.trim(),
      channel: values.channel?.trim() || null,
      source: values.source?.trim() || null,
      date: values.date.format('YYYY-MM-DD'),
      cashVnd: values.cashVnd || 0,
      personHours: values.personHours || 0,
      hourlyRate: parsedRate,
      note: values.note?.trim() || null,
    });
    form.resetFields();
    onClose();
  };

  const isEditing = Boolean(initialItem);

  return (
    <Modal
      title={isEditing ? t("Sửa chi phí chiến dịch") : t("Thêm chi phí chiến dịch")}
      open={open}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={isEditing ? t("Cập nhật chi phí") : t("Lưu chi phí")}
      cancelText={t("Hủy")}
      confirmLoading={isLoading}
      destroyOnClose
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={{
          date: dayjs(),
          cashVnd: 0,
          personHours: 0,
          hourlyRate: undefined,
        }}
        onFinish={handleFinish}
      >
        <Form.Item
          name="campaign"
          label={t("Tên chiến dịch")}
          rules={[{ required: true, message: t("Vui lòng nhập tên chiến dịch.") }]}
        >
          <Input placeholder="vd: Facebook Ads Q4" />
        </Form.Item>

        <Space style={{ display: 'flex', width: '100%' }} size={12}>
          <Form.Item name="channel" label={t("Kênh")} style={{ flex: 1 }}>
            <Input placeholder="social, search, email..." />
          </Form.Item>

          <Form.Item name="source" label={t("Nguồn")} style={{ flex: 1 }}>
            <Input placeholder="facebook, google, zalo..." />
          </Form.Item>
        </Space>

        <Form.Item
          name="date"
          label={t("Ngày ghi nhận")}
          rules={[{ required: true }]}
        >
          <DatePicker format={dateFormat()} style={{ width: '100%' }} />
        </Form.Item>

        <Space style={{ display: 'flex', width: '100%' }} size={12}>
          <Form.Item
            name="cashVnd"
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

        <Form.Item name="hourlyRate" label={t("Đơn giá giờ công (VND/giờ)")}>
          <MoneyInput
            style={{ width: '100%' }}
            min={0}
            step={10000}
            placeholder={t("Thiếu đơn giá giờ công")}
            onChange={(val) => {
              if (val === null || val === undefined || val === '') {
                setHourlyRate(null);
              } else {
                setHourlyRate(Number(val));
              }
            }}
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
            {hasMissingValuation ? (
              <Space size={6}>
                <span style={{ fontSize: 18, fontWeight: 700, color: '#17a486' }}>
                  {formatVnd(cashExpense || 0)}
                </span>
                <Tag color="warning">{t("Thiếu đơn giá giờ công")}</Tag>
              </Space>
            ) : (
              <span style={{ fontSize: 18, fontWeight: 700, color: '#17a486' }}>
                {formatVnd(totalCost)}
              </span>
            )}
          </div>
        </div>

        {initialItem?.updatedByName && (
          <div style={{ marginBottom: 16, fontSize: 13, color: '#667085' }}>
            <span>{t("Người cập nhật")}: </span>
            <strong>{initialItem.updatedByName}</strong>
            {initialItem.updatedAt && (
              <span style={{ marginLeft: 8 }}>
                ({dayjs(initialItem.updatedAt).format('YYYY-MM-DD HH:mm')})
              </span>
            )}
          </div>
        )}

        <Form.Item name="note" label={t("Ghi chú")}>
          <Input.TextArea rows={2} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
