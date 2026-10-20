import { LineChartOutlined, SettingOutlined, WalletOutlined } from '@ant-design/icons';
import { App, Tabs } from 'antd';
import { useState } from 'react';
import { t } from '../../../shared/i18n/runtime';
import { PageTitle } from '../../../shared/ui/Ui';
import { GrowthTab } from './GrowthTab';
import { MonitoringFiltersBar } from './MonitoringFiltersBar';
import { UnconfiguredNotice } from './MonitoringState';
import { OperationsTab } from './OperationsTab';
import { RevenueTab } from './RevenueTab';
import type {
  CampaignCostEntry,
  CreateCampaignCostInput,
  MonitoringFilters,
} from './types';

export default function AdminMonitoringPage() {
  const { message } = App.useApp();
  const [activeTab, setActiveTab] = useState<'growth' | 'revenue' | 'operations'>('growth');

  const [filters, setFilters] = useState<MonitoringFilters>({
    rangePreset: '30d',
    dateFrom: '2026-09-20',
    dateTo: '2026-10-20',
    source: '',
    opsWindow: '1h',
  });

  const [campaignCosts, setCampaignCosts] = useState<CampaignCostEntry[]>([]);

  const handleAddCampaignCost = (input: CreateCampaignCostInput) => {
    const newEntry: CampaignCostEntry = {
      id: `cost_${Date.now()}`,
      campaignName: input.campaignName,
      channel: input.channel,
      recordedDate: input.recordedDate,
      cashExpenseVnd: input.cashExpenseVnd,
      personHours: input.personHours,
      hourlyRateVnd: input.hourlyRateVnd,
      totalCostVnd: input.cashExpenseVnd + input.personHours * (input.hourlyRateVnd || 0),
      updatedBy: input.updatedBy,
      updatedAt: new Date().toISOString(),
      notes: input.notes,
    };
    setCampaignCosts((prev) => [newEntry, ...prev]);
    message.success(t("Đã lưu thông tin chi phí chiến dịch."));
  };

  const handleRefresh = () => {
    // Refresh handler - ready for React Query invalidation when Task T2 endpoints connect
  };

  const tabItems = [
    {
      key: 'growth',
      label: (
        <span>
          <LineChartOutlined style={{ marginRight: 6 }} />
          {t("Tăng trưởng & Sử dụng")}
        </span>
      ),
      children: <GrowthTab />,
    },
    {
      key: 'revenue',
      label: (
        <span>
          <WalletOutlined style={{ marginRight: 6 }} />
          {t("Doanh thu")}
        </span>
      ),
      children: (
        <RevenueTab
          campaignCosts={campaignCosts}
          onAddCampaignCost={handleAddCampaignCost}
        />
      ),
    },
    {
      key: 'operations',
      label: (
        <span>
          <SettingOutlined style={{ marginRight: 6 }} />
          {t("Vận hành")}
        </span>
      ),
      children: <OperationsTab />,
    },
  ];

  return (
    <div className="admin-monitoring-page">
      <PageTitle
        eyebrow="Admin"
        title={t("Giám sát hệ thống")}
        subtitle="Vietnam calendar, 30-day business / 1-hour ops"
      />

      <UnconfiguredNotice />

      <MonitoringFiltersBar
        filters={filters}
        onChange={setFilters}
        onRefresh={handleRefresh}
        activeTab={activeTab}
      />

      <Tabs
        activeKey={activeTab}
        onChange={(k) => setActiveTab(k as 'growth' | 'revenue' | 'operations')}
        items={tabItems}
        size="large"
        className="monitoring-tabs"
      />
    </div>
  );
}
