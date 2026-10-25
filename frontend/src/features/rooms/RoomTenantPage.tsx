import { ArrowLeftOutlined, FileTextOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Col, ConfigProvider, DatePicker, Form, Grid, Input, InputNumber, Row, Space, Spin, Typography } from 'antd';
import dayjs from 'dayjs';
import { useEffect, useRef } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { formatVnd } from '../../shared/format';
import { dateFormat, t } from '../../shared/i18n/runtime';
import MoneyInput from '../../shared/ui/MoneyInput';
import { EmptyPanel, LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import './RoomsPage.css';
import { roomsApi } from './api';
import { roomTheme } from './theme';
import type { Room } from './types';

interface TenantFormValues {
  tenantName: string;
  phone?: string;
  people: number;
  startDate?: dayjs.Dayjs;
  initialReading?: number;
  depositAmount?: number;
}

export default function RoomTenantPage() {
  const screens = Grid.useBreakpoint();
  return (
    <ConfigProvider theme={roomTheme(!screens.md)}>
      <App component={false}>
        <RoomTenantWorkspace />
      </App>
    </ConfigProvider>
  );
}

function RoomTenantWorkspace() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const client = useQueryClient();
  const [form] = Form.useForm<TenantFormValues>();
  const watchedPeople = Form.useWatch('people', form);
  const totalPeople = Math.max(1, Number(watchedPeople || 1));
  const openContractAfterSaveRef = useRef(false);
  const initializedOccupancyKeyRef = useRef<string | null>(null);
  const serverOccupancySignatureRef = useRef<string | null>(null);

  const roomsQuery = useQuery({
    queryKey: ['rooms', 'archive-view'],
    queryFn: () => roomsApi.list(undefined, true),
    refetchOnWindowFocus: true,
    refetchInterval: 4000,
    refetchIntervalInBackground: false,
  });

  const room: Room | undefined = (roomsQuery.data ?? []).find((item) => item.id === id);
  const isEditing = !!room?.occupancy;
  const minReading = room?.previousReading ?? room?.initialReading ?? 0;

  useEffect(() => {
    if (!room) return;
    const currentKey = `${room.id}:${room.occupancy?.id ?? 'new'}`;
    const nextSignature = JSON.stringify(room.occupancy ?? null);
    const isInitialForRoom = initializedOccupancyKeyRef.current !== currentKey;
    const isServerUpdated = serverOccupancySignatureRef.current !== null && serverOccupancySignatureRef.current !== nextSignature;

    if (!isInitialForRoom && !isServerUpdated) return;
    const hadPreviousInit = initializedOccupancyKeyRef.current !== null;
    initializedOccupancyKeyRef.current = currentKey;
    serverOccupancySignatureRef.current = nextSignature;

    if (room.occupancy) {
      form.setFieldsValue({
        tenantName: room.occupancy.tenantName,
        phone: room.occupancy.phone ?? '',
        people: room.occupancy.people,
        initialReading: room.initialReading ?? room.previousReading ?? 0,
      });
      if (hadPreviousInit && isServerUpdated) {
        message.info(t("Đã tự động đồng bộ thông tin người thuê mới nhất."));
      }
    } else {
      form.setFieldsValue({
        tenantName: '',
        phone: '',
        people: 1,
        startDate: dayjs(),
        depositAmount: 0,
        initialReading: minReading,
      });
    }
  }, [room, minReading, form, message]);

  const refreshData = async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: ['rooms'] }),
      client.invalidateQueries({ queryKey: ['dashboard'] }),
      client.invalidateQueries({ queryKey: ['buildings'] }),
      client.invalidateQueries({ queryKey: ['invoices'] }),
      client.invalidateQueries({ queryKey: ['subscription'] }),
      client.invalidateQueries({ queryKey: ['readings'] }),
    ]);
  };

  const handleAfterSuccess = async (roomId: string, successText: string) => {
    await refreshData();
    message.success(successText);
    if (openContractAfterSaveRef.current) {
      openContractAfterSaveRef.current = false;
      navigate(`/rooms/${encodeURIComponent(roomId)}/documents`);
    } else {
      navigate('/rooms');
    }
  };

  const startOccupancyMutation = useMutation({
    mutationFn: ({ roomId, body }: { roomId: string; body: Parameters<typeof roomsApi.startOccupancy>[1] }) =>
      roomsApi.startOccupancy(roomId, body),
    onSuccess: async (_, vars) => {
      await handleAfterSuccess(vars.roomId, t("Đã ghi nhận người thuê và khoản cọc nếu có."));
    },
    onError: (err: Error) => {
      openContractAfterSaveRef.current = false;
      message.error(err.message);
    },
  });

  const editOccupancyMutation = useMutation({
    mutationFn: ({ roomId, body }: { roomId: string; body: Parameters<typeof roomsApi.editOccupancy>[1] }) =>
      roomsApi.editOccupancy(roomId, body),
    onSuccess: async (_, vars) => {
      await handleAfterSuccess(vars.roomId, t("Đã cập nhật người thuê."));
    },
    onError: (err: Error) => {
      openContractAfterSaveRef.current = false;
      message.error(err.message);
    },
  });

  const isSaving = startOccupancyMutation.isPending || editOccupancyMutation.isPending;

  const onFinish = (values: TenantFormValues) => {
    if (!room || isSaving) return;
    const people = Math.min(30, Math.max(1, Number(values.people || 1)));

    if (room.occupancy) {
      editOccupancyMutation.mutate({
        roomId: room.id,
        body: {
          occupancyId: room.occupancy.id,
          tenantName: values.tenantName,
          phone: values.phone,
          people,
          ...(values.initialReading != null && values.initialReading !== room.initialReading ? { initialReading: values.initialReading } : {}),
        },
      });
    } else {
      startOccupancyMutation.mutate({
        roomId: room.id,
        body: {
          tenantName: values.tenantName,
          phone: values.phone,
          people,
          startDate: (values.startDate ?? dayjs()).format('YYYY-MM-DD'),
          depositAmount: Number(values.depositAmount ?? 0),
          initialReading: values.initialReading,
        },
      });
    }
  };

  if (roomsQuery.isError) {
    return <LoadError error={roomsQuery.error} onRetry={() => void roomsQuery.refetch()} />;
  }

  if (roomsQuery.isPending) {
    return (
      <div className="screen-center">
        <Spin size="large" />
      </div>
    );
  }

  if (!room) {
    return (
      <div className="rooms-workspace">
        <EmptyPanel
          title={t("Không tìm thấy phòng phù hợp")}
          description={t("Không có phòng khớp với bộ lọc hiện tại.")}
          action={<Button onClick={() => navigate('/rooms')}>{t("Phòng & người thuê")}</Button>}
        />
      </div>
    );
  }

  return (
    <div className="rooms-workspace room-tenant-page">
      <div style={{ marginBottom: 8 }}>
        <Button type="link" icon={<ArrowLeftOutlined />} style={{ paddingLeft: 0 }} onClick={() => navigate('/rooms')}>
          {t("Phòng & người thuê")}
        </Button>
      </div>
      <PageTitle
        title={isEditing ? `${t("Sửa thông tin người thuê")} · ${room.name}` : t("Thêm người thuê · {0}", [room.name])}
        subtitle={
          isEditing
            ? t("Số người mới áp dụng cho hóa đơn nháp chưa thu tiền và kỳ chưa lập hóa đơn. Hóa đơn đã phát hành giữ nguyên.")
            : t("Nhập tiền cọc thỏa thuận riêng với khách. Khoản cọc được tạo riêng và thanh toán qua link khách sau khi lưu.")
        }
        extra={
          <Space wrap>
            <Button onClick={() => navigate('/rooms')} disabled={isSaving}>
              {t("Hủy")}
            </Button>
            <Button
              icon={<FileTextOutlined />}
              loading={isSaving && openContractAfterSaveRef.current}
              disabled={isSaving}
              onClick={() => {
                openContractAfterSaveRef.current = true;
                form.submit();
              }}
            >
              {t("Xuất hợp đồng & CT01")}
            </Button>
            <Button
              type="primary"
              loading={isSaving && !openContractAfterSaveRef.current}
              disabled={isSaving}
              onClick={() => {
                openContractAfterSaveRef.current = false;
                form.submit();
              }}
            >
              {isEditing ? t("Lưu thay đổi") : t("Lưu người thuê")}
            </Button>
          </Space>
        }
      />

      <Form form={form} layout="vertical" requiredMark={false} onFinish={onFinish}>
        <div className="room-tenant-workspace-grid">
          <div className="room-tenant-main-column">
            <SectionCard className="room-tenant-card">
              <div className="room-tenant-card-header">
                <Typography.Title level={5} style={{ margin: 0 }}>
                  {isEditing ? t("Sửa thông tin người thuê") : t("Thêm người thuê")}
                </Typography.Title>
              </div>
              <Row gutter={16}>
                <Col xs={24} md={10}>
                  <Form.Item
                    label={t("Tên người thuê")}
                    name="tenantName"
                    rules={[{ required: true, whitespace: true, max: 160, message: t("Vui lòng nhập tên người thuê.") }]}
                  >
                    <Input size="large" placeholder={t("Họ và tên")} maxLength={160} />
                  </Form.Item>
                </Col>
                <Col xs={24} sm={14} md={8}>
                  <Form.Item label={t("Số điện thoại")} name="phone">
                    <Input size="large" inputMode="tel" placeholder="09xx xxx xxx" maxLength={60} />
                  </Form.Item>
                </Col>
                <Col xs={24} sm={10} md={6}>
                  <Form.Item
                    label={t("Số người ở")}
                    name="people"
                    rules={[{ required: true, type: 'number', min: 1, max: 30, message: t("Nhập từ 1 đến 30 người.") }]}
                  >
                    <InputNumber size="large" min={1} max={30} precision={0} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
              </Row>

              {isEditing ? (
                <Row gutter={16}>
                  <Col xs={24} sm={12} md={8}>
                    <Form.Item
                      label={t("Chỉ số điện ban đầu")}
                      name="initialReading"
                      rules={[
                        { required: true, message: t("Vui lòng nhập giá trị.") },
                        { type: 'number', min: 0, max: 1000000000, message: t("Nhập chỉ số từ {0} trở lên.", [0]) },
                      ]}
                    >
                      <InputNumber
                        size="large"
                        min={0}
                        max={1000000000}
                        step={1}
                        precision={0}
                        addonAfter="kWh"
                        style={{ width: '100%' }}
                      />
                    </Form.Item>
                  </Col>
                </Row>
              ) : (
                <Row gutter={16}>
                  <Col xs={24} sm={12} md={8}>
                    <Form.Item
                      label={t("Ngày chuyển vào")}
                      name="startDate"
                      rules={[{ required: true, message: t("Vui lòng chọn ngày.") }]}
                    >
                      <DatePicker
                        size="large"
                        format={dateFormat()}
                        disabledDate={(date) => date.isAfter(dayjs(), 'day')}
                        style={{ width: '100%' }}
                      />
                    </Form.Item>
                  </Col>
                  <Col xs={24} sm={12} md={8}>
                    <Form.Item
                      label={t("Chỉ số điện ban đầu")}
                      name="initialReading"
                      rules={[
                        { required: true, message: t("Vui lòng nhập giá trị.") },
                        { type: 'number', min: minReading, max: 1000000000, message: t("Nhập chỉ số từ {0} trở lên.", [minReading]) },
                      ]}
                    >
                      <InputNumber
                        size="large"
                        min={minReading}
                        max={1000000000}
                        step={1}
                        precision={0}
                        addonAfter="kWh"
                        style={{ width: '100%' }}
                      />
                    </Form.Item>
                  </Col>
                  <Col xs={24} md={8}>
                    <Form.Item
                      label={t("Tiền cọc yêu cầu")}
                      name="depositAmount"
                      extra={t("Nhập 0 nếu không thu cọc. Đây là khoản phải thu, chưa ghi nhận khách đã trả.")}
                      rules={[{ required: true, type: 'number', min: 0, max: 2147483647, message: t("Nhập số tiền cọc hợp lệ.") }]}
                    >
                      <MoneyInput size="large" min={0} max={2147483647} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} />
                    </Form.Item>
                  </Col>
                </Row>
              )}

              <Alert
                type="info"
                showIcon
                style={{ marginTop: 8 }}
                message={`${''}Thông tin chi tiết để làm Hợp đồng & Tờ khai CT01 (CCCD / Hộ chiếu, thành viên ở cùng, tài sản bàn giao) sẽ được nhập hoặc quét ảnh tại mục "Xuất hợp đồng & CT01".`}
              />
            </SectionCard>
          </div>

          <div className="room-tenant-side-column">
            <SectionCard className="room-tenant-card room-tenant-summary-card">
              <div className="room-tenant-fee-box">
                <div className="room-tenant-fee-item">
                  <span>{t("Phòng")}</span>
                  <strong>{room.name}</strong>
                </div>
                {room.buildingName && (
                  <div className="room-tenant-fee-item">
                    <span>{t("Dãy trọ")}</span>
                    <strong>{room.buildingName}</strong>
                  </div>
                )}
                <div className="room-tenant-fee-item">
                  <span>{t("Số người ở")}</span>
                  <strong>{totalPeople} {t("người")}</strong>
                </div>
                <div className="room-tenant-fee-item">
                  <span>{t("Giá thuê / tháng")}</span>
                  <strong>{formatVnd(room.rent)}</strong>
                </div>
                <div className="room-tenant-fee-item">
                  <span>{t("Giá điện / kWh")}</span>
                  <strong>{formatVnd(room.electricityRate)}</strong>
                </div>
                <div className="room-tenant-fee-item">
                  <span>{t("Tiền nước")}</span>
                  <strong>
                    {formatVnd(room.waterFee)} ({room.waterPerPerson ? t("Theo người / tháng") : t("Theo phòng / tháng")})
                  </strong>
                </div>
                <div className="room-tenant-fee-item">
                  <span>{t("Phí dịch vụ / tháng")}</span>
                  <strong>{formatVnd(room.serviceFee)}</strong>
                </div>
              </div>

              <Space className="room-tenant-summary-actions" direction="vertical" size={10} style={{ width: '100%', marginTop: 18 }}>
                <Button
                  type="primary"
                  size="large"
                  block
                  loading={isSaving && !openContractAfterSaveRef.current}
                  disabled={isSaving}
                  onClick={() => {
                    openContractAfterSaveRef.current = false;
                    form.submit();
                  }}
                >
                  {isEditing ? t("Lưu thay đổi") : t("Lưu người thuê")}
                </Button>
                <Button
                  size="large"
                  block
                  icon={<FileTextOutlined />}
                  loading={isSaving && openContractAfterSaveRef.current}
                  disabled={isSaving}
                  onClick={() => {
                    openContractAfterSaveRef.current = true;
                    form.submit();
                  }}
                >
                  {t("Xuất hợp đồng & CT01")}
                </Button>
              </Space>
            </SectionCard>
          </div>
        </div>
        <div className="room-tenant-mobile-bar" role="region" aria-label={t("Thao tác nhanh")}>
          <Button
            className="room-tenant-mobile-qr-btn"
            icon={<FileTextOutlined />}
            aria-label={t("Xuất hợp đồng & CT01")}
            title={t("Xuất hợp đồng & CT01")}
            loading={isSaving && openContractAfterSaveRef.current}
            disabled={isSaving}
            onClick={() => {
              openContractAfterSaveRef.current = true;
              form.submit();
            }}
          >
            {t("Xuất hợp đồng & CT01")}
          </Button>
          <Button
            type="primary"
            className="room-tenant-mobile-save-btn"
            loading={isSaving && !openContractAfterSaveRef.current}
            disabled={isSaving}
            onClick={() => {
              openContractAfterSaveRef.current = false;
              form.submit();
            }}
          >
            {isEditing ? t("Lưu thay đổi") : t("Lưu người thuê")}
          </Button>
        </div>
      </Form>
    </div>
  );
}
