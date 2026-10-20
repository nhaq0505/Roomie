import { LineChartOutlined, SettingOutlined, WalletOutlined } from '@ant-design/icons';
import { App, Tabs } from 'antd';
import { useState } from 'react';
import { ApiError } from '../../../shared/http/client';
import { t } from '../../../shared/i18n/runtime';
import { PageTitle } from '../../../shared/ui/Ui';
import { GrowthTab } from './GrowthTab';
import { MonitoringFiltersBar } from './MonitoringFiltersBar';
import { OperationsTab } from './OperationsTab';
import { RevenueTab } from './RevenueTab';
import {
  useCampaignCosts,
  useGrowthMonitoring,
  useMonitoringIncidents,
  useOperationsMonitoring,
  useRevenueMonitoring,
  useUpdateIncident,
  useUpsertCampaignCost,
} from './api';
import type { MonitoringFilters, UpsertCampaignCostRequest } from './types';

function getInitialDates(): [string, string] {
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

  // Default: 30 days
  const today = new Date(todayStr);
  const from = new Date(today);
  from.setDate(from.getDate() - 29);
  const fromStr = from.toISOString().split('T')[0];
  return [fromStr, todayStr];
}

export default function AdminMonitoringPage() {
  const { message } = App.useApp();
  const [activeTab, setActiveTab] = useState<'growth' | 'revenue' | 'operations'>('growth');

  const [dateFrom, dateTo] = getInitialDates();
  const [filters, setFilters] = useState<MonitoringFilters>({
    rangePreset: '30d',
    dateFrom,
    dateTo,
    source: undefined,
    channel: undefined,
    campaign: undefined,
    opsWindow: '1h',
  });

  const [lastUpdatedAt, setLastUpdatedAt] = useState<Date | null>(new Date());

  const filterRequest = {
    fromDate: filters.dateFrom,
    toDate: filters.dateTo,
    source: filters.source,
    channel: filters.channel,
    campaign: filters.campaign,
  };

  const campaignCostsFilter = {
    fromDate: filters.dateFrom,
    toDate: filters.dateTo,
    campaign: filters.campaign,
  };

  // Queries
  const growthQuery = useGrowthMonitoring(filterRequest, activeTab === 'growth');
  const revenueQuery = useRevenueMonitoring(filterRequest, activeTab === 'revenue');
  const operationsQuery = useOperationsMonitoring(filters.opsWindow, activeTab === 'operations');
  const incidentsQuery = useMonitoringIncidents(activeTab === 'operations');
  const campaignCostsQuery = useCampaignCosts(campaignCostsFilter, activeTab === 'revenue');

  // Mutations
  const upsertCostMutation = useUpsertCampaignCost();
  const updateIncidentMutation = useUpdateIncident();

  const handleRefresh = async () => {
    if (activeTab === 'growth') {
      await growthQuery.refetch();
    } else if (activeTab === 'revenue') {
      await Promise.all([revenueQuery.refetch(), campaignCostsQuery.refetch()]);
    } else if (activeTab === 'operations') {
      await Promise.all([operationsQuery.refetch(), incidentsQuery.refetch()]);
    }
    setLastUpdatedAt(new Date());
  };

  const handleAddCampaignCost = async (input: UpsertCampaignCostRequest) => {
    try {
      await upsertCostMutation.mutateAsync(input);
      message.success(t("Đã lưu thông tin chi phí chiến dịch."));
    } catch (err) {
      const msg = err instanceof Error ? err.message : t("Không thể lưu chi phí chiến dịch.");
      message.error(msg);
    }
  };

  const handleUpdateIncident = async (id: string, state: string) => {
    try {
      await updateIncidentMutation.mutateAsync({ id, body: { state } });
      message.success(t("Đã cập nhật trạng thái sự cố."));
    } catch (err) {
      const msg = err instanceof Error ? err.message : t("Không thể cập nhật sự cố.");
      message.error(msg);
    }
  };

  const is501Error = (err: unknown) => err instanceof ApiError && err.status === 501;

  const tabItems = [
    {
      key: 'growth',
      label: (
        <span>
          <LineChartOutlined style={{ marginRight: 6 }} />
          {t("Tăng trưởng & Sử dụng")}
        </span>
      ),
      children: (
        <GrowthTab
          data={growthQuery.data}
          isLoading={growthQuery.isLoading}
          error={growthQuery.error}
          is501={is501Error(growthQuery.error)}
          onRetry={() => void growthQuery.refetch()}
        />
      ),
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
          data={revenueQuery.data}
          campaignCostsList={campaignCostsQuery.data}
          isLoading={revenueQuery.isLoading || campaignCostsQuery.isLoading}
          error={revenueQuery.error}
          is501={is501Error(revenueQuery.error)}
          onRetry={() => {
            void revenueQuery.refetch();
            void campaignCostsQuery.refetch();
          }}
          onUpsertCampaignCost={handleAddCampaignCost}
          isSavingCost={upsertCostMutation.isPending}
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
      children: (
        <OperationsTab
          data={operationsQuery.data}
          incidents={incidentsQuery.data?.incidents}
          window={filters.opsWindow}
          isLoading={operationsQuery.isLoading || incidentsQuery.isLoading}
          error={operationsQuery.error}
          is501={is501Error(operationsQuery.error)}
          onRetry={() => {
            void operationsQuery.refetch();
            void incidentsQuery.refetch();
          }}
          onUpdateIncidentState={handleUpdateIncident}
        />
      ),
    },
  ];

  const isLoading =
    (activeTab === 'growth' && growthQuery.isFetching) ||
    (activeTab === 'revenue' && (revenueQuery.isFetching || campaignCostsQuery.isFetching)) ||
    (activeTab === 'operations' && (operationsQuery.isFetching || incidentsQuery.isFetching));

  return (
    <div className="admin-monitoring-page">
      <PageTitle
        eyebrow="Admin"
        title={t("Giám sát hệ thống")}
        subtitle={t("Số liệu kinh doanh 30 ngày, vận hành theo lịch Việt Nam")}
      />

      <MonitoringFiltersBar
        filters={filters}
        onChange={setFilters}
        onRefresh={() => void handleRefresh()}
        isLoading={isLoading}
        activeTab={activeTab}
        lastUpdatedAt={lastUpdatedAt}
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
