import { ArrowLeftOutlined, DeleteOutlined, DownloadOutlined, FileTextOutlined, PlusOutlined, PrinterOutlined, QrcodeOutlined, SaveOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Col, Collapse, DatePicker, Drawer, Form, Input, InputNumber, Row, Segmented, Select, Space, Spin, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useEffect, useMemo, useRef, useState } from 'react';
import { formatVnd } from '../../shared/format';
import { legalContractTemplate, legalCt01Template } from '../../shared/i18n/legalTemplates';
import { dateFormat, t } from '../../shared/i18n/runtime';
import { LoadError, PageTitle } from '../../shared/ui/Ui';
import CccdQrScannerModal from './CccdQrScannerModal';
import type { ParsedCccdQr } from './cccdQr';
import { roomsApi } from './api';
import type { OccupancyDocumentData, Room, UpdateOccupancyDocumentRequest } from './types';

type DocumentTab = 'contract' | 'ct01';

type DocQrTarget =
  | { kind: 'tenant' }
  | { kind: 'owner' }
  | { kind: 'coResident-add' }
  | { kind: 'coResident-row'; index: number }
  | null;

interface CoResidentFormItem {
  fullName: string;
  dateOfBirth?: Dayjs | null;
  gender?: string | null;
  citizenId?: string | null;
  relation?: string | null;
}

interface DocumentFormValues {
  ownerPhone?: string;
  ownerCitizenId?: string;
  ownerDateOfBirth?: Dayjs | null;
  ownerCitizenIdIssuedDate?: Dayjs | null;
  ownerCitizenIdIssuedPlace?: string;
  ownerPermanentAddress?: string;
  buildingAddress?: string;
  policeStationName?: string;
  tenantName?: string;
  tenantPhone?: string;
  tenantEmail?: string;
  tenantCitizenId?: string;
  tenantDateOfBirth?: Dayjs | null;
  tenantGender?: string;
  tenantCitizenIdIssuedDate?: Dayjs | null;
  tenantCitizenIdIssuedPlace?: string;
  tenantPermanentAddress?: string;
  contractEndDate?: Dayjs | null;
  paymentDueDay?: number;
  roomAreaSqm?: number | null;
  handoverAssets?: string;
  additionalTerms?: string;
  householdHeadRelation?: string;
  coResidents?: CoResidentFormItem[];
}

function toDayjs(value?: string | null): Dayjs | null {
  if (!value) return null;
  const parsed = dayjs(value);
  return parsed.isValid() ? parsed : null;
}

function formatDateDisplay(value?: Dayjs | string | null): string {
  if (!value) return '....../....../............';
  const d = typeof value === 'string' ? dayjs(value) : value;
  return d.isValid() ? d.format('DD/MM/YYYY') : '....../....../............';
}

function buildCitizenBoxes(citizenId?: string | null): string[] {
  const cleaned = (citizenId ?? '').trim();
  return Array.from({ length: 12 }, (_, idx) => cleaned[idx] ?? '');
}

export default function ContractDocumentsDrawer({
  room,
  roomId: propRoomId,
  initialTab = 'contract',
  asPage = false,
  onClose,
}: {
  room?: Room;
  roomId?: string;
  initialTab?: DocumentTab;
  asPage?: boolean;
  onClose: () => void;
}) {
  const targetRoomId = room?.id ?? propRoomId ?? '';
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState<DocumentTab>(initialTab);
  const [form] = Form.useForm<DocumentFormValues>();
  const watchedValues = Form.useWatch([], form);
  const previewRef = useRef<HTMLDivElement | null>(null);
  const initializedOccupancyId = useRef<string | null>(null);
  const serverDocSignatureRef = useRef<string | null>(null);
  const [isDownloadingDocx, setIsDownloadingDocx] = useState(false);
  const [qrTarget, setQrTarget] = useState<DocQrTarget>(null);

  const query = useQuery({
    queryKey: ['room-documents', targetRoomId],
    queryFn: () => roomsApi.getDocuments(targetRoomId),
    enabled: !!targetRoomId,
    refetchOnWindowFocus: true,
    refetchInterval: 4000,
    refetchIntervalInBackground: false,
  });

  const data = query.data;
  const displayRoomName = data?.roomName ?? room?.name ?? '';

  useEffect(() => {
    if (!data) return;
    const nextSignature = JSON.stringify(data);
    const isInitialForOccupancy = initializedOccupancyId.current !== data.occupancyId;
    const isServerUpdated = serverDocSignatureRef.current !== null && serverDocSignatureRef.current !== nextSignature;
    if (!isInitialForOccupancy && !isServerUpdated) return;

    const hadPreviousInit = initializedOccupancyId.current !== null;
    initializedOccupancyId.current = data.occupancyId;
    serverDocSignatureRef.current = nextSignature;
    form.setFieldsValue({
      ownerPhone: data.ownerPhone ?? '',
      ownerCitizenId: data.ownerCitizenId ?? '',
      ownerDateOfBirth: toDayjs(data.ownerDateOfBirth),
      ownerCitizenIdIssuedDate: toDayjs(data.ownerCitizenIdIssuedDate),
      ownerCitizenIdIssuedPlace: data.ownerCitizenIdIssuedPlace ?? '',
      ownerPermanentAddress: data.ownerPermanentAddress ?? '',
      buildingAddress: data.buildingAddress ?? '',
      policeStationName: data.policeStationName ?? '',
      tenantName: data.tenantName ?? '',
      tenantPhone: data.tenantPhone ?? '',
      tenantEmail: data.tenantEmail ?? '',
      tenantCitizenId: data.tenantCitizenId ?? '',
      tenantDateOfBirth: toDayjs(data.tenantDateOfBirth),
      tenantGender: data.tenantGender ?? undefined,
      tenantCitizenIdIssuedDate: toDayjs(data.tenantCitizenIdIssuedDate),
      tenantCitizenIdIssuedPlace: data.tenantCitizenIdIssuedPlace ?? '',
      tenantPermanentAddress: data.tenantPermanentAddress ?? '',
      contractEndDate: toDayjs(data.contractEndDate),
      paymentDueDay: data.paymentDueDay ?? 5,
      roomAreaSqm: data.roomAreaSqm ?? null,
      handoverAssets: data.handoverAssets ?? '',
      additionalTerms: data.additionalTerms ?? '',
      householdHeadRelation: data.householdHeadRelation || legalCt01Template.defaultRelation,
      coResidents: (data.coResidents ?? []).map((item) => ({
        fullName: item.fullName,
        dateOfBirth: toDayjs(item.dateOfBirth),
        gender: item.gender ?? undefined,
        citizenId: item.citizenId ?? '',
        relation: item.relation ?? '',
      })),
    });
    if (hadPreviousInit && isServerUpdated) {
      message.info(t("Đã tự động đồng bộ thông tin người thuê mới nhất."));
    }
  }, [data, form, message]);

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

    if (qrTarget.kind === 'owner') {
      form.setFieldsValue({
        ownerCitizenId: parsed.citizenId,
        ownerDateOfBirth: parsed.dateOfBirth ? dayjs(parsed.dateOfBirth) : form.getFieldValue('ownerDateOfBirth'),
        ownerPermanentAddress: parsed.permanentAddress || form.getFieldValue('ownerPermanentAddress'),
        ownerCitizenIdIssuedDate: parsed.issuedDate ? dayjs(parsed.issuedDate) : form.getFieldValue('ownerCitizenIdIssuedDate'),
        ownerCitizenIdIssuedPlace: form.getFieldValue('ownerCitizenIdIssuedPlace') || t("Cục Cảnh sát QLHC về TTXH"),
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
      relation: legalCt01Template.defaultRelation,
    };

    if (qrTarget.kind === 'coResident-row') {
      const idx = qrTarget.index;
      if (idx >= 0 && idx < currentList.length) {
        currentList[idx] = {
          ...currentList[idx],
          ...newItem,
          relation: currentList[idx]?.relation || legalCt01Template.defaultRelation,
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
        relation: currentList[existingIdx]?.relation || legalCt01Template.defaultRelation,
      };
    } else if (currentList.length < 29) {
      currentList.push(newItem);
    }
    form.setFieldValue('coResidents', currentList);
    message.success(t("Đã điền thông tin CCCD của {0}.", [parsed.fullName]));
  };

  const buildPayload = (values: DocumentFormValues): UpdateOccupancyDocumentRequest => ({
    occupancyId: data!.occupancyId,
    ownerPhone: values.ownerPhone?.trim() || null,
    ownerCitizenId: values.ownerCitizenId?.trim() || null,
    ownerDateOfBirth: values.ownerDateOfBirth ? values.ownerDateOfBirth.format('YYYY-MM-DD') : null,
    ownerCitizenIdIssuedDate: values.ownerCitizenIdIssuedDate ? values.ownerCitizenIdIssuedDate.format('YYYY-MM-DD') : null,
    ownerCitizenIdIssuedPlace: values.ownerCitizenIdIssuedPlace?.trim() || null,
    ownerPermanentAddress: values.ownerPermanentAddress?.trim() || null,
    buildingAddress: values.buildingAddress?.trim() || null,
    policeStationName: values.policeStationName?.trim() || null,
    tenantName: values.tenantName?.trim() || data!.tenantName,
    tenantPhone: values.tenantPhone?.trim() || null,
    tenantEmail: values.tenantEmail?.trim() || null,
    tenantCitizenId: values.tenantCitizenId?.trim() || null,
    tenantDateOfBirth: values.tenantDateOfBirth ? values.tenantDateOfBirth.format('YYYY-MM-DD') : null,
    tenantGender: values.tenantGender?.trim() || null,
    tenantCitizenIdIssuedDate: values.tenantCitizenIdIssuedDate ? values.tenantCitizenIdIssuedDate.format('YYYY-MM-DD') : null,
    tenantCitizenIdIssuedPlace: values.tenantCitizenIdIssuedPlace?.trim() || null,
    tenantPermanentAddress: values.tenantPermanentAddress?.trim() || null,
    contractEndDate: values.contractEndDate ? values.contractEndDate.format('YYYY-MM-DD') : null,
    paymentDueDay: values.paymentDueDay ?? 5,
    roomAreaSqm: values.roomAreaSqm ?? null,
    handoverAssets: values.handoverAssets?.trim() || null,
    additionalTerms: values.additionalTerms?.trim() || null,
    householdHeadRelation: values.householdHeadRelation?.trim() || legalCt01Template.defaultRelation,
    coResidents: (values.coResidents ?? [])
      .filter((item) => item && item.fullName && item.fullName.trim().length > 0)
      .map((item) => ({
        fullName: item.fullName.trim(),
        dateOfBirth: item.dateOfBirth ? item.dateOfBirth.format('YYYY-MM-DD') : null,
        gender: item.gender?.trim() || null,
        citizenId: item.citizenId?.trim() || null,
        relation: item.relation?.trim() || null,
      })),
  });

  const saveMutation = useMutation({
    mutationFn: (payload: UpdateOccupancyDocumentRequest) => roomsApi.saveDocuments(targetRoomId, payload),
    onSuccess: async (updated) => {
      serverDocSignatureRef.current = JSON.stringify(updated);
      queryClient.setQueryData(['room-documents', targetRoomId], updated);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['rooms'] }),
        queryClient.invalidateQueries({ queryKey: ['buildings'] }),
      ]);
    },
    onError: (err: Error) => message.error(err.message),
  });

  const handleSave = async () => {
    if (!data) return;
    const values = await form.validateFields();
    await saveMutation.mutateAsync(buildPayload(values));
    message.success(t("Đã lưu thông tin hợp đồng và tờ khai CT01."));
  };

  const handleDownloadDocx = async (targetTab: DocumentTab) => {
    if (!data) return;
    try {
      const values = await form.validateFields();
      setIsDownloadingDocx(true);
      await saveMutation.mutateAsync(buildPayload(values));
      const blob = targetTab === 'contract'
        ? await roomsApi.downloadContractDocx(targetRoomId)
        : await roomsApi.downloadCt01Docx(targetRoomId);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      const safeName = displayRoomName.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, '-') || 'phong';
      const dateSuffix = dayjs().format('YYYYMMDD');
      link.href = url;
      link.download = targetTab === 'contract'
        ? `hop-dong-thue-tro-${safeName}-${dateSuffix}.docx`
        : `to-khai-ct01-${safeName}-${dateSuffix}.docx`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
      message.success(targetTab === 'contract' ? t("Đã tải file Word Hợp đồng thuê phòng.") : t("Đã tải file Word Tờ khai CT01."));
    } catch (err) {
      if (err instanceof Error && err.message) {
        message.error(err.message);
      }
    } finally {
      setIsDownloadingDocx(false);
    }
  };

  const handlePrintA4 = async () => {
    if (!data || !previewRef.current) return;
    try {
      const values = await form.validateFields();
      await saveMutation.mutateAsync(buildPayload(values));
    } catch {
      return;
    }
    const htmlContent = previewRef.current.innerHTML;
    const iframe = document.createElement('iframe');
    iframe.style.position = 'fixed';
    iframe.style.right = '0';
    iframe.style.bottom = '0';
    iframe.style.width = '0';
    iframe.style.height = '0';
    iframe.style.border = '0';
    document.body.appendChild(iframe);

    const doc = iframe.contentWindow?.document;
    if (!doc) {
      iframe.remove();
      return;
    }
    doc.open();
    doc.write(`<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8" />
  <title>${activeTab === 'contract' ? 'Hop-dong-thue-phong' : 'To-khai-CT01'}</title>
  <style>
    @page { size: A4; margin: 15mm 15mm 15mm 20mm; }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      padding: 0;
      font-family: "Times New Roman", Times, serif;
      font-size: 13pt;
      line-height: 1.45;
      color: #000;
      background: #fff;
    }
    .a4-header-note { text-align: right; font-style: italic; font-size: 10.5pt; margin-bottom: 4px; }
    .a4-republic { text-align: center; font-weight: 700; font-size: 13pt; text-transform: uppercase; margin: 0; }
    .a4-motto { text-align: center; font-weight: 700; font-size: 13pt; text-decoration: underline; margin: 2px 0 14px; }
    .a4-title { text-align: center; font-weight: 700; font-size: 15pt; text-transform: uppercase; margin: 10px 0 8px; }
    .a4-subtitle { text-align: center; font-size: 12.5pt; margin: 0 0 12px; }
    .a4-date-place { font-style: italic; margin: 6px 0 10px; }
    .a4-section-heading { font-weight: 700; margin: 12px 0 5px; font-size: 13pt; }
    .a4-line { margin: 4px 0; text-align: justify; }
    .a4-inline-row { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 8px 18px; margin: 4px 0; }
    .ct01-id-row { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; margin: 6px 0; }
    .ct01-id-boxes { display: inline-grid; grid-template-columns: repeat(12, 24px); border: 1px solid #000; }
    .ct01-id-box {
      width: 24px;
      height: 26px;
      border-right: 1px solid #000;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 12.5pt;
    }
    .ct01-id-box:last-child { border-right: 0; }
    .ct01-table { width: 100%; border-collapse: collapse; margin: 8px 0 12px; font-size: 11.5pt; }
    .ct01-table th, .ct01-table td { border: 1px solid #000; padding: 5px 6px; vertical-align: middle; }
    .ct01-table th { font-weight: 700; text-align: center; }
    .ct01-table td.center { text-align: center; }
    .a4-sign-grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-top: 20px; text-align: center; page-break-inside: avoid; }
    .a4-sign-grid-3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 10px; margin-top: 16px; text-align: center; font-size: 11.5pt; page-break-inside: avoid; }
    .a4-sign-box { min-height: 125px; display: flex; flex-direction: column; justify-content: space-between; }
    .a4-sign-note { font-style: italic; font-size: 10.5pt; font-weight: 400; }
    .a4-sign-name { font-weight: 700; margin-top: 56px; }
    .ct01-footnote { font-style: italic; font-size: 10pt; margin-top: 12px; }
  </style>
</head>
<body>${htmlContent}</body>
</html>`);
    doc.close();
    setTimeout(() => {
      iframe.contentWindow?.focus();
      iframe.contentWindow?.print();
      setTimeout(() => iframe.remove(), 1500);
    }, 150);
  };

  const livePreview = useMemo(() => {
    if (!data) return null;
    const v = watchedValues ?? {};
    return {
      ...data,
      ownerPhone: v.ownerPhone ?? data.ownerPhone,
      ownerCitizenId: v.ownerCitizenId ?? data.ownerCitizenId,
      ownerDateOfBirth: v.ownerDateOfBirth ?? toDayjs(data.ownerDateOfBirth),
      ownerCitizenIdIssuedDate: v.ownerCitizenIdIssuedDate ?? toDayjs(data.ownerCitizenIdIssuedDate),
      ownerCitizenIdIssuedPlace: v.ownerCitizenIdIssuedPlace ?? data.ownerCitizenIdIssuedPlace,
      ownerPermanentAddress: v.ownerPermanentAddress ?? data.ownerPermanentAddress,
      buildingAddress: v.buildingAddress ?? data.buildingAddress,
      policeStationName: v.policeStationName ?? data.policeStationName,
      tenantName: v.tenantName || data.tenantName,
      tenantPhone: v.tenantPhone ?? data.tenantPhone,
      tenantEmail: v.tenantEmail ?? data.tenantEmail,
      tenantCitizenId: v.tenantCitizenId ?? data.tenantCitizenId,
      tenantDateOfBirth: v.tenantDateOfBirth ?? toDayjs(data.tenantDateOfBirth),
      tenantGender: v.tenantGender ?? data.tenantGender,
      tenantCitizenIdIssuedDate: v.tenantCitizenIdIssuedDate ?? toDayjs(data.tenantCitizenIdIssuedDate),
      tenantCitizenIdIssuedPlace: v.tenantCitizenIdIssuedPlace ?? data.tenantCitizenIdIssuedPlace,
      tenantPermanentAddress: v.tenantPermanentAddress ?? data.tenantPermanentAddress,
      contractEndDate: v.contractEndDate ?? toDayjs(data.contractEndDate),
      paymentDueDay: v.paymentDueDay ?? data.paymentDueDay ?? 5,
      roomAreaSqm: v.roomAreaSqm ?? data.roomAreaSqm,
      handoverAssets: v.handoverAssets ?? data.handoverAssets,
      additionalTerms: v.additionalTerms ?? data.additionalTerms,
      householdHeadRelation: v.householdHeadRelation || data.householdHeadRelation || legalCt01Template.defaultRelation,
      coResidents: v.coResidents ?? (data.coResidents ?? []).map((item) => ({
        fullName: item.fullName,
        dateOfBirth: toDayjs(item.dateOfBirth),
        gender: item.gender,
        citizenId: item.citizenId,
        relation: item.relation,
      })),
    };
  }, [data, watchedValues]);

  const today = dayjs();

  const headerActions = data ? (
    <Space wrap>
      <Button
        icon={<SaveOutlined />}
        loading={saveMutation.isPending && !isDownloadingDocx}
        onClick={() => void handleSave()}
      >
        {t("Lưu thông tin")}
      </Button>
      <Button
        icon={<PrinterOutlined />}
        onClick={() => void handlePrintA4()}
      >
        {t("In / Lưu PDF (A4)")}
      </Button>
      <Button
        type="primary"
        icon={<DownloadOutlined />}
        loading={isDownloadingDocx}
        onClick={() => void handleDownloadDocx(activeTab)}
      >
        {activeTab === 'contract' ? t("Tải Word Hợp đồng (.docx)") : t("Tải Word CT01 (.docx)")}
      </Button>
    </Space>
  ) : null;

  const workspaceBody = query.isError ? (
    <LoadError error={query.error} onRetry={() => void query.refetch()} />
  ) : !data || !livePreview ? (
    <div className="contract-loading-state">
      <Spin size="large" />
      <Typography.Paragraph>{t("Đang chuẩn bị dữ liệu hợp đồng và tờ khai CT01…")}</Typography.Paragraph>
    </div>
  ) : (
    <div className="contract-workspace-grid">
      <div className="contract-form-pane">
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 14 }}
          message={t("Thông tin phòng, giá thuê, điện nước và tiền cọc đã được điền tự động")}
          description={t("Thông tin CCCD của chủ trọ và tên Công an phường/xã được lưu lại để tự động dùng cho tất cả các phòng khác.")}
        />
        <Form form={form} layout="vertical" requiredMark={false}>
          <Collapse
            defaultActiveKey={['tenant', 'owner', 'contract', 'ct01']}
            items={[
                  {
                    key: 'tenant',
                    label: <strong>{t("1. Bên thuê phòng / Người kê khai CT01 (Bên B)")}</strong>,
                    children: (
                      <>
                        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
                          <Button
                            size="small"
                            type="primary"
                            ghost
                            icon={<QrcodeOutlined />}
                            onClick={() => setQrTarget({ kind: 'tenant' })}
                          >
                            {t("Quét / Tải ảnh CCCD")}
                          </Button>
                        </div>
                        <Row gutter={12}>
                          <Col xs={24} sm={14}>
                            <Form.Item
                              name="tenantName"
                              label={t("Họ và tên người thuê")}
                              rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên người thuê.") }]}
                            >
                              <Input maxLength={160} placeholder={t("Họ và tên")} />
                            </Form.Item>
                          </Col>
                          <Col xs={24} sm={10}>
                            <Form.Item name="tenantPhone" label={t("Số điện thoại")}>
                              <Input maxLength={60} placeholder="09xx xxx xxx" />
                            </Form.Item>
                          </Col>
                        </Row>
                        <Row gutter={12}>
                          <Col xs={24} sm={12}>
                            <Form.Item
                              name="tenantCitizenId"
                              label={t("Số CCCD / Định danh cá nhân (12 số)")}
                              rules={[{ pattern: /^[0-9]{9,12}$/, message: t("Nhập từ 9 đến 12 chữ số.") }]}
                            >
                              <Input maxLength={12} placeholder="07909900xxxx" />
                            </Form.Item>
                          </Col>
                          <Col xs={12} sm={6}>
                            <Form.Item name="tenantDateOfBirth" label={t("Ngày sinh")}>
                              <DatePicker format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
                            </Form.Item>
                          </Col>
                          <Col xs={12} sm={6}>
                            <Form.Item name="tenantGender" label={t("Giới tính")}>
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
                        </Row>
                        <Row gutter={12}>
                          <Col xs={12} sm={8}>
                            <Form.Item name="tenantCitizenIdIssuedDate" label={t("Ngày cấp CCCD")}>
                              <DatePicker format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
                            </Form.Item>
                          </Col>
                          <Col xs={12} sm={16}>
                            <Form.Item name="tenantCitizenIdIssuedPlace" label={t("Nơi cấp CCCD")}>
                              <Input maxLength={200} placeholder={t("Cục Cảnh sát QLHC về TTXH")} />
                            </Form.Item>
                          </Col>
                        </Row>
                        <Form.Item name="tenantPermanentAddress" label={t("Địa chỉ thường trú")}>
                          <Input maxLength={500} placeholder={t("Số nhà, đường, xã/phường, quận/huyện, tỉnh/thành phố")} />
                        </Form.Item>
                        <Form.Item name="tenantEmail" label={t("Email liên hệ (nếu có)")}>
                          <Input maxLength={320} placeholder="ten@email.com" />
                        </Form.Item>
                      </>
                    ),
                  },
                  {
                    key: 'owner',
                    label: <strong>{t("2. Bên cho thuê / Chủ hộ (Bên A — Lưu dùng chung mọi phòng)")}</strong>,
                    children: (
                      <>
                        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
                          <Button
                            size="small"
                            type="primary"
                            ghost
                            icon={<QrcodeOutlined />}
                            onClick={() => setQrTarget({ kind: 'owner' })}
                          >
                            {t("Quét / Tải ảnh CCCD")}
                          </Button>
                        </div>
                        <Row gutter={12}>
                          <Col xs={24} sm={12}>
                            <Form.Item label={t("Họ và tên chủ trọ")}>
                              <Input value={data.ownerName} disabled />
                            </Form.Item>
                          </Col>
                          <Col xs={24} sm={12}>
                            <Form.Item name="ownerPhone" label={t("Số điện thoại chủ trọ")}>
                              <Input maxLength={60} placeholder="09xx xxx xxx" />
                            </Form.Item>
                          </Col>
                        </Row>
                        <Row gutter={12}>
                          <Col xs={24} sm={12}>
                            <Form.Item
                              name="ownerCitizenId"
                              label={t("Số CCCD / Định danh chủ hộ (12 số)")}
                              rules={[{ pattern: /^[0-9]{9,12}$/, message: t("Nhập từ 9 đến 12 chữ số.") }]}
                            >
                              <Input maxLength={12} placeholder="07908800xxxx" />
                            </Form.Item>
                          </Col>
                          <Col xs={12} sm={6}>
                            <Form.Item name="ownerDateOfBirth" label={t("Ngày sinh")}>
                              <DatePicker format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
                            </Form.Item>
                          </Col>
                          <Col xs={12} sm={6}>
                            <Form.Item name="ownerCitizenIdIssuedDate" label={t("Ngày cấp CCCD")}>
                              <DatePicker format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
                            </Form.Item>
                          </Col>
                        </Row>
                        <Form.Item name="ownerCitizenIdIssuedPlace" label={t("Nơi cấp CCCD")}>
                          <Input maxLength={200} placeholder={t("Cục Cảnh sát QLHC về TTXH")} />
                        </Form.Item>
                        <Form.Item name="ownerPermanentAddress" label={t("Địa chỉ thường trú")}>
                          <Input maxLength={500} placeholder={t("Số nhà, đường, xã/phường, quận/huyện, tỉnh/thành phố")} />
                        </Form.Item>
                      </>
                    ),
                  },
                  {
                    key: 'contract',
                    label: <strong>{t("3. Điều khoản Hợp đồng & Tài sản bàn giao")}</strong>,
                    children: (
                      <>
                        <Form.Item name="buildingAddress" label={t("Địa chỉ nhà trọ / nơi đăng ký tạm trú")}>
                          <Input maxLength={500} placeholder={t("Số nhà, đường, xã/phường, quận/huyện, tỉnh/thành phố")} />
                        </Form.Item>
                        <Row gutter={12}>
                          <Col xs={24} sm={8}>
                            <Form.Item name="contractEndDate" label={t("Ngày kết thúc hợp đồng")}>
                              <DatePicker format={dateFormat()} style={{ width: '100%' }} placeholder={t("Chọn ngày")} />
                            </Form.Item>
                          </Col>
                          <Col xs={12} sm={8}>
                            <Form.Item name="paymentDueDay" label={t("Hạn đóng tiền (ngày)")}>
                              <InputNumber min={1} max={28} precision={0} style={{ width: '100%' }} />
                            </Form.Item>
                          </Col>
                          <Col xs={12} sm={8}>
                            <Form.Item name="roomAreaSqm" label={t("Diện tích phòng")}>
                              <InputNumber min={0} max={10000} step={1} style={{ width: '100%' }} addonAfter="m²" />
                            </Form.Item>
                          </Col>
                        </Row>
                        <Form.Item name="handoverAssets" label={t("Danh mục tài sản bàn giao")}>
                          <Input.TextArea
                            rows={2}
                            maxLength={2000}
                            placeholder={t("Ví dụ: 01 máy lạnh, 01 bình nóng lạnh, 01 giường, 01 tủ quần áo, 02 chìa khóa phòng…")}
                          />
                        </Form.Item>
                        <Form.Item name="additionalTerms" label={t("Thỏa thuận / nội quy bổ sung (nếu có)")}>
                          <Input.TextArea
                            rows={2}
                            maxLength={2000}
                            placeholder={t("Ví dụ: Giờ giấc tự do, không nuôi thú cưng, báo trước 30 ngày khi trả phòng…")}
                          />
                        </Form.Item>
                      </>
                    ),
                  },
                  {
                    key: 'ct01',
                    label: <strong>{t("4. Tờ khai cư trú CT01 & Thành viên ở cùng")}</strong>,
                    children: (
                      <>
                        <Form.Item
                          name="policeStationName"
                          label={t("Kính gửi Cơ quan Công an (lưu theo dãy trọ)")}
                        >
                          <Input maxLength={250} placeholder={t("Ví dụ: Công an phường Thạnh Mỹ Lợi, TP. Thủ Đức, TP. Hồ Chí Minh")} />
                        </Form.Item>
                        <Form.Item name="householdHeadRelation" label={t("Mối quan hệ với chủ hộ")}>
                          <Input maxLength={100} placeholder={t("Người thuê trọ")} />
                        </Form.Item>
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
                          <Typography.Text strong style={{ display: 'block' }}>
                            {t("Thành viên cùng thay đổi cư trú / ở cùng phòng")}
                          </Typography.Text>
                          <Button
                            size="small"
                            type="primary"
                            ghost
                            icon={<QrcodeOutlined />}
                            onClick={() => setQrTarget({ kind: 'coResident-add' })}
                          >
                            {t("Quét CCCD thêm thành viên")}
                          </Button>
                        </div>
                        <Form.List name="coResidents">
                          {(fields, { add, remove }) => (
                            <div className="co-residents-list">
                              {fields.map(({ key, name, ...restField }, index) => (
                                <div key={key} className="co-resident-item">
                                  <Row gutter={8} align="middle">
                                    <Col xs={24} sm={9}>
                                      <Form.Item
                                        {...restField}
                                        name={[name, 'fullName']}
                                        label={t("Họ và tên")}
                                        rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên người thuê.") }]}
                                      >
                                        <Input placeholder={t("Họ và tên")} maxLength={160} />
                                      </Form.Item>
                                    </Col>
                                    <Col xs={12} sm={6}>
                                      <Form.Item {...restField} name={[name, 'dateOfBirth']} label={t("Ngày sinh")}>
                                        <DatePicker format={dateFormat()} style={{ width: '100%' }} />
                                      </Form.Item>
                                    </Col>
                                    <Col xs={12} sm={4}>
                                      <Form.Item {...restField} name={[name, 'gender']} label={t("Giới tính")}>
                                        <Select
                                          allowClear
                                          options={[
                                            { value: legalCt01Template.genderMale, label: t("Nam") },
                                            { value: legalCt01Template.genderFemale, label: t("Nữ") },
                                          ]}
                                        />
                                      </Form.Item>
                                    </Col>
                                    <Col xs={12} sm={5}>
                                      <Form.Item {...restField} name={[name, 'citizenId']} label={t("Số CCCD")}>
                                        <Input maxLength={12} placeholder="07909900xxxx" />
                                      </Form.Item>
                                    </Col>
                                    <Col xs={10} sm={18}>
                                      <Form.Item {...restField} name={[name, 'relation']} label={t("Mối quan hệ với chủ hộ")}>
                                        <Input maxLength={100} placeholder={t("Người thuê trọ")} />
                                      </Form.Item>
                                    </Col>
                                    <Col xs={14} sm={6} style={{ textAlign: 'right' }}>
                                      <Space size={4}>
                                        <Button
                                          size="small"
                                          type="text"
                                          icon={<QrcodeOutlined />}
                                          onClick={() => setQrTarget({ kind: 'coResident-row', index })}
                                        />
                                        <Button
                                          danger
                                          type="text"
                                          icon={<DeleteOutlined />}
                                          onClick={() => remove(name)}
                                          aria-label={t("Xóa thành viên")}
                                        />
                                      </Space>
                                    </Col>
                                  </Row>
                                </div>
                              ))}
                              <Button
                                type="dashed"
                                block
                                icon={<PlusOutlined />}
                                onClick={() => add({ fullName: '', relation: legalCt01Template.defaultRelation })}
                              >
                                {t("Thêm thành viên ở cùng")}
                              </Button>
                            </div>
                          )}
                        </Form.List>
                      </>
                    ),
                  },
                ]}
              />
            </Form>
          </div>

          <div className="contract-preview-pane">
            <div className="contract-preview-toolbar">
              <Segmented
                value={activeTab}
                onChange={(val) => setActiveTab(val as DocumentTab)}
                options={[
                  {
                    value: 'contract',
                    label: (
                      <Space size={6}>
                        <FileTextOutlined />
                        <span>{t("Bản xem trước: Hợp đồng thuê trọ")}</span>
                      </Space>
                    ),
                  },
                  {
                    value: 'ct01',
                    label: (
                      <Space size={6}>
                        <FileTextOutlined />
                        <span>{t("Bản xem trước: Mẫu CT01 (TT 66/2023/TT-BCA)")}</span>
                      </Space>
                    ),
                  },
                ]}
              />
              <Space size={8}>
                <Button
                  size="small"
                  icon={<DownloadOutlined />}
                  loading={isDownloadingDocx}
                  onClick={() => void handleDownloadDocx(activeTab === 'contract' ? 'ct01' : 'contract')}
                >
                  {activeTab === 'contract' ? t("Tải kèm CT01 (.docx)") : t("Tải kèm Hợp đồng (.docx)")}
                </Button>
              </Space>
            </div>

            <div className="contract-a4-scroll">
              <div className="contract-a4-sheet" ref={previewRef} data-testid="document-a4-preview">
                {activeTab === 'contract' ? (
                  <RentalContractA4Preview preview={livePreview} today={today} />
                ) : (
                  <Ct01A4Preview preview={livePreview} today={today} />
                )}
              </div>
            </div>
          </div>
          <CccdQrScannerModal
            open={qrTarget !== null}
            title={
              qrTarget?.kind === 'owner'
                ? t("Quét mã QR CCCD chủ trọ")
                : qrTarget?.kind === 'tenant'
                  ? t("Quét mã QR CCCD người thuê")
                  : t("Quét mã QR CCCD thành viên ở cùng")
            }
            onClose={() => setQrTarget(null)}
            onDetected={handleQrDetected}
          />
        </div>
      );

  if (asPage) {
    return (
      <div className="rooms-workspace room-documents-page contract-documents-drawer">
        <div style={{ marginBottom: 8 }}>
          <Button type="link" icon={<ArrowLeftOutlined />} style={{ paddingLeft: 0 }} onClick={onClose}>
            {t("Phòng & người thuê")}
          </Button>
        </div>
        <PageTitle
          title={t("Hợp đồng thuê trọ & Tờ khai CT01 · {0}", [displayRoomName])}
          subtitle={t("Thông tin phòng, giá thuê, điện nước và tiền cọc đã được điền tự động")}
          extra={headerActions}
        />
        {workspaceBody}
      </div>
    );
  }

  return (
    <Drawer
      className="contract-documents-drawer"
      title={t("Hợp đồng thuê trọ & Tờ khai CT01 · {0}", [displayRoomName])}
      width={1220}
      open
      onClose={onClose}
      extra={headerActions}
    >
      {workspaceBody}
    </Drawer>
  );
}

function RentalContractA4Preview({
  preview,
  today,
}: {
  preview: NonNullable<ReturnType<typeof Object>>;
  today: Dayjs;
}) {
  const p = preview as OccupancyDocumentData & {
    ownerDateOfBirth?: Dayjs | null;
    ownerCitizenIdIssuedDate?: Dayjs | null;
    tenantDateOfBirth?: Dayjs | null;
    tenantCitizenIdIssuedDate?: Dayjs | null;
    contractEndDate?: Dayjs | null;
    coResidents?: CoResidentFormItem[];
  };

  const c = legalContractTemplate;
  const locationText = p.buildingAddress?.trim() || p.buildingName;
  const fullRoomAddress = p.buildingAddress?.trim()
    ? c.roomWithBuildingAddress(p.roomName, p.buildingName, p.buildingAddress.trim())
    : `${p.roomName} - ${p.buildingName}`;
  const dueDay = Math.min(28, Math.max(1, Number(p.paymentDueDay || 5)));
  const validCoResidents = (p.coResidents ?? []).filter((item) => item?.fullName?.trim());

  return (
    <div className="a4-document-content">
      <p className="a4-republic">{c.republic}</p>
      <p className="a4-motto">{c.motto}</p>
      <h2 className="a4-title">{c.contractTitle}</h2>
      <p className="a4-date-place">
        {c.todayAtAddress(today.format('DD'), today.format('MM'), today.format('YYYY'), locationText)}
      </p>
      <p className="a4-line">{c.partiesIntro}</p>

      <div className="a4-section-heading">{c.partyAHeading}</div>
      <p className="a4-line">{c.fullNamePrefix}<strong>{p.ownerName}</strong></p>
      <p className="a4-line">
        {c.dobPrefix}{formatDateDisplay(p.ownerDateOfBirth)} &nbsp;&nbsp;&nbsp;&nbsp; {c.citizenIdPrefix}<strong>{p.ownerCitizenId?.trim() || '..............................'}</strong>
      </p>
      <p className="a4-line">
        {c.issuedDatePrefix}{formatDateDisplay(p.ownerCitizenIdIssuedDate)} &nbsp;&nbsp;&nbsp;&nbsp; {c.issuedPlacePrefix}{p.ownerCitizenIdIssuedPlace?.trim() || '....................................................'}
      </p>
      <p className="a4-line">
        {c.permanentAddressPrefix}{p.ownerPermanentAddress?.trim() || p.buildingAddress?.trim() || '................................................................................'}
      </p>
      <p className="a4-line">{c.phonePrefix}{p.ownerPhone?.trim() || '................................................'}</p>

      <div className="a4-section-heading">{c.partyBHeading}</div>
      <p className="a4-line">{c.fullNamePrefix}<strong>{p.tenantName}</strong></p>
      <p className="a4-line">
        {c.dobPrefix}{formatDateDisplay(p.tenantDateOfBirth)} &nbsp;&nbsp;&nbsp;&nbsp; {c.genderPrefix}{p.tenantGender?.trim() || '..........'} &nbsp;&nbsp;&nbsp;&nbsp; {c.citizenIdShortPrefix}<strong>{p.tenantCitizenId?.trim() || '........................'}</strong>
      </p>
      <p className="a4-line">
        {c.issuedDatePrefix}{formatDateDisplay(p.tenantCitizenIdIssuedDate)} &nbsp;&nbsp;&nbsp;&nbsp; {c.issuedPlacePrefix}{p.tenantCitizenIdIssuedPlace?.trim() || '....................................................'}
      </p>
      <p className="a4-line">
        {c.permanentAddressPrefix}{p.tenantPermanentAddress?.trim() || '................................................................................'}
      </p>
      <p className="a4-line">
        {c.phonePrefix}{p.tenantPhone?.trim() || '................................................'} &nbsp;&nbsp;&nbsp;&nbsp; {c.peopleCountText(p.people)}
      </p>
      {validCoResidents.length > 0 && (
        <p className="a4-line">
          {c.coResidentsLine(validCoResidents.map((m) => (m.citizenId ? `${m.fullName} (CCCD: ${m.citizenId})` : m.fullName)).join('; '))}
        </p>
      )}

      <p className="a4-line" style={{ fontStyle: 'italic', marginTop: 8 }}>{c.agreementIntro}</p>

      <div className="a4-section-heading">{c.section1Heading}</div>
      <p className="a4-line">{c.clause1_1Prefix}<strong>{fullRoomAddress}</strong>.</p>
      <p className="a4-line">{c.clause1_2(p.roomAreaSqm)}</p>
      <p className="a4-line">
        {c.clause1_3Prefix}<strong>{formatDateDisplay(p.startDate)}</strong>{' '}
        {p.contractEndDate ? (
          <>{c.clause1_3ToDatePrefix}<strong>{formatDateDisplay(p.contractEndDate)}</strong>.</>
        ) : (
          <>{c.clause1_3MonthlyFallback}</>
        )}
      </p>

      <div className="a4-section-heading">{c.section2Heading}</div>
      <p className="a4-line">
        {c.clause2_1Prefix}<strong>{formatVnd(p.rent)}{c.perMonthSuffix}</strong> <em>{c.inWordsText(p.rentInWords)}</em>{c.clause2_1Suffix}
      </p>
      <p className="a4-line">
        {c.clause2_2Prefix}<strong>{formatVnd(p.electricityRate)}</strong>{c.clause2_2Middle}<strong>{p.initialReading} kWh</strong>.
      </p>
      <p className="a4-line">
        {c.clause2_3Prefix}<strong>{formatVnd(p.waterFee)}</strong> {p.waterPerPerson ? c.waterPerPersonUnit : c.waterPerRoomUnit}.
      </p>
      <p className="a4-line">
        {c.clause2_4Prefix}<strong>{formatVnd(p.serviceFee)}{c.perMonthSuffix}</strong>.
      </p>
      <p className="a4-line">
        {c.clause2_5Prefix}<strong>{String(dueDay).padStart(2, '0')}</strong>{c.clause2_5Suffix}
      </p>
      <p className="a4-line">
        {c.clause2_6Prefix}
        {p.bankAccountNumber ? (
          <>
            {c.clause2_6BankPrefix}<strong>{p.bankAccountNumber}</strong> ({p.bankShortName || p.bankName || p.bankBin}){c.clause2_6HolderPrefix}<strong>{p.bankAccountName || p.ownerName}</strong>.
          </>
        ) : (
          <>{c.clause2_6Fallback}</>
        )}
      </p>

      <div className="a4-section-heading">{c.section3Heading}</div>
      <p className="a4-line">
        {c.clause3_1Prefix}<strong>{formatVnd(p.depositRequired)}</strong> <em>{c.inWordsText(p.depositInWords)}</em>.
      </p>
      <p className="a4-line">{c.clause3_2}</p>
      <p className="a4-line">{c.clause3_3}</p>

      <div className="a4-section-heading">{c.section4Heading}</div>
      <p className="a4-line">
        {c.clause4_1Prefix}{p.handoverAssets?.trim() || c.defaultHandoverAssets}
      </p>
      <p className="a4-line">{c.clause4_2}</p>
      {p.additionalTerms?.trim() && (
        <p className="a4-line">{c.clause4_3Prefix}{p.additionalTerms.trim()}</p>
      )}

      <div className="a4-section-heading">{c.section5Heading}</div>
      <p className="a4-line">{c.clause5_1}</p>
      <p className="a4-line">{c.clause5_2}</p>

      <div className="a4-section-heading">{c.section6Heading}</div>
      <p className="a4-line">{c.clause6_1}</p>
      <p className="a4-line">{c.clause6_2}</p>

      <div className="a4-sign-grid-2">
        <div className="a4-sign-box">
          <div>
            <strong>{c.signPartyATitle}</strong>
            <div className="a4-sign-note">{c.signInstruction}</div>
          </div>
          <div className="a4-sign-name">{p.ownerName}</div>
        </div>
        <div className="a4-sign-box">
          <div>
            <strong>{c.signPartyBTitle}</strong>
            <div className="a4-sign-note">{c.signInstruction}</div>
          </div>
          <div className="a4-sign-name">{p.tenantName}</div>
        </div>
      </div>
    </div>
  );
}

function Ct01A4Preview({
  preview,
  today,
}: {
  preview: NonNullable<ReturnType<typeof Object>>;
  today: Dayjs;
}) {
  const p = preview as OccupancyDocumentData & {
    tenantDateOfBirth?: Dayjs | null;
    coResidents?: CoResidentFormItem[];
  };

  const f = legalCt01Template;
  const tenantBoxes = buildCitizenBoxes(p.tenantCitizenId);
  const ownerBoxes = buildCitizenBoxes(p.ownerCitizenId);
  const residenceAddress = p.buildingAddress?.trim()
    ? `${p.roomName}, ${p.buildingName}, ${p.buildingAddress.trim()}`
    : `${p.roomName}, ${p.buildingName}`;

  const activeCoResidents = (p.coResidents ?? []).filter((c) => c && c.fullName?.trim());
  const tableRowsCount = Math.max(3, activeCoResidents.length);
  const signDateLine = f.signDateLine(today.format('DD'), today.format('MM'), today.format('YYYY'));

  return (
    <div className="a4-document-content">
      <div className="a4-header-note">{f.circularHeader}</div>
      <p className="a4-republic">{legalContractTemplate.republic}</p>
      <p className="a4-motto">{legalContractTemplate.motto}</p>
      <h2 className="a4-title">{f.title}</h2>
      <p className="a4-subtitle">
        {f.dearPrefix}<sup>(1)</sup>: {p.policeStationName?.trim() || f.policeFallback}
      </p>

      <p className="a4-line">
        {f.item1Prefix}<strong>{p.tenantName.toUpperCase()}</strong>
      </p>
      <div className="a4-inline-row">
        <span>{f.item2Prefix}{formatDateDisplay(p.tenantDateOfBirth)}</span>
        <span>{f.item3Prefix}{p.tenantGender?.trim() || '............'}</span>
      </div>

      <div className="ct01-id-row">
        <span>{f.item4Label}</span>
        <div className="ct01-id-boxes">
          {tenantBoxes.map((digit, index) => (
            <span key={index} className="ct01-id-box">{digit}</span>
          ))}
        </div>
      </div>

      <div className="a4-inline-row">
        <span>{f.item5Prefix}{p.tenantPhone?.trim() || '....................................'}</span>
        <span>{f.item6Prefix}{p.tenantEmail?.trim() || '....................................'}</span>
      </div>

      <p className="a4-line">
        {f.item7Prefix}<strong>{p.ownerName.toUpperCase()}</strong>
      </p>
      <p className="a4-line">
        {f.item8Prefix}{p.householdHeadRelation?.trim() || f.defaultRelation}
      </p>

      <div className="ct01-id-row">
        <span>{f.item9Label}</span>
        <div className="ct01-id-boxes">
          {ownerBoxes.map((digit, index) => (
            <span key={index} className="ct01-id-box">{digit}</span>
          ))}
        </div>
      </div>

      <p className="a4-line">
        {f.item10Prefix}<sup>(2)</sup>: <strong>{f.item10Content(residenceAddress)}</strong>
      </p>

      <p className="a4-line">{f.item11Label}</p>
      <table className="ct01-table">
        <thead>
          <tr>
            <th style={{ width: 38 }}>{f.colStt}</th>
            <th>{f.colFullName}</th>
            <th style={{ width: 105 }}>{f.colDob}</th>
            <th style={{ width: 68 }}>{f.colGender}</th>
            <th style={{ width: 130 }}>{f.colCitizenId}</th>
            <th style={{ width: 120 }}>{f.colRelation}</th>
          </tr>
        </thead>
        <tbody>
          {Array.from({ length: tableRowsCount }, (_, idx) => {
            const member = activeCoResidents[idx];
            return (
              <tr key={idx}>
                <td className="center">{idx + 1}</td>
                <td>{member?.fullName ?? ''}</td>
                <td className="center">{member?.dateOfBirth ? formatDateDisplay(member.dateOfBirth) : ''}</td>
                <td className="center">{member?.gender ?? ''}</td>
                <td className="center">{member?.citizenId ?? ''}</td>
                <td>{member?.relation ?? ''}</td>
              </tr>
            );
          })}
        </tbody>
      </table>

      <div className="a4-sign-grid-3">
        <div className="a4-sign-box">
          <div>
            <div className="a4-sign-note">{signDateLine}</div>
            <strong>{f.signHeadTitle}</strong>
            <div className="a4-sign-note">{f.signHeadInstruction}</div>
          </div>
          <div className="a4-sign-name">{p.ownerName}</div>
        </div>
        <div className="a4-sign-box">
          <div>
            <div className="a4-sign-note">{signDateLine}</div>
            <strong>{f.signOwnerTitle}</strong>
            <div className="a4-sign-note">{f.signInstruction}</div>
          </div>
          <div className="a4-sign-name">{p.ownerName}</div>
        </div>
        <div className="a4-sign-box">
          <div>
            <div className="a4-sign-note">{signDateLine}</div>
            <strong>{f.signDeclarantTitle}</strong>
            <div className="a4-sign-note">{f.signInstruction}</div>
          </div>
          <div className="a4-sign-name">{p.tenantName}</div>
        </div>
      </div>

      <p className="ct01-footnote">{f.footnote}</p>
    </div>
  );
}
