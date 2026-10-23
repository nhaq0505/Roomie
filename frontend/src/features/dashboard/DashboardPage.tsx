import { ArrowRightOutlined, BankOutlined, CalculatorOutlined, FileTextOutlined, HomeOutlined, LinkOutlined, PlusOutlined, RiseOutlined, WarningOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, Col, Progress, Row, Skeleton, Statistic, Typography } from 'antd';
import { useMemo } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { currentPeriod, formatVnd, periodLabel } from '../../shared/format';
import { getLocale, t } from '../../shared/i18n/runtime';
import { ActiveBannerStrip } from '../../shared/ui/ActiveBannerStrip';
import { LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { invoicesApi } from '../invoices/index';
import { roomsApi } from '../rooms/index';
import { dashboardApi } from './index';

export default function DashboardPage() {
  const navigate = useNavigate();
  const dashboardQuery = useQuery({ queryKey: ['dashboard'], queryFn: dashboardApi });
  const roomsQuery = useQuery({ queryKey: ['rooms'], queryFn: () => roomsApi.list() });
  const loading = dashboardQuery.isPending || roomsQuery.isPending;
  const summary = dashboardQuery.data;
  const invoicesQuery = useQuery({ queryKey: ['invoices', summary?.period], queryFn: () => invoicesApi.list(summary!.period), enabled: !!summary });
  const rooms = roomsQuery.data ?? [];
  const occupancyPct = summary?.totalRooms ? Math.round((summary.occupiedRooms / summary.totalRooms) * 100) : 0;
  const todayLabel = new Intl.DateTimeFormat(getLocale(), { timeZone: 'Asia/Ho_Chi_Minh', weekday: 'long', day: '2-digit', month: 'long' }).format(new Date());
  const recentInvoices = useMemo(() => [...(invoicesQuery.data ?? [])].sort((a, b) => (b.issuedAt ?? '').localeCompare(a.issuedAt ?? '')).slice(0, 5), [invoicesQuery.data]);

  if (dashboardQuery.isError) return <><PageTitle eyebrow={t("TỔNG QUAN")} title={t("Chào mừng trở lại")} subtitle={t("Tình hình nhà trọ của bạn, trong nháy mắt.")} /><LoadError error={dashboardQuery.error} onRetry={() => void dashboardQuery.refetch()} /></>;
  return <>
    <ActiveBannerStrip placement="OwnerDashboard" />
    <PageTitle eyebrow={todayLabel} title={t("Chào mừng trở lại")} subtitle={t("Tình hình nhà trọ của bạn, trong nháy mắt.")} extra={<Button type="primary" icon={<CalculatorOutlined />} onClick={() => navigate('/readings')}>{t("Nhập chỉ số tháng này")}</Button>} />
    <SectionCard title={t("Thao tác nhanh")} className="quick-actions-card" extra={<Button onClick={() => navigate('/settings/payment')}>{t("Cài đặt thanh toán")}</Button>}><div className="quick-actions"><Link to="/buildings?create=1"><BankOutlined /><strong>{t("Tạo dãy trọ")}</strong><span>{t("Thêm địa điểm cho thuê")}</span><PlusOutlined /></Link><Link to="/rooms?create=1"><HomeOutlined /><strong>{t("Tạo phòng trọ")}</strong><span>{t("Thiết lập giá thuê và phí")}</span><PlusOutlined /></Link><Link to="/rooms?links=1"><LinkOutlined /><strong>{t("Cấp link cho khách")}</strong><span>{t("Chọn phòng đang có người thuê")}</span><ArrowRightOutlined /></Link></div></SectionCard>
    {loading ? <div className="content-card"><Skeleton active paragraph={{ rows: 8 }} /></div> : <>
      <Row gutter={[16, 16]} className="stat-grid">
        <Col xs={12} xl={6}><Card className="stat-card"><div className="stat-icon stat-blue"><FileTextOutlined /></div><Statistic title={t("Tổng phải thu tháng này")} value={summary?.monthlyBilled ?? 0} formatter={(v) => formatVnd(Number(v))} /><div className="stat-foot"><span className="stat-muted">{t("Đã phát hành")}</span></div></Card></Col>
        <Col xs={12} xl={6}><Card className="stat-card"><div className="stat-icon stat-green"><RiseOutlined /></div><Statistic title={t("Đã thu tháng này")} value={summary?.monthlyPaid ?? 0} formatter={(v) => formatVnd(Number(v))} /><div className="stat-foot"><span className="stat-positive">{t("Khoản đã ghi nhận")}</span></div></Card></Col>
        <Col xs={12} xl={6}><Card className="stat-card"><div className="stat-icon stat-amber"><WarningOutlined /></div><Statistic title={t("Còn phải thu")} value={summary?.monthlyOutstanding ?? 0} formatter={(v) => formatVnd(Number(v))} /><div className="stat-foot"><span className="stat-muted">{t("Công nợ của hóa đơn tháng này")}</span></div></Card></Col>
        <Col xs={12} xl={6}><Card className="stat-card"><div className="stat-icon stat-teal"><HomeOutlined /></div><Statistic title={t("Tỷ lệ thu tiền")} value={summary?.collectionRate ?? 0} precision={2} suffix="%" /><div className="stat-foot"><span className="stat-muted">{periodLabel(summary?.period ?? currentPeriod())}</span></div></Card></Col>
      </Row>
      <SectionCard title={t("Thống kê toàn thời gian")}><Typography.Paragraph>{t("Tổng phải thu: {0} · Đã thu: {1} · Còn phải thu: {2}", [formatVnd(summary?.totalBilled ?? 0), formatVnd(summary?.totalPaid ?? 0), formatVnd(summary?.totalOutstanding ?? 0)])}</Typography.Paragraph></SectionCard>
      <Row gutter={[18, 18]} className="dashboard-main-grid">
        <Col xs={24} xl={15}><SectionCard title={t("Việc cần làm")} extra={<span className="section-caption">{periodLabel(summary?.period ?? currentPeriod())}</span>}>
          <div className="task-list">
            <Link className="task-item" to="/readings"><div className="task-symbol task-symbol-amber"><CalculatorOutlined /></div><div className="task-copy"><strong>{t("Nhập chỉ số điện")}</strong><span>{summary?.pendingReadings ?? 0}  {t("phòng cần ghi chỉ số tháng này")}</span></div><ArrowRightOutlined className="task-arrow" /></Link>
            <Link className="task-item" to="/invoices"><div className="task-symbol task-symbol-teal"><FileTextOutlined /></div><div className="task-copy"><strong>{t("Tạo hóa đơn tháng")}</strong><span>{t("Kiểm tra chỉ số rồi phát hành hóa đơn cho người thuê")}</span></div><ArrowRightOutlined className="task-arrow" /></Link>
            <Link className="task-item" to="/rooms"><div className="task-symbol task-symbol-blue"><HomeOutlined /></div><div className="task-copy"><strong>{t("Cập nhật phòng trống")}</strong><span>{t("Quản lý người đang thuê và ngày chuyển vào")}</span></div><ArrowRightOutlined className="task-arrow" /></Link>
          </div>
        </SectionCard></Col>
        <Col xs={24} xl={9}><SectionCard title={t("Tình trạng phòng")} extra={<Link to="/rooms">{t("Xem phòng")} <ArrowRightOutlined /></Link>}>
          <div className="occupancy-summary"><div><Typography.Text type="secondary">{t("Tỷ lệ đang thuê")}</Typography.Text><div className="occupancy-percent">{occupancyPct}<small>%</small></div></div><Progress type="circle" percent={occupancyPct} size={92} strokeWidth={10} strokeColor="#0f9d83" trailColor="#eaf0ee" format={() => `${summary?.occupiedRooms ?? 0}/${summary?.totalRooms ?? 0}`} /></div>
          <div className="occupancy-stats"><div><span className="legend-dot dot-teal" />{t("Đang thuê")} <strong>{summary?.occupiedRooms ?? 0}</strong></div><div><span className="legend-dot dot-gray" />{t("Còn trống")} <strong>{Math.max(0, (summary?.totalRooms ?? 0) - (summary?.occupiedRooms ?? 0))}</strong></div></div>
          <div className="mini-room-note"><BankOutlined /> {rooms.length}  {t("phòng đã được thêm vào hệ thống")}</div>
        </SectionCard></Col>
      </Row>
      <SectionCard title={t("Hóa đơn gần đây")} extra={<Button type="link" onClick={() => navigate('/invoices')}>{t("Tất cả hóa đơn")} <ArrowRightOutlined /></Button>}>
        {invoicesQuery.isError ? <LoadError error={invoicesQuery.error} onRetry={() => void invoicesQuery.refetch()} /> : invoicesQuery.isPending ? <Skeleton active paragraph={{ rows: 3 }} /> : recentInvoices.length ? <div className="recent-invoice-list">{recentInvoices.map((invoice) => <Link key={invoice.id} className="recent-invoice-row" to={`/invoices?invoice=${encodeURIComponent(invoice.id)}`}><div className="invoice-room-avatar">{invoice.roomName?.slice(0, 2)?.toUpperCase() ?? 'P'}</div><div className="recent-invoice-room"><strong>{invoice.roomName}</strong><span>{invoice.buildingName || t("Nhà trọ")} · {invoice.tenantName || t("Chưa có tên người thuê")}</span></div><div className="recent-invoice-period">{invoice.kind === 'Deposit' ? t("Tiền cọc nhận phòng") : invoice.kind === 'Supplemental' ? t("Hóa đơn bổ sung") : periodLabel(invoice.period)}</div><div className="recent-invoice-amount"><strong>{formatVnd(invoice.total)}</strong><span className={`status-pill status-${invoice.status?.toLowerCase()}`}>{invoice.status === 'Paid' ? t("Đã thanh toán") : invoice.status === 'Partial' ? t("Thanh toán một phần") : t("Chưa thanh toán")}</span></div></Link>)}</div> : <div className="inline-empty"><FileTextOutlined /><div><strong>{t("Chưa có hóa đơn trong tháng này")}</strong><span>{t("Ghi chỉ số điện để bắt đầu tạo hóa đơn.")}</span></div><Button type="link" onClick={() => navigate('/readings')}>{t("Nhập chỉ số")}</Button></div>}
      </SectionCard>
    </>}
  </>;
}
