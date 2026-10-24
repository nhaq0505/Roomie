import { CameraOutlined, CheckCircleFilled, DeleteOutlined, IdcardOutlined, UserAddOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, Input, Space, Tag, Typography } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { formatDate, t } from '../../shared/i18n/runtime';
import { decodeCccdQrFromImageBlob } from '../../shared/ui/cccdQr';
import type { Occupancy } from '../rooms';
import { tenantApi } from './api';

interface TenantSelfCheckinCardProps {
  token: string;
  occupancy: Occupancy;
  onUpdated: () => Promise<unknown> | void;
}

export default function TenantSelfCheckinCard({ token, occupancy, onUpdated }: TenantSelfCheckinCardProps) {
  const { message } = App.useApp();
  const tenantInputRef = useRef<HTMLInputElement | null>(null);
  const coResidentInputRef = useRef<HTMLInputElement | null>(null);

  const [phone, setPhone] = useState(occupancy.phone ?? '');
  const [email, setEmail] = useState(occupancy.tenantEmail ?? '');
  const [relation, setRelation] = useState(t("Người thuê trọ"));
  const [busyTarget, setBusyTarget] = useState<'tenant' | 'coResident' | null>(null);
  const [removingCitizenId, setRemovingCitizenId] = useState<string | null>(null);
  const [qrError, setQrError] = useState<string | null>(null);
  const [lastSavedSummary, setLastSavedSummary] = useState<string | null>(null);

  useEffect(() => {
    setPhone(occupancy.phone ?? '');
    setEmail(occupancy.tenantEmail ?? '');
  }, [occupancy.phone, occupancy.tenantEmail]);

  const handleFileChange = async (target: 'tenant' | 'coResident', file: File | undefined, inputEl: HTMLInputElement | null) => {
    if (inputEl) inputEl.value = '';
    if (!file) return;

    setQrError(null);
    setLastSavedSummary(null);
    setBusyTarget(target);

    try {
      const parsed = await decodeCccdQrFromImageBlob(file);
      if (!parsed) {
        setQrError(t("Không tìm thấy mã QR CCCD hợp lệ trong ảnh. Hãy chọn ảnh chụp rõ góc trên bên phải mặt trước CCCD."));
        return;
      }

      await tenantApi.selfCheckin(token, {
        target,
        fullName: parsed.fullName,
        citizenId: parsed.citizenId,
        dateOfBirth: parsed.dateOfBirth,
        gender: parsed.gender,
        citizenIdIssuedDate: parsed.issuedDate,
        citizenIdIssuedPlace: t("Cục Cảnh sát QLHC về TTXH"),
        permanentAddress: parsed.permanentAddress,
        phone: target === 'tenant' ? (phone.trim() || null) : null,
        email: target === 'tenant' ? (email.trim() || null) : null,
        relation: target === 'coResident' ? (relation.trim() || t("Người thuê trọ")) : null,
      });

      await onUpdated();
      const summary = `${parsed.fullName} (${parsed.citizenId})`;
      setLastSavedSummary(summary);
      message.success(t("Đã điền thông tin CCCD của {0}.", [parsed.fullName]));
    } catch (error) {
      const errMsg = error instanceof Error ? error.message : t("Không thể đọc file ảnh đã chọn. Vui lòng thử ảnh khác.");
      setQrError(errMsg);
    } finally {
      setBusyTarget(null);
    }
  };

  const handleRemoveCoResident = async (citizenId?: string | null) => {
    if (!citizenId) return;
    setRemovingCitizenId(citizenId);
    setQrError(null);
    try {
      await tenantApi.removeCoResident(token, citizenId);
      await onUpdated();
      message.success(t("Đã cập nhật người thuê."));
    } catch (error) {
      if (error instanceof Error) message.error(error.message);
    } finally {
      setRemovingCitizenId(null);
    }
  };

  const coResidents = occupancy.coResidents ?? [];
  const hasPrimaryCitizenId = Boolean(occupancy.tenantCitizenId);

  return (
    <Card className="tenant-self-checkin-card" bordered={false} style={{ marginBottom: 16, overflow: 'hidden' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12, flexWrap: 'wrap', marginBottom: 12 }}>
        <div>
          <Space size={8} align="center">
            <IdcardOutlined style={{ fontSize: 18, color: '#0f766e' }} />
            <Typography.Title level={4} style={{ margin: 0 }}>
              {`${''}Khai báo thông tin CCCD & Cư trú`}
            </Typography.Title>
          </Space>
          <div style={{ marginTop: 4 }}>
            <Typography.Text type="secondary">
              {`${''}Chụp hoặc tải ảnh mặt trước thẻ CCCD rõ nét để hệ thống tự động nhận diện và điền thông tin hợp đồng.`}
            </Typography.Text>
          </div>
        </div>
        {hasPrimaryCitizenId && (
          <Tag color="success" icon={<CheckCircleFilled />}>
            {t("Đã hoàn tất")}
          </Tag>
        )}
      </div>

      {qrError && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message={qrError}
        />
      )}

      {lastSavedSummary && (
        <Alert
          type="success"
          showIcon
          style={{ marginBottom: 12 }}
          message={t("Đã tự động đồng bộ thông tin người thuê mới nhất.")}
          description={lastSavedSummary}
        />
      )}

      <div style={{ padding: 12, borderRadius: 10, background: 'rgba(15, 118, 110, 0.05)', marginBottom: 12 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
          <div>
            <Typography.Text strong>{occupancy.tenantName}</Typography.Text>
            {occupancy.tenantCitizenId && (
              <div style={{ fontSize: 13 }}>
                <span>{t("Số CCCD")}: </span>
                <strong>{occupancy.tenantCitizenId}</strong>
                {occupancy.tenantDateOfBirth ? ` · ${formatDate(occupancy.tenantDateOfBirth)}` : ''}
                {occupancy.tenantGender ? ` · ${occupancy.tenantGender}` : ''}
              </div>
            )}
            {occupancy.tenantPermanentAddress && (
              <div style={{ fontSize: 13, color: '#475569' }}>
                {t("Địa chỉ thường trú")}: {occupancy.tenantPermanentAddress}
              </div>
            )}
          </div>
          <div>
            <input
              ref={tenantInputRef}
              type="file"
              accept="image/*"
              data-testid="tenant-cccd-upload-input"
              style={{ display: 'none' }}
              onChange={(e) => void handleFileChange('tenant', e.target.files?.[0], e.currentTarget)}
            />
            <Button
              type={hasPrimaryCitizenId ? 'default' : 'primary'}
              icon={<CameraOutlined />}
              loading={busyTarget === 'tenant'}
              disabled={busyTarget !== null && busyTarget !== 'tenant'}
              onClick={() => tenantInputRef.current?.click()}
            >
              {t("Quét / Tải ảnh CCCD")}
            </Button>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 8, marginTop: 10 }}>
          <Input
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            placeholder={t("Số điện thoại")}
            maxLength={60}
          />
          <Input
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder={t("Email liên hệ (nếu có)")}
            maxLength={320}
          />
        </div>
      </div>

      <div style={{ borderTop: '1px solid rgba(15, 23, 42, 0.08)', paddingTop: 12 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8, marginBottom: 8 }}>
          <Typography.Text strong>
            {t("Thành viên cùng thay đổi cư trú / ở cùng phòng")} ({coResidents.length})
          </Typography.Text>
          <Space wrap>
            <Input
              size="small"
              value={relation}
              onChange={(e) => setRelation(e.target.value)}
              placeholder={t("Mối quan hệ với chủ hộ")}
              style={{ width: 150 }}
              maxLength={100}
            />
            <input
              ref={coResidentInputRef}
              type="file"
              accept="image/*"
              data-testid="coresident-cccd-upload-input"
              style={{ display: 'none' }}
              onChange={(e) => void handleFileChange('coResident', e.target.files?.[0], e.currentTarget)}
            />
            <Button
              size="small"
              icon={<UserAddOutlined />}
              loading={busyTarget === 'coResident'}
              disabled={busyTarget !== null && busyTarget !== 'coResident'}
              onClick={() => coResidentInputRef.current?.click()}
            >
              {t("Quét CCCD thêm thành viên")}
            </Button>
          </Space>
        </div>

        {coResidents.length > 0 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {coResidents.map((member, index) => (
              <div
                key={member.citizenId || `${member.fullName}-${index}`}
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  padding: '8px 12px',
                  borderRadius: 8,
                  background: 'rgba(15, 23, 42, 0.03)',
                  gap: 8,
                }}
              >
                <div>
                  <Typography.Text strong>{member.fullName}</Typography.Text>
                  <div style={{ fontSize: 12, color: '#475569' }}>
                    {member.citizenId ? `${t("Số CCCD")}: ${member.citizenId}` : ''}
                    {member.dateOfBirth ? ` · ${formatDate(member.dateOfBirth)}` : ''}
                    {member.gender ? ` · ${member.gender}` : ''}
                    {member.relation ? ` · ${member.relation}` : ''}
                  </div>
                </div>
                {member.citizenId && (
                  <Button
                    type="text"
                    danger
                    size="small"
                    icon={<DeleteOutlined />}
                    loading={removingCitizenId === member.citizenId}
                    onClick={() => void handleRemoveCoResident(member.citizenId)}
                  >
                    {t("Xóa thành viên")}
                  </Button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </Card>
  );
}
