import { ArrowLeftOutlined, DeleteOutlined, FileTextOutlined, PlusOutlined, QrcodeOutlined, UserAddOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Col, ConfigProvider, DatePicker, Form, Grid, Input, InputNumber, Row, Select, Space, Spin, Tag, Typography } from 'antd';
import dayjs from 'dayjs';
import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { formatVnd } from '../../shared/format';
import { legalCt01Template } from '../../shared/i18n/legalTemplates';
import { dateFormat, t } from '../../shared/i18n/runtime';
import MoneyInput from '../../shared/ui/MoneyInput';
import { EmptyPanel, LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import CccdQrScannerModal from './CccdQrScannerModal';
import type { ParsedCccdQr } from './cccdQr';
import './RoomsPage.css';
import { roomsApi } from './api';
import { roomTheme } from './theme';
import type { CoResident, Room } from './types';

type QrScanTarget =
  | { kind: 'tenant' }
  | { kind: 'coResident-add' }
  | { kind: 'coResident-row'; index: number }
  | null;

interface CoResidentFormItem {
  fullName?: string;
  citizenId?: string;
  gender?: string;
  dateOfBirth?: dayjs.Dayjs | null;
  relation?: string;
}

interface TenantFormValues {
  tenantName: string;
  phone?: string;
  tenantCitizenId?: string;
  tenantDateOfBirth?: dayjs.Dayjs | null;
  tenantGender?: string;
  tenantCitizenIdIssuedDate?: dayjs.Dayjs | null;
  tenantCitizenIdIssuedPlace?: string;
  tenantPermanentAddress?: string;
  tenantEmail?: string;
  householdHeadRelation?: string;
  people: number;
  startDate?: dayjs.Dayjs;
  contractEndDate?: dayjs.Dayjs | null;
  paymentDueDay?: number;
  roomAreaSqm?: number | null;
  handoverAssets?: string;
  additionalTerms?: string;
  initialReading?: number;
  depositAmount?: number;
  coResidents?: CoResidentFormItem[];
}

function formatDayjsDate(val?: dayjs.Dayjs | null): string | null {
  return val && dayjs.isDayjs(val) ? val.format('YYYY-MM-DD') : null;
}

function normalizeCoResidents(raw?: CoResidentFormItem[]): CoResident[] {
  return (raw ?? [])
    .filter((item) => item && item.fullName && item.fullName.trim().length > 0)
    .map((item) => ({
      fullName: item.fullName!.trim(),
      citizenId: item.citizenId?.trim() || null,
      gender: item.gender || null,
      dateOfBirth: formatDayjsDate(item.dateOfBirth),
      relation: item.relation?.trim() || null,
    }));
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
  const watchedCoResidents = Form.useWatch('coResidents', form);
  const totalPeople = 1 + (watchedCoResidents?.length ?? 0);
  const openContractAfterSaveRef = useRef(false);
  const initializedOccupancyKeyRef = useRef<string | null>(null);
  const serverOccupancySignatureRef = useRef<string | null>(null);
  const [qrTarget, setQrTarget] = useState<QrScanTarget>(null);

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
        tenantCitizenId: room.occupancy.tenantCitizenId ?? '',
        tenantDateOfBirth: room.occupancy.tenantDateOfBirth ? dayjs(room.occupancy.tenantDateOfBirth) : null,
        tenantGender: room.occupancy.tenantGender ?? undefined,
        tenantCitizenIdIssuedDate: room.occupancy.tenantCitizenIdIssuedDate ? dayjs(room.occupancy.tenantCitizenIdIssuedDate) : null,
        tenantCitizenIdIssuedPlace: room.occupancy.tenantCitizenIdIssuedPlace ?? t("Cục Cảnh sát QLHC về TTXH"),
        tenantPermanentAddress: room.occupancy.tenantPermanentAddress ?? '',
        tenantEmail: room.occupancy.tenantEmail ?? '',
        householdHeadRelation: room.occupancy.householdHeadRelation ?? t("Người thuê trọ"),
        people: room.occupancy.people,
        contractEndDate: room.occupancy.contractEndDate ? dayjs(room.occupancy.contractEndDate) : null,
        paymentDueDay: room.occupancy.paymentDueDay ?? 5,
        roomAreaSqm: room.occupancy.roomAreaSqm ?? null,
        handoverAssets: room.occupancy.handoverAssets ?? '',
        additionalTerms: room.occupancy.additionalTerms ?? '',
        coResidents: (room.occupancy.coResidents ?? []).map((member) => ({
          fullName: member.fullName,
          citizenId: member.citizenId ?? '',
          gender: member.gender ?? undefined,
          dateOfBirth: member.dateOfBirth ? dayjs(member.dateOfBirth) : null,
          relation: member.relation ?? '',
        })),
      });
      if (hadPreviousInit && isServerUpdated) {
        message.info(t("Đã tự động đồng bộ thông tin người thuê mới nhất."));
      }
    } else {
      form.setFieldsValue({
        tenantName: '',
        phone: '',
        tenantCitizenId: '',
        tenantDateOfBirth: null,
        tenantGender: legalCt01Template.genderMale,
        tenantCitizenIdIssuedDate: null,
        tenantCitizenIdIssuedPlace: t("Cục Cảnh sát QLHC về TTXH"),
        tenantPermanentAddress: '',
        tenantEmail: '',
        householdHeadRelation: t("Người thuê trọ"),
        people: 1,
        startDate: dayjs(),
        contractEndDate: dayjs().add(1, 'year'),
        paymentDueDay: 5,
        roomAreaSqm: null,
        handoverAssets: '',
        additionalTerms: '',
        depositAmount: 0,
        initialReading: minReading,
        coResidents: [],
      });
    }
  }, [room, minReading, form, message]);

  const handleQrDetected = (parsed: ParsedCccdQr) => {
    if (!qrTarget) return;
    if (qrTarget.kind === 'tenant') {
      form.setFieldsValue({
        tenantName: parsed.fullName,
        tenantCitizenId: parsed.citizenId,
        tenantDateOfBirth: parsed.dateOfBirth ? dayjs(parsed.dateOfBirth) : form.getFieldValue('tenantDateOfBirth'),
        tenantGender: parsed.gender ?? form.getFieldValue('tenantGender'),
        tenantPermanentAddress: parsed.permanentAddress || form.getFieldValue('tenantPermanentAddress'),
        tenantCitizenIdIssuedDate: parsed.issuedDate ? dayjs(parsed.issuedDate) : form.getFieldValue('tenantCitizenIdIssuedDate'),
        tenantCitizenIdIssuedPlace: form.getFieldValue('tenantCitizenIdIssuedPlace') || t("Cục Cảnh sát QLHC về TTXH"),
      });
      message.success(t("Đã điền thông tin CCCD của {0}.", [parsed.fullName]));
      return;
    }

    const currentList: CoResidentFormItem[] = [...(form.getFieldValue('coResidents') ?? [])];
    const newItem: CoResidentFormItem = {
      fullName: parsed.fullName,
      citizenId: parsed.citizenId,
      gender: parsed.gender ?? legalCt01Template.genderMale,
      dateOfBirth: parsed.dateOfBirth ? dayjs(parsed.dateOfBirth) : null,
      relation: t("Người thuê trọ"),
    };

    if (qrTarget.kind === 'coResident-row') {
      const idx = qrTarget.index;
      if (idx >= 0 && idx < currentList.length) {
        currentList[idx] = {
          ...currentList[idx],
          ...newItem,
          relation: currentList[idx]?.relation || t("Người thuê trọ"),
        };
        form.setFieldValue('coResidents', currentList);
        message.success(t("Đã điền thông tin CCCD của {0}.", [parsed.fullName]));
      }
      return;
    }

    const existingIdx = currentList.findIndex(
      (item) => (item?.citizenId && item.citizenId.trim() === parsed.citizenId) || (!item?.fullName?.trim() && !item?.citizenId?.trim()),
    );
    if (existingIdx >= 0) {
      currentList[existingIdx] = {
        ...currentList[existingIdx],
        ...newItem,
        relation: currentList[existingIdx]?.relation || t("Người thuê trọ"),
      };
    } else if (currentList.length < 29) {
      currentList.push(newItem);
    }
    form.setFieldValue('coResidents', currentList);
    form.setFieldValue('people', Math.min(30, 1 + currentList.length));
    message.success(t("Đã điền thông tin CCCD của {0}.", [parsed.fullName]));
  };

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
    const coResidents = normalizeCoResidents(values.coResidents);
    const people = 1 + coResidents.length;
    const commonContractFields = {
      tenantCitizenId: values.tenantCitizenId?.trim() ?? '',
      tenantDateOfBirth: formatDayjsDate(values.tenantDateOfBirth),
      tenantGender: values.tenantGender?.trim() ?? '',
      tenantCitizenIdIssuedDate: formatDayjsDate(values.tenantCitizenIdIssuedDate),
      tenantCitizenIdIssuedPlace: values.tenantCitizenIdIssuedPlace?.trim() ?? '',
      tenantPermanentAddress: values.tenantPermanentAddress?.trim() ?? '',
      tenantEmail: values.tenantEmail?.trim() ?? '',
      householdHeadRelation: values.householdHeadRelation?.trim() ?? '',
      contractEndDate: formatDayjsDate(values.contractEndDate),
      paymentDueDay: Number(values.paymentDueDay || 5),
      roomAreaSqm: values.roomAreaSqm === null || values.roomAreaSqm === undefined ? null : Number(values.roomAreaSqm),
      handoverAssets: values.handoverAssets?.trim() ?? '',
      additionalTerms: values.additionalTerms?.trim() ?? '',
      coResidents,
    };

    if (room.occupancy) {
      editOccupancyMutation.mutate({
        roomId: room.id,
        body: {
          occupancyId: room.occupancy.id,
          tenantName: values.tenantName,
          phone: values.phone,
          people,
          ...commonContractFields,
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
          ...commonContractFields,
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
              <div className="room-tenant-card-header room-tenant-card-header-flex">
                <Typography.Title level={5} style={{ margin: 0 }}>
                  {t("1. Bên thuê phòng / Người kê khai CT01 (Bên B)")}
                </Typography.Title>
                <Button
                  type="primary"
                  ghost
                  icon={<QrcodeOutlined />}
                  onClick={() => setQrTarget({ kind: 'tenant' })}
                >
                  {t("Quét / Tải ảnh CCCD")}
                </Button>
              </div>
              <Row gutter={16}>
                <Col xs={24} md={14}>
                  <Form.Item
                    label={t("Tên người thuê")}
                    name="tenantName"
                    rules={[{ required: true, whitespace: true, max: 160, message: t("Vui lòng nhập tên người thuê.") }]}
                  >
                    <Input size="large" placeholder={t("Họ và tên")} maxLength={160} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={10}>
                  <Form.Item label={t("Số điện thoại")} name="phone">
                    <Input size="large" inputMode="tel" placeholder="09xx xxx xxx" maxLength={60} />
                  </Form.Item>
                </Col>
              </Row>

              <Row gutter={16}>
                <Col xs={24} md={14}>
                  <Form.Item
                    label={t("Số CCCD / Định danh cá nhân (12 số)")}
                    name="tenantCitizenId"
                    rules={[{ pattern: /^[0-9]{9,12}$/, message: t("Nhập từ 9 đến 12 chữ số.") }]}
                  >
                    <Input size="large" inputMode="numeric" placeholder={t("Số CCCD")} maxLength={12} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={10}>
                  <Form.Item label={t("Giới tính")} name="tenantGender">
                    <Select
                      size="large"
                      allowClear
                      placeholder={t("Chọn")}
                      options={[
                        { value: legalCt01Template.genderMale, label: t("Nam") },
                        { value: legalCt01Template.genderFemale, label: t("Nữ") },
                      ]}
                    />
                  </Form.Item>
                </Col>
              </Row>

              <Row gutter={16}>
                <Col xs={24} sm={12} md={12}>
                  <Form.Item label={t("Ngày sinh")} name="tenantDateOfBirth">
                    <DatePicker size="large" format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
                  </Form.Item>
                </Col>
                <Col xs={24} sm={12} md={12}>
                  <Form.Item label={t("Ngày cấp CCCD")} name="tenantCitizenIdIssuedDate">
                    <DatePicker size="large" format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
                  </Form.Item>
                </Col>
              </Row>

              <Row gutter={16}>
                <Col xs={24} md={14}>
                  <Form.Item label={t("Nơi cấp CCCD")} name="tenantCitizenIdIssuedPlace">
                    <Input size="large" maxLength={200} placeholder={t("Cục Cảnh sát QLHC về TTXH")} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={10}>
                  <Form.Item label={t("Mối quan hệ với chủ hộ")} name="householdHeadRelation">
                    <Input size="large" maxLength={100} placeholder={t("Người thuê trọ")} />
                  </Form.Item>
                </Col>
              </Row>

              <Row gutter={16}>
                <Col xs={24} md={14}>
                  <Form.Item label={t("Địa chỉ thường trú")} name="tenantPermanentAddress">
                    <Input size="large" maxLength={500} placeholder={t("Số nhà, đường, xã/phường, quận/huyện, tỉnh/thành phố")} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={10}>
                  <Form.Item label={t("Email liên hệ (nếu có)")} name="tenantEmail">
                    <Input size="large" maxLength={320} placeholder="email@example.com" />
                  </Form.Item>
                </Col>
              </Row>
            </SectionCard>

            <SectionCard className="room-tenant-card">
              <Form.List name="coResidents">
                {(fields, { add, remove }) => (
                  <>
                    <div className="room-tenant-coresidents-header">
                      <div>
                        <Typography.Title level={5} style={{ margin: 0 }}>
                          {t("Thành viên cùng thay đổi cư trú / ở cùng phòng")}
                        </Typography.Title>
                      </div>
                      {fields.length > 0 && (
                        <Space wrap>
                          <Button
                            icon={<QrcodeOutlined />}
                            disabled={fields.length >= 29}
                            onClick={() => setQrTarget({ kind: 'coResident-add' })}
                          >
                            {t("Quét CCCD thêm thành viên")}
                          </Button>
                          <Button
                            type="primary"
                            ghost
                            icon={<PlusOutlined />}
                            disabled={fields.length >= 29}
                            onClick={() => {
                              const nextCount = Math.min(30, Math.max(Number(form.getFieldValue('people') || 1), 1 + fields.length + 1));
                              add({ fullName: '', citizenId: '', gender: legalCt01Template.genderMale, dateOfBirth: null, relation: t("Người thuê trọ") });
                              form.setFieldValue('people', nextCount);
                            }}
                          >
                            {t("Thêm thành viên ở cùng")}
                          </Button>
                        </Space>
                      )}
                    </div>

                    {fields.length === 0 ? (
                      <div className="room-tenant-coresidents-empty">
                        <UserAddOutlined className="room-tenant-coresidents-empty-icon" />
                        <Space wrap>
                          <Button
                            icon={<QrcodeOutlined />}
                            onClick={() => setQrTarget({ kind: 'coResident-add' })}
                          >
                            {t("Quét CCCD thêm thành viên")}
                          </Button>
                          <Button
                            type="dashed"
                            icon={<PlusOutlined />}
                            onClick={() => {
                              const nextCount = Math.min(30, Math.max(Number(form.getFieldValue('people') || 1), 1 + fields.length + 1));
                              add({ fullName: '', citizenId: '', gender: legalCt01Template.genderMale, dateOfBirth: null, relation: t("Người thuê trọ") });
                              form.setFieldValue('people', nextCount);
                            }}
                          >
                            {t("Thêm thành viên ở cùng")}
                          </Button>
                        </Space>
                      </div>
                    ) : (
                      <div className="room-tenant-coresidents-list">
                        {fields.map((field, index) => (
                          <div key={field.key} className="room-tenant-coresident-row">
                            <div className="room-tenant-coresident-row-head" style={{ flexWrap: 'wrap', gap: 8 }}>
                              <Tag color="cyan">#{index + 1}</Tag>
                              <Space size={4} wrap>
                                <Button
                                  type="text"
                                  size="small"
                                  icon={<QrcodeOutlined />}
                                  onClick={() => setQrTarget({ kind: 'coResident-row', index })}
                                >
                                  {t("Quét / Tải ảnh CCCD")}
                                </Button>
                                <Button
                                  danger
                                  type="text"
                                  size="small"
                                  icon={<DeleteOutlined />}
                                  onClick={() => {
                                    const nextCount = Math.max(1, 1 + fields.length - 1);
                                    remove(field.name);
                                    form.setFieldValue('people', nextCount);
                                  }}
                                  aria-label={t("Xóa thành viên")}
                                >
                                  {t("Xóa thành viên")}
                                </Button>
                              </Space>
                            </div>
                            <Row gutter={12}>
                              <Col xs={24} md={14}>
                                <Form.Item
                                  label={t("Họ và tên")}
                                  name={[field.name, 'fullName']}
                                  rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên người thuê.") }]}
                                >
                                  <Input placeholder={t("Họ và tên")} maxLength={160} />
                                </Form.Item>
                              </Col>
                              <Col xs={24} md={10}>
                                <Form.Item
                                  label={t("Số CCCD")}
                                  name={[field.name, 'citizenId']}
                                  rules={[{ pattern: /^[0-9]{9,12}$/, message: t("Nhập từ 9 đến 12 chữ số.") }]}
                                >
                                  <Input placeholder={t("Số CCCD")} maxLength={12} />
                                </Form.Item>
                              </Col>
                            </Row>
                            <Row gutter={12}>
                              <Col xs={24} sm={12} md={8}>
                                <Form.Item label={t("Ngày sinh")} name={[field.name, 'dateOfBirth']}>
                                  <DatePicker format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
                                </Form.Item>
                              </Col>
                              <Col xs={24} sm={12} md={6}>
                                <Form.Item label={t("Giới tính")} name={[field.name, 'gender']}>
                                  <Select
                                    allowClear
                                    placeholder={t("Chọn")}
                                    options={[
                                      { value: legalCt01Template.genderMale, label: t("Nam") },
                                      { value: legalCt01Template.genderFemale, label: t("Nữ") },
                                    ]}
                                  />
                                </Form.Item>
                              </Col>
                              <Col xs={24} md={10}>
                                <Form.Item label={t("Mối quan hệ với chủ hộ")} name={[field.name, 'relation']}>
                                  <Input placeholder={t("Người thuê trọ")} maxLength={100} />
                                </Form.Item>
                              </Col>
                            </Row>
                          </div>
                        ))}
                      </div>
                    )}
                  </>
                )}
              </Form.List>
            </SectionCard>

            <SectionCard className="room-tenant-card">
              <div className="room-tenant-card-header">
                <Typography.Title level={5} style={{ margin: 0 }}>
                  {t("3. Điều khoản Hợp đồng & Tài sản bàn giao")}
                </Typography.Title>
              </div>
              <Row gutter={16}>
                <Col xs={24} sm={12} md={9}>
                  <Form.Item label={t("Ngày kết thúc hợp đồng")} name="contractEndDate">
                    <DatePicker size="large" format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
                  </Form.Item>
                </Col>
                <Col xs={24} sm={12} md={8}>
                  <Form.Item label={t("Hạn đóng tiền (ngày)")} name="paymentDueDay">
                    <InputNumber size="large" min={1} max={28} precision={0} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={7}>
                  <Form.Item label={t("Diện tích phòng")} name="roomAreaSqm">
                    <InputNumber size="large" min={0} max={10000} step={1} addonAfter="m²" style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
              </Row>
              <Form.Item label={t("Danh mục tài sản bàn giao")} name="handoverAssets">
                <Input.TextArea
                  rows={2}
                  maxLength={2000}
                  placeholder={t("Ví dụ: 01 máy lạnh, 01 bình nóng lạnh, 01 giường, 01 tủ quần áo, 02 chìa khóa phòng…")}
                />
              </Form.Item>
              <Form.Item label={t("Thỏa thuận / nội quy bổ sung (nếu có)")} name="additionalTerms" style={{ marginBottom: 0 }}>
                <Input.TextArea
                  rows={2}
                  maxLength={2000}
                  placeholder={t("Ví dụ: Giờ giấc tự do, không nuôi thú cưng, báo trước 30 ngày khi trả phòng…")}
                />
              </Form.Item>
            </SectionCard>
          </div>

          <div className="room-tenant-side-column">
            <SectionCard className="room-tenant-card room-tenant-summary-card">
              {!isEditing && (
                <>
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
                      step={0.001}
                      precision={3}
                      addonAfter="kWh"
                      style={{ width: '100%' }}
                    />
                  </Form.Item>
                  <Form.Item
                    label={t("Tiền cọc yêu cầu")}
                    name="depositAmount"
                    extra={t("Nhập 0 nếu không thu cọc. Đây là khoản phải thu, chưa ghi nhận khách đã trả.")}
                    rules={[{ required: true, type: 'number', min: 0, max: 2147483647, message: t("Nhập số tiền cọc hợp lệ.") }]}
                  >
                    <MoneyInput size="large" min={0} max={2147483647} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} />
                  </Form.Item>
                </>
              )}

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

              <Space direction="vertical" size={10} style={{ width: '100%', marginTop: 18 }}>
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
            icon={<QrcodeOutlined />}
            aria-label={t("Quét / Tải ảnh CCCD")}
            title={t("Quét / Tải ảnh CCCD")}
            disabled={isSaving}
            onClick={() => setQrTarget({ kind: 'tenant' })}
          />
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
      <CccdQrScannerModal
        open={qrTarget !== null}
        title={
          qrTarget?.kind === 'tenant'
            ? t("Quét mã QR CCCD người thuê")
            : t("Quét mã QR CCCD thành viên ở cùng")
        }
        onClose={() => setQrTarget(null)}
        onDetected={handleQrDetected}
      />
    </div>
  );
}
