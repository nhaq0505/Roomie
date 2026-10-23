import { QrcodeOutlined, SaveOutlined } from '@ant-design/icons';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { App, Button, Col, DatePicker, Form, Input, Row, Space, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useEffect, useState } from 'react';
import { dateFormat, t } from '../../shared/i18n/runtime';
import CccdQrScannerModal, { type ParsedCccdQr } from '../../shared/ui/CccdQrScannerModal';
import { PageTitle, SectionCard } from '../../shared/ui/Ui';
import { authApi } from './api';
import { useAuth } from './session';

interface OwnerProfileFormValues {
  name: string;
  phone?: string;
  citizenId?: string;
  dateOfBirth?: Dayjs | null;
  citizenIdIssuedDate?: Dayjs | null;
  citizenIdIssuedPlace?: string;
  permanentAddress?: string;
}

function toDayjs(value?: string | null): Dayjs | null {
  if (!value) return null;
  const parsed = dayjs(value);
  return parsed.isValid() ? parsed : null;
}

export default function OwnerProfilePage() {
  const { message } = App.useApp();
  const { user, patchUser } = useAuth();
  const queryClient = useQueryClient();
  const [profileForm] = Form.useForm<OwnerProfileFormValues>();
  const [qrOpen, setQrOpen] = useState(false);

  useEffect(() => {
    if (!user) return;
    profileForm.setFieldsValue({
      name: user.name ?? '',
      phone: user.phone ?? '',
      citizenId: user.citizenId ?? '',
      dateOfBirth: toDayjs(user.dateOfBirth),
      citizenIdIssuedDate: toDayjs(user.citizenIdIssuedDate),
      citizenIdIssuedPlace: user.citizenIdIssuedPlace ?? '',
      permanentAddress: user.permanentAddress ?? '',
    });
  }, [user, profileForm]);

  const profileMutation = useMutation({
    mutationFn: authApi.updateProfile,
    onSuccess: async (updated) => {
      patchUser(updated);
      await queryClient.invalidateQueries({ queryKey: ['room-documents'] });
      message.success(t("Đã cập nhật thông tin chủ trọ."));
    },
    onError: (err: Error) => message.error(err.message),
  });

  const handleQrDetected = (parsed: ParsedCccdQr) => {
    profileForm.setFieldsValue({
      name: parsed.fullName || profileForm.getFieldValue('name'),
      citizenId: parsed.citizenId,
      dateOfBirth: parsed.dateOfBirth ? dayjs(parsed.dateOfBirth) : profileForm.getFieldValue('dateOfBirth'),
      permanentAddress: parsed.permanentAddress || profileForm.getFieldValue('permanentAddress'),
      citizenIdIssuedDate: parsed.issuedDate ? dayjs(parsed.issuedDate) : profileForm.getFieldValue('citizenIdIssuedDate'),
      citizenIdIssuedPlace: profileForm.getFieldValue('citizenIdIssuedPlace') || t("Cục Cảnh sát QLHC về TTXH"),
    });
    message.success(t("Đã điền thông tin CCCD của {0}.", [parsed.fullName]));
  };

  return (
    <>
      <PageTitle
        eyebrow={t("HỒ SƠ CHỦ TRỌ")}
        title={t("Hồ sơ")}
        subtitle={t("Cập nhật họ tên, số điện thoại và CCCD của chủ trọ dùng chung cho mọi hợp đồng thuê phòng và tờ khai CT01.")}
      />

      <SectionCard
        title={t("Thông tin chủ trọ (Bên A)")}
        extra={
          <Space wrap>
            <Button
              type="primary"
              ghost
              icon={<QrcodeOutlined />}
              onClick={() => setQrOpen(true)}
            >
              {t("Quét / Tải ảnh CCCD")}
            </Button>
            <Button
              type="primary"
              icon={<SaveOutlined />}
              loading={profileMutation.isPending}
              onClick={() => profileForm.submit()}
            >
              {t("Lưu thông tin")}
            </Button>
          </Space>
        }
      >
        <Typography.Paragraph type="secondary" style={{ marginBottom: 16 }}>
          {t("Cập nhật họ tên, số điện thoại và CCCD của chủ trọ dùng chung cho mọi hợp đồng thuê phòng và tờ khai CT01.")}
        </Typography.Paragraph>
        <Form
          form={profileForm}
          layout="vertical"
          requiredMark={false}
          disabled={profileMutation.isPending}
          onFinish={(values) => {
            if (profileMutation.isPending) return;
            profileMutation.mutate({
              name: values.name.trim(),
              phone: values.phone?.trim() || null,
              citizenId: values.citizenId?.trim() || null,
              dateOfBirth: values.dateOfBirth ? values.dateOfBirth.format('YYYY-MM-DD') : null,
              citizenIdIssuedDate: values.citizenIdIssuedDate ? values.citizenIdIssuedDate.format('YYYY-MM-DD') : null,
              citizenIdIssuedPlace: values.citizenIdIssuedPlace?.trim() || null,
              permanentAddress: values.permanentAddress?.trim() || null,
            });
          }}
        >
          <Row gutter={16}>
            <Col xs={24} md={8}>
              <Form.Item
                name="name"
                label={t("Họ và tên chủ trọ")}
                rules={[{ required: true, whitespace: true, max: 160, message: t("Vui lòng nhập tên.") }]}
              >
                <Input maxLength={160} placeholder={t("Họ và tên")} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="phone" label={t("Số điện thoại chủ trọ")}>
                <Input maxLength={60} placeholder="09xx xxx xxx" />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item
                name="citizenId"
                label={t("Số CCCD / Định danh chủ hộ (12 số)")}
                rules={[{ pattern: /^[0-9]{9,12}$/, message: t("Nhập từ 9 đến 12 chữ số.") }]}
              >
                <Input maxLength={12} placeholder="07908800xxxx" />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col xs={12} md={6}>
              <Form.Item name="dateOfBirth" label={t("Ngày sinh")}>
                <DatePicker format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
              </Form.Item>
            </Col>
            <Col xs={12} md={6}>
              <Form.Item name="citizenIdIssuedDate" label={t("Ngày cấp CCCD")}>
                <DatePicker format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="citizenIdIssuedPlace" label={t("Nơi cấp CCCD")}>
                <Input maxLength={200} placeholder={t("Cục Cảnh sát QLHC về TTXH")} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="permanentAddress" label={t("Địa chỉ thường trú")} style={{ marginBottom: 0 }}>
            <Input maxLength={500} placeholder={t("Số nhà, đường, xã/phường, quận/huyện, tỉnh/thành phố")} />
          </Form.Item>
        </Form>
      </SectionCard>

      <CccdQrScannerModal
        open={qrOpen}
        title={t("Quét mã QR CCCD chủ trọ")}
        onClose={() => setQrOpen(false)}
        onDetected={handleQrDetected}
      />
    </>
  );
}
