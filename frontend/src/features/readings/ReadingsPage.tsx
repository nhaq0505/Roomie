import { ArrowLeftOutlined, CheckCircleFilled, ClockCircleOutlined, InfoCircleOutlined, SearchOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { TableProps } from 'antd';
import { Alert, App, Button, Form, Input, InputNumber, Modal, Select, Space, Tag, Typography } from 'antd';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { currentPeriod, formatVnd, periodLabel } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import MoneyInput from '../../shared/ui/MoneyInput';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { EmptyPanel, LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { invoicesApi, type Invoice } from '../invoices/index';
import { roomsApi, type Room } from '../rooms/index';
import { readingsApi, type Reading } from './index';

interface ReadingRow { room: Room; saved?: Reading; draftInvoice?: Invoice; issuedInvoice?: Invoice; }
interface DraftSession { room: Room; currentReading: number; invoice: Invoice; }
interface DraftAmounts { rent: number; electricityAmount: number; waterAmount: number; serviceAmount: number; }

function lastDay(period: string) { const [year, month] = period.split('-').map(Number); return `${period}-${String(new Date(Date.UTC(year, month, 0)).getUTCDate()).padStart(2, '0')}`; }

export default function ReadingsPage() {
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const client = useQueryClient();
  const period = currentPeriod();
  const [drafts, setDrafts] = useState<Record<string, number | null>>({});
  const [waterDrafts, setWaterDrafts] = useState<Record<string, number | null>>({});
  const [draftSession, setDraftSession] = useState<DraftSession | null>(null);
  const [viewingInvoice, setViewingInvoice] = useState<{ room: Room; invoice: Invoice } | null>(null);
  const [draftForm] = Form.useForm<DraftAmounts>();
  const [selectedBuilding, setSelectedBuilding] = useState<string>('all');
  const [searchText, setSearchText] = useState<string>('');
  const publishKeys = useRef(new Map<string, string>());
  const userEditedRoomsRef = useRef(new Set<string>());
  const userEditedWaterRoomsRef = useRef(new Set<string>());

  const roomsQuery = useQuery({ queryKey: ['rooms', period], queryFn: () => roomsApi.list(period) });
  const readingsQuery = useQuery({ queryKey: ['readings', period], queryFn: () => readingsApi.list(period) });
  const invoicesQuery = useQuery({ queryKey: ['invoices', 'period', period], queryFn: () => invoicesApi.list(period) });

  const rows = useMemo<ReadingRow[]>(() => (roomsQuery.data ?? [])
    .filter((room) => room.occupancy && room.occupancy.startDate <= lastDay(period))
    .map((room) => ({
      room,
      saved: (readingsQuery.data ?? []).find((reading) => reading.roomId === room.id && reading.occupancyId === room.occupancy?.id),
      draftInvoice: (invoicesQuery.data ?? []).find((invoice) => invoice.kind === 'Monthly' && invoice.roomId === room.id && invoice.period === period && invoice.lifecycleState === 'Draft' && invoice.occupancyId === room.occupancy?.id),
      issuedInvoice: (invoicesQuery.data ?? []).find((invoice) => invoice.kind === 'Monthly' && invoice.roomId === room.id && invoice.period === period && invoice.lifecycleState === 'Issued' && invoice.occupancyId === room.occupancy?.id),
    })), [roomsQuery.data, readingsQuery.data, invoicesQuery.data, period]);

  const buildingOptions = useMemo(() => {
    const map = new Map<string, string>();
    for (const r of rows) {
      if (r.room.buildingId) {
        map.set(r.room.buildingId, r.room.buildingName || t("Dãy trọ"));
      }
    }
    return [
      { value: 'all', label: t("Tất cả dãy trọ") },
      ...Array.from(map.entries()).map(([id, name]) => ({ value: id, label: name })),
    ];
  }, [rows]);

  const buildingRows = useMemo(() => {
    if (selectedBuilding === 'all') return rows;
    return rows.filter((r) => r.room.buildingId === selectedBuilding);
  }, [rows, selectedBuilding]);

  const filteredRows = useMemo(() => {
    const keyword = searchText.trim().toLocaleLowerCase();
    const matched = keyword
      ? buildingRows.filter((r) => r.room.name.toLocaleLowerCase().includes(keyword))
      : buildingRows;
    return [...matched].sort((a, b) => {
      const aIssued = Boolean(a.saved && !a.draftInvoice);
      const bIssued = Boolean(b.saved && !b.draftInvoice);
      if (aIssued !== bIssued) return aIssued ? 1 : -1;
      return 0;
    });
  }, [buildingRows, searchText]);

  const prevPeriodRef = useRef(period);
  useEffect(() => {
    if (prevPeriodRef.current !== period) {
      prevPeriodRef.current = period;
      userEditedRoomsRef.current.clear();
      userEditedWaterRoomsRef.current.clear();
      setDrafts(Object.fromEntries(rows.map(({ room, saved }) => [
        room.id,
        saved?.currentReading ?? null,
      ])));
      setWaterDrafts(Object.fromEntries(rows.map(({ room, saved }) => [
        room.id,
        saved?.currentWaterReading ?? null,
      ])));
      return;
    }
    setDrafts((prev) => {
      const next: Record<string, number | null> = {};
      for (const { room, saved } of rows) {
        if (saved?.currentReading !== undefined) {
          next[room.id] = saved.currentReading;
        } else if (userEditedRoomsRef.current.has(room.id) && prev[room.id] !== undefined) {
          next[room.id] = prev[room.id];
        } else if (prev[room.id] !== undefined) {
          next[room.id] = prev[room.id];
        } else {
          next[room.id] = null;
        }
      }
      return next;
    });
    setWaterDrafts((prev) => {
      const next: Record<string, number | null> = {};
      for (const { room, saved } of rows) {
        if (saved?.currentWaterReading !== undefined && saved?.currentWaterReading !== null) {
          next[room.id] = saved.currentWaterReading;
        } else if (userEditedWaterRoomsRef.current.has(room.id) && prev[room.id] !== undefined) {
          next[room.id] = prev[room.id];
        } else if (prev[room.id] !== undefined) {
          next[room.id] = prev[room.id];
        } else {
          next[room.id] = null;
        }
      }
      return next;
    });
  }, [rows, period]);

  const refresh = async () => Promise.all([
    client.invalidateQueries({ queryKey: ['readings', period] }),
    client.invalidateQueries({ queryKey: ['invoices'] }),
    client.invalidateQueries({ queryKey: ['rooms'] }),
    client.invalidateQueries({ queryKey: ['dashboard'] }),
  ]);

  const readingMutation = useMutation({
    mutationFn: (body: { roomId: string; period: string; currentReading: number; currentWaterReading?: number | null }) => readingsApi.save(body),
    onSuccess: async () => {
      await refresh();
      message.success(t("Đã chốt chỉ số và lập hóa đơn."));
    },
    onError: (error: Error) => message.error(error.message),
  });

  const publishMutation = useMutation({
    mutationFn: ({ invoice, values, requestId }: { invoice: Invoice; values: DraftAmounts; requestId: string; key: string }) => invoicesApi.publishDraft(invoice.id, { ...values, expectedVersion: invoice.version ?? 0, requestId }),
    onSuccess: async (_, variables) => { await refresh(); setDraftSession(null); publishKeys.current.delete(variables.key); message.success(t("Đã phát hành hóa đơn nháp.")); },
    onError: (error: Error) => message.error(error.message),
  });

  const openDraft = (row: ReadingRow) => {
    if (row.saved?.currentReading == null || !row.draftInvoice) return;
    setDraftSession({ room: row.room, currentReading: row.saved.currentReading, invoice: row.draftInvoice });
    draftForm.setFieldsValue({ rent: row.draftInvoice.rent, electricityAmount: row.draftInvoice.electricityAmount, waterAmount: row.draftInvoice.waterAmount, serviceAmount: row.draftInvoice.serviceAmount });
  };
  const confirmReading = (row: ReadingRow, currentReading: number, currentWaterReading?: number | null) => {
    const previous = row.saved?.previousReading ?? row.room.previousReading ?? row.room.initialReading ?? 0;
    const consumption = Math.round(Math.max(0, currentReading - previous) * 1000) / 1000;
    const rent = Math.round(row.room.rent ?? 0);
    const electricityRate = row.room.electricityRate ?? 0;
    const electricityAmount = Math.round(consumption * electricityRate);
    const people = row.room.occupancy?.people ?? 1;
    const waterFee = row.room.waterFee ?? 0;
    const isWaterByMeter = Boolean(row.room.waterByMeter);
    const previousWater = row.saved?.previousWaterReading ?? row.room.previousWaterReading ?? row.room.initialWaterReading ?? 0;
    const waterConsumption = isWaterByMeter && currentWaterReading != null ? Math.round(Math.max(0, currentWaterReading - previousWater) * 1000) / 1000 : null;
    const waterAmount = isWaterByMeter
      ? Math.round((waterConsumption ?? 0) * waterFee)
      : Math.round(row.room.waterPerPerson ? waterFee * people : waterFee);
    const serviceAmount = Math.round(row.room.serviceFee ?? 0);
    const total = rent + electricityAmount + waterAmount + serviceAmount;

    modal.confirm({
      width: 480,
      icon: null,
      className: 'reading-confirm-modal',
      title: t("Chốt chỉ số và lập hóa đơn?"),
      content: (
        <div className="reading-confirm-detail">
          <div className="invoice-period-box" style={{ margin: '10px 0 12px' }}>
            <span>{row.room.name} · {row.room.occupancy?.tenantName || t("Người thuê")}</span>
            <strong style={{ textAlign: 'right' }}>{periodLabel(period)}</strong>
          </div>
          <div className="invoice-breakdown">
            <div>
              <span>{t("Tiền thuê phòng")}</span>
              <strong>{formatVnd(rent)}</strong>
            </div>
            <div>
              <span>
                {t("Tiền điện")}{' '}
                <small>{consumption} kWh · {previous} → {currentReading}</small>
              </span>
              <strong>{formatVnd(electricityAmount)}</strong>
            </div>
            <div>
              <span>
                {t("Tiền nước")}
                {isWaterByMeter && waterConsumption !== null && currentWaterReading != null && (
                  <> <small>{waterConsumption} m³ · {previousWater} → {currentWaterReading}</small></>
                )}
              </span>
              <strong>{formatVnd(waterAmount)}</strong>
            </div>
            <div>
              <span>{t("Phí dịch vụ")}</span>
              <strong>{formatVnd(serviceAmount)}</strong>
            </div>
            <div className="breakdown-total">
              <span>{t("Tổng hóa đơn")}</span>
              <strong style={{ color: '#087c69', fontSize: 16 }}>{formatVnd(total)}</strong>
            </div>
          </div>
          <div style={{ marginTop: 12, padding: '8px 12px', borderRadius: 8, background: '#f6faf8', color: '#526970', fontSize: 12.5 }}>
            {t("Tiền thuê được tính đủ theo tháng, không phụ thuộc ngày bắt đầu thuê.")}
          </div>
        </div>
      ),
      okText: t("Chốt & lập hóa đơn"),
      cancelText: t("Kiểm tra lại"),
      onOk: () => readingMutation.mutateAsync({ roomId: row.room.id, period, currentReading, ...(isWaterByMeter ? { currentWaterReading } : {}) }),
    });
  };
  const publishDraft = (values: DraftAmounts) => {
    if (!draftSession?.invoice) return;
    const key = `${draftSession.invoice.id}:${JSON.stringify(values)}`;
    let requestId = publishKeys.current.get(key);
    if (!requestId) { requestId = crypto.randomUUID(); publishKeys.current.set(key, requestId); }
    modal.confirm({
      title: t("Phát hành hóa đơn nháp?"),
      content: t("Khách sẽ thấy hóa đơn sau khi phát hành. Kiểm tra đủ tiền thuê, điện, nước và phí dịch vụ trước khi tiếp tục."),
      okText: t("Phát hành hóa đơn"), cancelText: t("Kiểm tra lại"),
      onOk: () => publishMutation.mutateAsync({ invoice: draftSession.invoice!, values, requestId: requestId!, key }),
    });
  };

  const handleDraftChange = (roomId: string, value: number | null) => {
    userEditedRoomsRef.current.add(roomId);
    setDrafts((current) => ({ ...current, [roomId]: value }));
  };

  const handleWaterDraftChange = (roomId: string, value: number | null) => {
    userEditedWaterRoomsRef.current.add(roomId);
    setWaterDrafts((current) => ({ ...current, [roomId]: value }));
  };

  const columns: TableProps<ReadingRow>['columns'] = [
    {
      title: t("Phòng"),
      key: 'room',
      width: 150,
      render: (_, row) => (
        <div className="room-name-cell">
          <div className="room-avatar"><span>⚡</span></div>
          <div>
            <strong>{row.room.name}</strong>
          </div>
        </div>
      ),
    },
    { title: t("CHỈ SỐ TRƯỚC"), key: 'previous', width: 120, render: (_, row) => {
      const showWater = Boolean(row.saved?.waterByMeter ?? row.room.waterByMeter);
      return (
        <div>
          <span className="meter-value">{row.saved?.previousReading ?? row.room.previousReading ?? row.room.initialReading ?? 0} <small>kWh</small></span>
          {showWater && (
            <div style={{ marginTop: 6 }}>
              <span className="meter-value">{row.saved?.previousWaterReading ?? row.room.previousWaterReading ?? row.room.initialWaterReading ?? 0} <small>m³</small></span>
            </div>
          )}
        </div>
      );
    } },
    { title: t("CHỈ SỐ MỚI"), key: 'current', width: 205, render: (_, row) => {
      const showWater = Boolean(row.saved?.waterByMeter ?? row.room.waterByMeter);
      if (row.saved?.currentReading !== undefined) {
        return (
          <div>
            <div className="saved-reading"><strong>{row.saved.currentReading} <small>kWh</small></strong></div>
            {showWater && row.saved.currentWaterReading != null && (
              <div className="saved-reading" style={{ marginTop: 4 }}><strong>{row.saved.currentWaterReading} <small>m³</small></strong></div>
            )}
          </div>
        );
      }
      return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <InputNumber className="reading-desktop-input" inputMode="numeric" aria-label={t("Chỉ số mới {0}", [row.room.name])} min={row.room.previousReading ?? row.room.initialReading ?? 0} precision={0} step={1} value={drafts[row.room.id] ?? undefined} onChange={(value) => handleDraftChange(row.room.id, value)} addonAfter="kWh" placeholder={t("Nhập chỉ số")} />
          {showWater && (
            <InputNumber className="reading-desktop-input" inputMode="numeric" aria-label={t("Chỉ số nước mới {0}", [row.room.name])} min={row.room.previousWaterReading ?? row.room.initialWaterReading ?? 0} precision={0} step={1} value={waterDrafts[row.room.id] ?? undefined} onChange={(value) => handleWaterDraftChange(row.room.id, value)} addonAfter="m³" placeholder={t("Nhập số nước")} />
          )}
        </div>
      );
    } },
    { title: t("SỬ DỤNG"), key: 'consumption', width: 130, render: (_, row) => {
      const previous = row.saved?.previousReading ?? row.room.previousReading ?? row.room.initialReading ?? 0;
      const current = row.saved?.currentReading ?? drafts[row.room.id];
      const usage = current == null ? null : Math.round(Math.max(0, current - previous) * 1000) / 1000;
      const rate = row.room.electricityRate ?? 0;
      const estimatedCost = usage == null ? null : Math.round(usage * rate);
      const showWater = Boolean(row.saved?.waterByMeter ?? row.room.waterByMeter);
      const prevWater = row.saved?.previousWaterReading ?? row.room.previousWaterReading ?? row.room.initialWaterReading ?? 0;
      const curWater = row.saved?.currentWaterReading ?? waterDrafts[row.room.id];
      const waterUsage = showWater && curWater != null ? Math.round(Math.max(0, curWater - prevWater) * 1000) / 1000 : null;
      const waterCost = waterUsage == null ? null : Math.round(waterUsage * (row.room.waterFee ?? 0));
      return (
        <div>
          <span className="consumption-value">{usage === null ? '—' : `${usage} kWh`}</span>
          {estimatedCost != null && usage !== null && (
            <div style={{ color: '#8a999d', fontSize: '11px', marginTop: 2 }}>
              ≈ {formatVnd(estimatedCost)}
            </div>
          )}
          {showWater && (
            <div style={{ marginTop: 6 }}>
              <span className="consumption-value">{waterUsage === null ? '—' : `${waterUsage} m³`}</span>
              {waterCost != null && waterUsage !== null && (
                <div style={{ color: '#8a999d', fontSize: '11px', marginTop: 2 }}>
                  ≈ {formatVnd(waterCost)}
                </div>
              )}
            </div>
          )}
        </div>
      );
    } },
    { title: t("TRẠNG THÁI PHÁT HÀNH"), key: 'lifecycle', width: 145, render: (_, row) => row.draftInvoice ? <Tag className="draft-state-tag" icon={<ClockCircleOutlined />}>{t("Bản nháp")}</Tag> : row.saved ? <Tag icon={<CheckCircleFilled />} className="submitted-tag">{t("Đã phát hành")}</Tag> : <span>—</span> },
    { title: '', key: 'action', fixed: 'right', width: 175, align: 'right', render: (_, row) => {
      if (row.draftInvoice) return <Button type="primary" size="small" onClick={() => openDraft(row)}>{t("Sửa bản nháp")}</Button>;
      if (row.saved) {
        return (
          <Space size={6}>
            <span className="already-done">{t("Đã phát hành")}</span>
            {row.issuedInvoice && (
              <Button type="default" size="small" onClick={() => setViewingInvoice({ room: row.room, invoice: row.issuedInvoice! })}>
                {t("Chi tiết")}
              </Button>
            )}
          </Space>
        );
      }
      const currentReading = drafts[row.room.id];
      const isWaterByMeter = Boolean(row.room.waterByMeter);
      const currentWaterReading = waterDrafts[row.room.id];
      const invalid = currentReading == null || currentReading < (row.room.previousReading ?? row.room.initialReading ?? 0) ||
        (isWaterByMeter && (currentWaterReading == null || currentWaterReading < (row.room.previousWaterReading ?? row.room.initialWaterReading ?? 0)));
      return (
        <Button
          type="primary"
          size="small"
          loading={readingMutation.isPending && readingMutation.variables?.roomId === row.room.id}
          disabled={invalid || readingMutation.isPending}
          onClick={() => {
            if (currentReading != null && (!isWaterByMeter || currentWaterReading != null)) confirmReading(row, currentReading, currentWaterReading);
          }}
        >
          {t("Chốt & lập hóa đơn")}
        </Button>
      );
    } },
  ];

  const renderCompactReading = (row: ReadingRow) => {
    const previous = row.saved?.previousReading ?? row.room.previousReading ?? row.room.initialReading ?? 0;
    const current = row.saved?.currentReading ?? drafts[row.room.id];
    const usage = current == null ? null : Math.round(Math.max(0, current - previous) * 1000) / 1000;
    const rate = row.room.electricityRate ?? 0;
    const estimatedCost = usage == null ? null : Math.round(usage * rate);
    const currentReading = drafts[row.room.id];
    const isWaterByMeter = Boolean(row.saved?.waterByMeter ?? row.room.waterByMeter);
    const previousWater = row.saved?.previousWaterReading ?? row.room.previousWaterReading ?? row.room.initialWaterReading ?? 0;
    const currentWater = row.saved?.currentWaterReading ?? waterDrafts[row.room.id];
    const waterUsage = isWaterByMeter && currentWater != null ? Math.round(Math.max(0, currentWater - previousWater) * 1000) / 1000 : null;
    const estimatedWaterCost = waterUsage == null ? null : Math.round(waterUsage * (row.room.waterFee ?? 0));
    const currentWaterReading = waterDrafts[row.room.id];
    const invalid = currentReading == null || currentReading < previous ||
      (Boolean(row.room.waterByMeter) && (currentWaterReading == null || currentWaterReading < previousWater));

    return (
      <div className="reading-mobile-card">
        <div className="reading-mobile-head">
          <div className="room-name-cell">
            <div className="room-avatar"><span>⚡</span></div>
            <div>
              <strong>{row.room.name}</strong>
            </div>
          </div>
          {row.draftInvoice ? (
            <Tag className="draft-state-tag" icon={<ClockCircleOutlined />}>{t("Bản nháp")}</Tag>
          ) : row.saved ? (
            <Tag icon={<CheckCircleFilled />} className="submitted-tag">{t("Đã phát hành")}</Tag>
          ) : null}
        </div>
        <div className="reading-mobile-stats">
          <div className="reading-mobile-stat">
            <span>{t("CHỈ SỐ TRƯỚC")}</span>
            <strong className="meter-value">{previous} <small>kWh</small></strong>
            {isWaterByMeter && <strong className="meter-value" style={{ display: 'block', marginTop: 2 }}>{previousWater} <small>m³</small></strong>}
          </div>
          <div className="reading-mobile-stat">
            <span>{t("SỬ DỤNG")}</span>
            <div className="reading-mobile-usage">
              <strong className="consumption-value">{usage === null ? '—' : `${usage} kWh`}</strong>
              {estimatedCost != null && usage !== null && (
                <small className="reading-mobile-cost">≈ {formatVnd(estimatedCost)}</small>
              )}
              {isWaterByMeter && (
                <>
                  <strong className="consumption-value" style={{ display: 'block', marginTop: 2 }}>{waterUsage === null ? '—' : `${waterUsage} m³`}</strong>
                  {estimatedWaterCost != null && waterUsage !== null && (
                    <small className="reading-mobile-cost">≈ {formatVnd(estimatedWaterCost)}</small>
                  )}
                </>
              )}
            </div>
          </div>
        </div>
        {row.saved?.currentReading !== undefined ? (
          <div className="reading-mobile-saved">
            <div>
              <span>{t("CHỈ SỐ MỚI")}</span>
              <div className="saved-reading">
                <strong>{row.saved.currentReading} <small>kWh</small></strong>
                {isWaterByMeter && row.saved.currentWaterReading != null && (
                  <strong style={{ marginLeft: 10 }}>{row.saved.currentWaterReading} <small>m³</small></strong>
                )}
              </div>
            </div>
            {row.draftInvoice ? (
              <Button type="primary" onClick={() => openDraft(row)}>{t("Sửa bản nháp")}</Button>
            ) : row.issuedInvoice ? (
              <Button type="default" size="small" onClick={() => setViewingInvoice({ room: row.room, invoice: row.issuedInvoice! })}>
                {t("Chi tiết")}
              </Button>
            ) : (
              <span className="already-done">{t("Đã phát hành")}</span>
            )}
          </div>
        ) : (
          <div className="reading-mobile-entry">
            <InputNumber
              className="reading-mobile-input"
              inputMode="decimal"
              aria-label={t("Chỉ số mới {0}", [row.room.name])}
              min={previous}
              precision={0}
              step={1}
              value={drafts[row.room.id] ?? undefined}
              onChange={(value) => handleDraftChange(row.room.id, value)}
              addonAfter="kWh"
              placeholder={t("Nhập chỉ số")}
            />
            {row.room.waterByMeter && (
              <InputNumber
                className="reading-mobile-input"
                inputMode="decimal"
                aria-label={t("Chỉ số nước mới {0}", [row.room.name])}
                min={previousWater}
                precision={0}
                step={1}
                value={waterDrafts[row.room.id] ?? undefined}
                onChange={(value) => handleWaterDraftChange(row.room.id, value)}
                addonAfter="m³"
                placeholder={t("Nhập số nước")}
              />
            )}
            <Button
              type="primary"
              className="reading-mobile-submit"
              loading={readingMutation.isPending && readingMutation.variables?.roomId === row.room.id}
              disabled={invalid || readingMutation.isPending}
              onClick={() => {
                if (currentReading != null && (!row.room.waterByMeter || currentWaterReading != null)) confirmReading(row, currentReading, currentWaterReading);
              }}
            >
              {t("Chốt & lập hóa đơn")}
            </Button>
          </div>
        )}
      </div>
    );
  };
  const occupied = buildingRows.length;
  const done = buildingRows.filter((row) => row.saved?.currentReading !== undefined).length;
  const draftSessionAmounts = Form.useWatch([], draftForm) as DraftAmounts | undefined;
  const draftTotal = (draftSessionAmounts?.rent ?? draftSession?.invoice.rent ?? 0) +
    (draftSessionAmounts?.electricityAmount ?? draftSession?.invoice.electricityAmount ?? 0) +
    (draftSessionAmounts?.waterAmount ?? draftSession?.invoice.waterAmount ?? 0) +
    (draftSessionAmounts?.serviceAmount ?? draftSession?.invoice.serviceAmount ?? 0);

  return <>
    <PageTitle
      eyebrow={t("CHỐT SỐ ĐIỆN HÀNG THÁNG")}
      title={t("Lập hóa đơn")}
      subtitle={t("Ghi nhận chỉ số cuối kỳ. Hóa đơn sẽ được lập ngay sau khi bạn chốt.")}
      extra={
        <Space size={8}>
          <Button className="page-back-btn" icon={<ArrowLeftOutlined />} onClick={() => navigate('/invoices')}>
            {t("Hóa đơn")}
          </Button>
          <Select
            aria-label={t("Lọc theo dãy trọ")}
            style={{ minWidth: 200 }}
            value={selectedBuilding}
            onChange={setSelectedBuilding}
            options={buildingOptions}
          />
        </Space>
      }
    />
    {roomsQuery.isError ? <LoadError error={roomsQuery.error} onRetry={() => void roomsQuery.refetch()} /> : readingsQuery.isError ? <LoadError error={readingsQuery.error} onRetry={() => void readingsQuery.refetch()} /> : invoicesQuery.isError ? <LoadError error={invoicesQuery.error} onRetry={() => void invoicesQuery.refetch()} /> : roomsQuery.isPending || readingsQuery.isPending || invoicesQuery.isPending ? <div className="content-card"><div className="loading-placeholder" /></div> : <>
      <div className="reading-progress-card"><div className="reading-progress-mark"><InfoCircleOutlined /></div><div className="reading-progress-copy"><strong>{periodLabel(period)} <span>·</span> {done}/{occupied} {t("phòng đã chốt")}</strong><span>{t("Chỉ số trước được lấy từ lần ghi gần nhất hoặc lúc tạo phòng.")}</span></div><div className="reading-progress-meter"><div style={{ width: `${occupied ? (done / occupied) * 100 : 0}%` }} /></div></div>
      <SectionCard
        className="reading-section"
        title={t("Danh sách phòng đang thuê")}
        extra={
          <Input
            allowClear
            prefix={<SearchOutlined />}
            aria-label={t("Tên phòng")}
            placeholder={t("Tên phòng")}
            value={searchText}
            onChange={(event) => setSearchText(event.target.value)}
            style={{ width: 220 }}
          />
        }
      >
        <div className="reading-notice"><InfoCircleOutlined /><span><strong>{t("Tiền thuê tính đủ theo tháng")}</strong> {t("Tiền thuê được tính đủ theo tháng, không phụ thuộc ngày bắt đầu thuê.")}</span></div>
        {filteredRows.length ? <ResponsiveTable className="reading-table" rowKey={(row) => row.room.id} columns={columns} dataSource={filteredRows} renderCompact={renderCompactReading} pagination={{ pageSize: 10, showSizeChanger: false, hideOnSinglePage: true }} scroll={{ x: 900 }} /> : searchText.trim() ? <EmptyPanel title={t("Không tìm thấy phòng phù hợp")} description={t("Không có phòng khớp với bộ lọc hiện tại.")} action={<Button onClick={() => setSearchText('')}>{t("Xóa lọc")}</Button>} /> : <EmptyPanel title={occupied === 0 ? t("Chưa có phòng đang thuê") : t("Chưa có phòng cần chốt cho {0}", [periodLabel(period)])} description={occupied === 0 ? t("Thêm người thuê vào phòng trước khi ghi chỉ số điện.") : t("Tất cả phòng hiện đã được chốt chỉ số trong kỳ này.")} />}
        {buildingRows.length > 0 && done === buildingRows.length && <div className="all-readings-done"><CheckCircleFilled /> {t("Tất cả phòng đang thuê đã có hóa đơn cho kỳ này.")}</div>}
      </SectionCard>
    </>}
    <Modal className="legacy-draft-modal" title={t("Xem và phát hành bản nháp cũ")} open={!!draftSession} onCancel={() => { if (!publishMutation.isPending) setDraftSession(null); }} footer={null} confirmLoading={publishMutation.isPending} width={620} destroyOnClose>
      {draftSession && <>
        <Alert type="warning" showIcon message={t("Bản nháp chưa phát hành") } description={t("Đây là bản nháp đã lưu trước thay đổi cách tính tiền thuê. Kiểm tra các khoản hiện có; nội dung cũ không tự đổi. Hóa đơn chỉ gửi cho khách sau khi bạn phát hành.")} />
        <div className="legacy-draft-summary"><span>{draftSession.room.name} · {draftSession.room.occupancy?.tenantName}</span><span>{t("Kỳ hóa đơn")} · {periodLabel(draftSession.invoice.period)}</span><span>{t("Chỉ số điện")} {draftSession.invoice.previousReading ?? draftSession.room.previousReading ?? draftSession.room.initialReading ?? 0} → {draftSession.currentReading} kWh</span></div>
        <Form form={draftForm} layout="vertical" requiredMark={false} onFinish={publishDraft} initialValues={{ rent: draftSession.invoice.rent, electricityAmount: draftSession.invoice.electricityAmount, waterAmount: draftSession.invoice.waterAmount, serviceAmount: draftSession.invoice.serviceAmount }}>
          <Form.Item label={t("Tiền thuê phòng") } name="rent" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t("Tiền điện")} name="electricityAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t("Tiền nước") } name="waterAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label={t("Phí dịch vụ")} name="serviceAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
          <div className="legacy-draft-total"><span>{t("Tổng tiền sau khi chỉnh")}</span><strong>{formatVnd(draftTotal)}</strong></div>
        </Form>
        <Typography.Paragraph className="draft-version-note">{t("Phiên bản bản nháp")} {draftSession.invoice.version ?? 0}</Typography.Paragraph>
        <Button type="primary" block loading={publishMutation.isPending} onClick={() => draftForm.submit()}>{t("Phát hành hóa đơn")}</Button>
      </>}
    </Modal>
    <Modal
      title={t("Chi tiết hóa đơn")}
      open={!!viewingInvoice}
      onCancel={() => setViewingInvoice(null)}
      footer={<Button type="primary" onClick={() => setViewingInvoice(null)}>{t("Đóng")}</Button>}
      width={480}
      destroyOnClose
    >
      {viewingInvoice && (
        <div className="reading-confirm-detail">
          <div className="invoice-period-box" style={{ margin: '10px 0 12px' }}>
            <span>{viewingInvoice.room.name} · {viewingInvoice.room.occupancy?.tenantName || viewingInvoice.invoice.tenantName || t("Người thuê")}</span>
            <strong style={{ textAlign: 'right' }}>{periodLabel(viewingInvoice.invoice.period)}</strong>
          </div>
          <div className="invoice-breakdown">
            <div>
              <span>{t("Tiền thuê phòng")}</span>
              <strong>{formatVnd(viewingInvoice.invoice.rent)}</strong>
            </div>
            <div>
              <span>
                {t("Tiền điện")}{' '}
                <small>{viewingInvoice.invoice.consumption} kWh · {viewingInvoice.invoice.previousReading} → {viewingInvoice.invoice.currentReading}</small>
              </span>
              <strong>{formatVnd(viewingInvoice.invoice.electricityAmount)}</strong>
            </div>
            <div>
              <span>
                {t("Tiền nước")}
                {viewingInvoice.invoice.waterByMeter && viewingInvoice.invoice.waterConsumption != null && (
                  <> <small>{viewingInvoice.invoice.waterConsumption} m³ · {viewingInvoice.invoice.previousWaterReading ?? 0} → {viewingInvoice.invoice.currentWaterReading ?? 0}</small></>
                )}
              </span>
              <strong>{formatVnd(viewingInvoice.invoice.waterAmount)}</strong>
            </div>
            <div>
              <span>{t("Phí dịch vụ")}</span>
              <strong>{formatVnd(viewingInvoice.invoice.serviceAmount)}</strong>
            </div>
            <div className="breakdown-total">
              <span>{t("Tổng hóa đơn")}</span>
              <strong style={{ color: '#087c69', fontSize: 16 }}>{formatVnd(viewingInvoice.invoice.total)}</strong>
            </div>
          </div>
          <div className="invoice-balance-box" style={{ marginTop: 12 }}>
            <div>
              <span>{t("Đã ghi nhận")}</span>
              <strong>{formatVnd(viewingInvoice.invoice.paid)}</strong>
            </div>
            <div>
              <span>{t("Còn phải thu")}</span>
              <strong className={viewingInvoice.invoice.outstanding > 0 ? 'balance-due' : 'balance-clear'}>
                {formatVnd(viewingInvoice.invoice.outstanding)}
              </strong>
            </div>
          </div>
        </div>
      )}
    </Modal>
  </>;
}
