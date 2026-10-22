import { ClockCircleOutlined, ReloadOutlined } from '@ant-design/icons';
import { Button, DatePicker, Input, Radio, Select, Space, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useEffect, useRef, useState } from 'react';
import { dateFormat, t } from '../../../shared/i18n/runtime';
import type { DateRangePreset, MonitoringFilters, OpsWindowPreset } from './types';

interface MonitoringFiltersBarProps {
  filters: MonitoringFilters;
  onChange: (updated: MonitoringFilters) => void;
  onRefresh: () => void;
  isLoading?: boolean;
  activeTab: 'growth' | 'revenue' | 'operations';
  lastUpdatedAt?: Date | null;
}

function calculatePresetDates(preset: DateRangePreset): [string, string] {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());

  const year = parts.find((p) => p.type === 'year')?.value ?? '2026';
  const month = parts.find((p) => p.type === 'month')?.value ?? '10';
  const day = parts.find((p) => p.type === 'day')?.value ?? '20';
  const todayStr = `${year}-${month}-${day}`;
  const today = dayjs(todayStr);

  if (preset === '7d') {
    return [today.subtract(6, 'day').format('YYYY-MM-DD'), todayStr];
  }
  if (preset === '90d') {
    return [today.subtract(89, 'day').format('YYYY-MM-DD'), todayStr];
  }
  return [today.subtract(29, 'day').format('YYYY-MM-DD'), todayStr];
}

export function MonitoringFiltersBar({
  filters,
  onChange,
  onRefresh,
  isLoading,
  activeTab,
  lastUpdatedAt,
}: MonitoringFiltersBarProps) {
  const handlePresetChange = (preset: DateRangePreset) => {
    if (preset === 'custom') {
      onChange({ ...filters, rangePreset: preset });
    } else {
      const [dateFrom, dateTo] = calculatePresetDates(preset);
      onChange({ ...filters, rangePreset: preset, dateFrom, dateTo });
    }
  };

  const handleCustomRange = (dates: [Dayjs | null, Dayjs | null] | null) => {
    if (dates && dates[0] && dates[1]) {
      onChange({
        ...filters,
        dateFrom: dates[0].format('YYYY-MM-DD'),
        dateTo: dates[1].format('YYYY-MM-DD'),
      });
    }
  };

  const handleSourceChange = (val?: string) => {
    onChange({ ...filters, source: val || undefined });
  };

  const [campaignInput, setCampaignInput] = useState(filters.campaign ?? '');
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    setCampaignInput(filters.campaign ?? '');
  }, [filters.campaign]);

  useEffect(() => {
    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, []);

  const flushCampaignChange = (val: string) => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
      debounceTimerRef.current = null;
    }
    const trimmed = val.trim() || undefined;
    if (trimmed !== filters.campaign) {
      onChange({ ...filters, campaign: trimmed });
    }
  };

  const handleCampaignChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const val = e.target.value;
    setCampaignInput(val);
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }
    debounceTimerRef.current = setTimeout(() => {
      debounceTimerRef.current = null;
      const trimmed = val.trim() || undefined;
      if (trimmed !== filters.campaign) {
        onChange({ ...filters, campaign: trimmed });
      }
    }, 300);
  };

  const handleOpsWindowChange = (val: OpsWindowPreset) => {
    onChange({ ...filters, opsWindow: val });
  };

  const sourceOptions = [
    { value: '', label: t("Tất cả nguồn") },
    { value: 'direct', label: '(direct)' },
    { value: 'google', label: 'google' },
    { value: 'facebook', label: 'facebook' },
    { value: 'zalo', label: 'zalo' },
    { value: 'tiktok', label: 'tiktok' },
  ];

  const formatLastUpdated = (date: Date) => {
    return new Intl.DateTimeFormat('vi-VN', {
      timeZone: 'Asia/Ho_Chi_Minh',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    }).format(date);
  };

  return (
    <div
      className="monitoring-filters-bar"
      style={{
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        flexWrap: 'wrap',
        gap: 12,
        padding: '12px 16px',
        background: '#fff',
        borderRadius: 12,
        marginBottom: 20,
        border: '1px solid #e8f0ee',
      }}
    >
      <Space wrap size={[12, 12]}>
        {activeTab !== 'operations' ? (
          <>
            <Radio.Group
              value={filters.rangePreset}
              onChange={(e) => handlePresetChange(e.target.value as DateRangePreset)}
              buttonStyle="solid"
              size="middle"
            >
              <Radio.Button value="7d">{t("7 ngày")}</Radio.Button>
              <Radio.Button value="30d">{t("30 ngày")}</Radio.Button>
              <Radio.Button value="90d">{t("90 ngày")}</Radio.Button>
              <Radio.Button value="custom">{t("Tùy chọn")}</Radio.Button>
            </Radio.Group>

            {filters.rangePreset === 'custom' && (
              <DatePicker.RangePicker
                value={[dayjs(filters.dateFrom), dayjs(filters.dateTo)]}
                onChange={handleCustomRange}
                format={dateFormat()}
                allowClear={false}
                disabledDate={(current) => current && current > dayjs().endOf('day')}
                maxDate={dayjs()}
              />
            )}

            <Select
              style={{ width: 140 }}
              value={filters.source ?? ''}
              onChange={handleSourceChange}
              options={sourceOptions}
              placeholder={t("Nguồn / Kênh")}
            />

            <Input
              style={{ width: 160 }}
              value={campaignInput}
              onChange={handleCampaignChange}
              onBlur={() => flushCampaignChange(campaignInput)}
              onPressEnter={() => flushCampaignChange(campaignInput)}
              placeholder={t("Chiến dịch")}
              allowClear
            />
          </>
        ) : (
          <Space size={8}>
            <span style={{ fontSize: 13, color: '#7b8c91' }}>{t("Cửa sổ vận hành")}:</span>
            <Radio.Group
              value={filters.opsWindow}
              onChange={(e) => handleOpsWindowChange(e.target.value as OpsWindowPreset)}
              buttonStyle="solid"
              size="middle"
            >
              <Radio.Button value="1h">{t("1 giờ")}</Radio.Button>
              <Radio.Button value="24h">{t("24 giờ")}</Radio.Button>
              <Radio.Button value="7d">{t("7 ngày")}</Radio.Button>
            </Radio.Group>
          </Space>
        )}
      </Space>

      <Space size={12} wrap>
        {lastUpdatedAt && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            <ClockCircleOutlined style={{ marginRight: 4 }} />
            <span>{formatLastUpdated(lastUpdatedAt)} (UTC+7)</span>
          </Typography.Text>
        )}

        <Button
          icon={<ReloadOutlined />}
          onClick={onRefresh}
          loading={isLoading}
        >
          {t("Làm mới")}
        </Button>
      </Space>
    </div>
  );
}
