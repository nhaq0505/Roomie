import { ReloadOutlined } from '@ant-design/icons';
import { Button, DatePicker, Radio, Select, Space } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { dateFormat, t } from '../../../shared/i18n/runtime';
import type { DateRangePreset, MonitoringFilters, OpsWindowPreset } from './types';

interface MonitoringFiltersBarProps {
  filters: MonitoringFilters;
  onChange: (updated: MonitoringFilters) => void;
  onRefresh: () => void;
  isLoading?: boolean;
  activeTab: 'growth' | 'revenue' | 'operations';
}

function calculatePresetDates(preset: DateRangePreset): [string, string] {
  // Use Vietnam timezone Asia/Ho_Chi_Minh
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
  // Default: 30 days
  return [today.subtract(29, 'day').format('YYYY-MM-DD'), todayStr];
}

export function MonitoringFiltersBar({
  filters,
  onChange,
  onRefresh,
  isLoading,
  activeTab,
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
    onChange({ ...filters, source: val });
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
  ];

  return (
    <div className="monitoring-filters-bar">
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
              />
            )}

            <Select
              style={{ width: 150 }}
              value={filters.source ?? ''}
              onChange={handleSourceChange}
              options={sourceOptions}
              placeholder={t("Nguồn / Kênh")}
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
              <Radio.Button value="6h">{t("6 giờ")}</Radio.Button>
              <Radio.Button value="24h">{t("24 giờ")}</Radio.Button>
            </Radio.Group>
          </Space>
        )}

        <Button icon={<ReloadOutlined />} onClick={onRefresh} loading={isLoading}>
          {t("Làm mới")}
        </Button>
      </Space>
    </div>
  );
}
