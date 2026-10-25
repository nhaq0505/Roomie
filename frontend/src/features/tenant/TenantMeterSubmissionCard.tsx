import { CameraOutlined, CheckCircleFilled, ClockCircleOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, Input, InputNumber, Space, Tag, Typography } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { periodLabel } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import type { Room } from '../rooms';
import { tenantApi } from './api';
import type { TenantMeterSubmission } from './types';

interface TenantMeterSubmissionCardProps {
  token: string;
  room: Room;
  currentPeriod?: string;
  currentPeriodHasReading?: boolean;
  meterSubmission?: TenantMeterSubmission | null;
  onSubmitted: () => Promise<unknown> | void;
}

const MAX_PHOTO_BYTES = 2 * 1024 * 1024;

export default function TenantMeterSubmissionCard({
  token,
  room,
  currentPeriod,
  currentPeriodHasReading,
  meterSubmission,
  onSubmitted,
}: TenantMeterSubmissionCardProps) {
  const { message } = App.useApp();
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const previousReading = room.previousReading ?? room.initialReading ?? 0;

  const [submittedReading, setSubmittedReading] = useState<number | null>(
    meterSubmission?.submittedReading ?? previousReading,
  );
  const [note, setNote] = useState(meterSubmission?.tenantNote ?? '');
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [isEditingPending, setIsEditingPending] = useState(false);

  useEffect(() => {
    if (meterSubmission?.submittedReading != null) {
      setSubmittedReading(meterSubmission.submittedReading);
    } else {
      setSubmittedReading(previousReading);
    }
    setNote(meterSubmission?.tenantNote ?? '');
  }, [meterSubmission?.submittedReading, meterSubmission?.tenantNote, previousReading]);

  useEffect(() => {
    if (!selectedFile) {
      setPreviewUrl(null);
      return;
    }
    const objectUrl = URL.createObjectURL(selectedFile);
    setPreviewUrl(objectUrl);
    return () => URL.revokeObjectURL(objectUrl);
  }, [selectedFile]);

  const isApprovedOrRecorded = Boolean(currentPeriodHasReading || meterSubmission?.status === 'Approved');
  const isPending = !isApprovedOrRecorded && meterSubmission?.status === 'Pending';
  const isRejected = !isApprovedOrRecorded && meterSubmission?.status === 'Rejected';
  const showForm = !isApprovedOrRecorded && (!isPending || isEditingPending);

  const handleSelectPhoto = (file: File | undefined, inputEl: HTMLInputElement | null) => {
    if (inputEl) inputEl.value = '';
    if (!file) return;
    setFileError(null);
    if (file.size > MAX_PHOTO_BYTES) {
      setFileError(t("Tệp chứng từ tối đa {0}.", ['2 MB']));
      setSelectedFile(null);
      return;
    }
    setSelectedFile(file);
  };

  const handleSubmit = async () => {
    setFileError(null);
    if (!selectedFile) {
      setFileError(`${''}Vui lòng chụp hoặc chọn ảnh đồng hồ điện.`);
      return;
    }
    if (submittedReading === null || Number.isNaN(submittedReading) || submittedReading < previousReading) {
      setFileError(`${t("Chỉ số điện mới")} >= ${previousReading} kWh`);
      return;
    }

    setSubmitting(true);
    try {
      await tenantApi.submitMeterReading(token, selectedFile, submittedReading, note.trim() || undefined, currentPeriod);
      setSelectedFile(null);
      setIsEditingPending(false);
      await onSubmitted();
      message.success(t("Đã gửi"));
    } catch (error) {
      const errMsg = error instanceof Error ? error.message : t("Yêu cầu chưa thành công. Vui lòng thử lại.");
      setFileError(errMsg);
    } finally {
      setSubmitting(false);
    }
  };

  const remotePhotoUrl = meterSubmission
    ? `${tenantApi.meterPhotoUrl(token, currentPeriod)}${currentPeriod ? '&' : '?'}v=${encodeURIComponent(meterSubmission.submittedAt)}`
    : null;

  return (
    <Card className="tenant-meter-submission-card" bordered={false} style={{ marginBottom: 16, overflow: 'hidden' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12, flexWrap: 'wrap', marginBottom: 12 }}>
        <div>
          <Space size={8} align="center">
            <ThunderboltOutlined style={{ fontSize: 18, color: '#0f766e' }} />
            <Typography.Title level={4} style={{ margin: 0 }}>
              {t("Chỉ số điện")} {currentPeriod ? `· ${periodLabel(currentPeriod)}` : ''}
            </Typography.Title>
          </Space>
          <div style={{ marginTop: 4 }}>
            <Typography.Text type="secondary">
              {t("Chỉ số đã chốt: {0} kWh · Giá điện hiện tại: {1}/kWh.", [
                String(previousReading),
                `${(room.electricityRate ?? 0).toLocaleString('vi-VN')} ₫`,
              ])}
            </Typography.Text>
          </div>
        </div>

        {isApprovedOrRecorded && (
          <Tag color="success" icon={<CheckCircleFilled />}>
            {t("Đã xác nhận")}
          </Tag>
        )}
        {isPending && (
          <Tag color="processing" icon={<ClockCircleOutlined />}>
            {t("Đang chờ đối soát")}
          </Tag>
        )}
        {isRejected && (
          <Tag color="error">
            {`${''}Cần chụp lại`}
          </Tag>
        )}
      </div>

      {isApprovedOrRecorded ? (
        <Alert
          type="success"
          showIcon
          message={t("Đã phát hành hóa đơn")}
          description={
            meterSubmission
              ? `${t("Chỉ số điện")}: ${meterSubmission.previousReading} → ${meterSubmission.approvedReading ?? meterSubmission.submittedReading} kWh`
              : `${t("Chỉ số điện")}: ${previousReading} kWh`
          }
        />
      ) : (
        <>
          {isRejected && (
            <Alert
              type="error"
              showIcon
              style={{ marginBottom: 12 }}
              message={`${''}Chủ trọ yêu cầu chụp và gửi lại ảnh đồng hồ điện`}
              description={meterSubmission?.rejectReason}
            />
          )}

          {isPending && meterSubmission && (
            <div style={{ padding: 12, borderRadius: 10, background: 'rgba(15, 118, 110, 0.05)', marginBottom: showForm ? 12 : 0 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12 }}>
                <div style={{ minWidth: 0, flex: 1 }}>
                  <Typography.Text strong>
                    {t("Chỉ số điện mới")}: {meterSubmission.previousReading} → {meterSubmission.submittedReading} kWh
                  </Typography.Text>
                  <div style={{ fontSize: 13, color: '#475569', marginTop: 2 }}>
                    {`${''}Đã gửi ảnh đồng hồ · Đang chờ chủ trọ kiểm tra và lập hóa đơn.`}
                  </div>
                  {meterSubmission.tenantNote && (
                    <div style={{ fontSize: 13, color: '#475569', marginTop: 2 }}>
                      {`${''}Ghi chú`}: {meterSubmission.tenantNote}
                    </div>
                  )}
                  <div style={{ marginTop: 8 }}>
                    <Button
                      size="small"
                      onClick={() => setIsEditingPending((prev) => !prev)}
                    >
                      {isEditingPending ? `${''}Ẩn biểu mẫu` : `${''}Gửi lại ảnh / Sửa chỉ số`}
                    </Button>
                  </div>
                </div>
                {remotePhotoUrl && (
                  <img
                    src={remotePhotoUrl}
                    alt="Meter"
                    style={{ width: 96, height: 72, objectFit: 'cover', borderRadius: 8, border: '1px solid rgba(15,23,42,0.12)', flexShrink: 0 }}
                  />
                )}
              </div>
            </div>
          )}

          {showForm && (
            <>
              {fileError && (
                <Alert type="warning" showIcon style={{ marginBottom: 12 }} message={fileError} />
              )}

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 10, alignItems: 'center' }}>
                <div>
                  <div style={{ fontSize: 12, marginBottom: 4, color: '#475569' }}>{t("Chỉ số điện mới")} (kWh)</div>
                  <InputNumber
                    style={{ width: '100%' }}
                    min={previousReading}
                    max={1000000000}
                    step={1}
                    precision={0}
                    value={submittedReading}
                    onChange={(val) => setSubmittedReading(typeof val === 'number' ? val : null)}
                  />
                </div>

                <div>
                  <div style={{ fontSize: 12, marginBottom: 4, color: '#475569' }}>{`${''}Ghi chú (không bắt buộc)`}</div>
                  <Input
                    value={note}
                    onChange={(e) => setNote(e.target.value)}
                    placeholder={`${''}Ví dụ: Chỉ số chốt ngày hôm nay...`}
                    maxLength={500}
                  />
                </div>
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10, marginTop: 12 }}>
                <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 8, minWidth: 0, maxWidth: '100%' }}>
                  <input
                    ref={fileInputRef}
                    type="file"
                    accept="image/jpeg,image/png,image/webp"
                    data-testid="tenant-meter-photo-input"
                    style={{ display: 'none' }}
                    onChange={(e) => handleSelectPhoto(e.target.files?.[0], e.currentTarget)}
                  />
                  <Button icon={<CameraOutlined />} onClick={() => fileInputRef.current?.click()}>
                    {selectedFile ? `${''}Chọn ảnh khác` : `${''}Chụp / Chọn ảnh đồng hồ điện`}
                  </Button>
                  {selectedFile && (
                    <span
                      title={selectedFile.name}
                      style={{
                        maxWidth: 200,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                        fontSize: 12,
                        color: '#475569',
                        display: 'inline-block',
                      }}
                    >
                      {selectedFile.name}
                    </span>
                  )}
                  {previewUrl && (
                    <img
                      src={previewUrl}
                      alt="Preview"
                      style={{ width: 56, height: 42, objectFit: 'cover', borderRadius: 6, border: '1px solid rgba(15,23,42,0.12)', flexShrink: 0 }}
                    />
                  )}
                </div>

                <Button
                  type="primary"
                  loading={submitting}
                  onClick={() => void handleSubmit()}
                >
                  {isPending ? t("Cập nhật") : t("Gửi ngay")}
                </Button>
              </div>
            </>
          )}
        </>
      )}
    </Card>
  );
}
