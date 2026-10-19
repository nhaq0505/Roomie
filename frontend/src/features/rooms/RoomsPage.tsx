import { CrownOutlined, HomeOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, ConfigProvider, Grid, Select, Space, Tag, Typography } from 'antd';
import { Link } from 'react-router-dom';
import { t } from '../../shared/i18n/runtime';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { EmptyPanel, LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import RoomDialogs from './RoomDialogs';
import './RoomsPage.css';
import { roomTheme } from './theme';
import { useRoomsPage } from './useRoomsPage';

export default function RoomsPage() {
  const screens = Grid.useBreakpoint();
  return <ConfigProvider theme={roomTheme(!screens.md)}><App component={false}><RoomsContent /></App></ConfigProvider>;
}

function RoomsContent() {
const state = useRoomsPage();
  const { navigate, setSearchParams, archiveFilter, setArchiveFilter, buildingFilter, statusFilter, setStatusFilter, buildingsQuery, roomsQuery, isPro, maxRooms, activeRoomCount, isQuotaReached, buildingRooms, rooms, occupiedCount, vacancyCount, activeBuildings, openCreate, compactRoom, columns, clearFilters } = state;
  return <div className="rooms-workspace">
    <PageTitle
      title={t("Phòng & người thuê")}
      subtitle={t("Quản lý phòng, người thuê và link hóa đơn.")}
      extra={archiveFilter === 'Active' ? (
        <Space wrap>
          {isPro ? (
            <Tag color="gold" icon={<CrownOutlined />} style={{ padding: '4px 8px' }}>
              PRO ({activeRoomCount} {t("phòng")})
            </Tag>
          ) : (
            <Tag color={isQuotaReached ? 'error' : 'default'} style={{ padding: '4px 8px' }}>
              {activeRoomCount} / {maxRooms} {t("phòng (Gói Miễn phí)")}
            </Tag>
          )}
          <Button
            type="primary"
            icon={isQuotaReached ? <CrownOutlined /> : <PlusOutlined />}
            onClick={isQuotaReached ? () => navigate('/account-status') : openCreate}
            disabled={!activeBuildings.length && !isQuotaReached}
          >
            {isQuotaReached ? t("Nâng cấp tài khoản") : t("Tạo phòng trọ")}
          </Button>
        </Space>
      ) : undefined}
    />
    {isQuotaReached && archiveFilter === 'Active' && (
      <Alert
        style={{ marginBottom: 16 }}
        type="warning"
        showIcon
        message={t("Đã đạt giới hạn phòng của gói Miễn phí")}
        description={
          <span>
            {t("Gói Miễn phí chỉ cho phép quản lý tối đa {0} phòng. Vui lòng nâng cấp lên gói Pro để tạo thêm phòng không giới hạn.", [maxRooms])}{' '}
            <Link to="/account-status"><strong>{t("Nâng cấp ngay")}</strong></Link>
          </span>
        }
      />
    )}
    {!buildingsQuery.isPending && !buildingsQuery.isError && activeBuildings.length === 0 && archiveFilter === 'Active' && <AlertBanner onCreate={() => navigate('/buildings?create=1')} />}
    {buildingsQuery.isError && <LoadError error={buildingsQuery.error} onRetry={() => void buildingsQuery.refetch()} />}
    {roomsQuery.isError ? <LoadError error={roomsQuery.error} onRetry={() => void roomsQuery.refetch()} /> : <SectionCard className="room-directory">
      <div className="room-directory-toolbar">
        <label className="room-filter"><span>{t("Trạng thái lưu trữ")}</span><Select aria-label={t("Lọc trạng thái lưu trữ")} value={archiveFilter} onChange={(value) => setArchiveFilter(value)} options={[{ value: 'Active', label: t("Đang hoạt động ({0})", [(roomsQuery.data ?? []).filter((room) => !room.isArchived).length]) }, { value: 'Archived', label: t("Đã lưu trữ ({0})", [(roomsQuery.data ?? []).filter((room) => room.isArchived).length]) }]} /></label>
        <label className="room-filter"><span>{t("Trạng thái")}</span><Select aria-label={t("Lọc theo tình trạng")} value={statusFilter} onChange={setStatusFilter} options={[{ value: 'all', label: t("Tất cả ({0})", [buildingRooms.length]) }, { value: 'occupied', label: t("Đang thuê ({0})", [occupiedCount]) }, { value: 'vacant', label: t("Còn trống ({0})", [vacancyCount]) }]} /></label>
        <label className="room-filter"><span>{t("Dãy trọ")}</span><Select aria-label={t("Lọc theo dãy trọ")} allowClear placeholder={t("Tất cả dãy trọ")} value={buildingFilter} onChange={(value) => { setSearchParams(value ? { building: value } : {}); }} options={[...(buildingsQuery.data ?? []).map((building) => ({ value: building.id, label: building.name })), ...(buildingFilter && buildingsQuery.isSuccess && !buildingsQuery.data.some((building) => building.id === buildingFilter) ? [{ value: buildingFilter, label: t("Dãy không còn tồn tại") }] : [])]} /></label>
        <div className="room-filter-result" data-filtered={!!buildingFilter || statusFilter !== 'all' || archiveFilter !== 'Active'}><span aria-live="polite">{roomsQuery.isPending ? t("Đang tải phòng…") : t("{0} phòng", [rooms.length])}</span>{rooms.length > 0 && (buildingFilter || statusFilter !== 'all' || archiveFilter !== 'Active') && <Button type="text" onClick={clearFilters}>{t("Xóa lọc")}</Button>}</div>
      </div>
      {roomsQuery.isPending ? <div className="room-list-loading"><div className="loading-placeholder" /></div> : rooms.length ? <ResponsiveTable className="room-table" rowKey="id" columns={columns} dataSource={rooms} renderCompact={compactRoom} pagination={{ pageSize: 8, showSizeChanger: false, hideOnSinglePage: true }} scroll={{ x: 900 }} /> : <EmptyPanel title={archiveFilter === 'Archived' ? t("Chưa có phòng được lưu trữ") : (roomsQuery.data?.length ?? 0) === 0 ? t("Chưa có phòng nào") : t("Không tìm thấy phòng phù hợp")} description={archiveFilter === 'Archived' ? t("Phòng đã lưu trữ sẽ xuất hiện ở đây để khôi phục.") : (roomsQuery.data?.length ?? 0) === 0 ? t("Tạo phòng trong một dãy trọ để bắt đầu quản lý người thuê.") : t("Không có phòng khớp với bộ lọc hiện tại.")} action={archiveFilter === 'Active' && (roomsQuery.data?.length ?? 0) === 0 ? <Button type="primary" icon={<PlusOutlined />} disabled={!activeBuildings.length} onClick={openCreate}>{t("Tạo phòng trọ")}</Button> : rooms.length === 0 && (buildingFilter || statusFilter !== 'all' || archiveFilter !== 'Active') ? <Button onClick={clearFilters}>{t("Xóa lọc")}</Button> : undefined} />}
    </SectionCard>}
    <RoomDialogs state={state} />
  </div>;
}

function AlertBanner({ onCreate }: { onCreate: () => void }) {
  return <Card className="empty-building-banner" bordered={false}><div className="empty-building-content"><div className="summary-icon"><HomeOutlined /></div><div><Typography.Text strong>{t("Hãy tạo dãy trọ trước")}</Typography.Text><Typography.Paragraph>{t("Phòng cần nằm trong một dãy trọ để bạn dễ phân loại và quản lý.")}</Typography.Paragraph></div><Button type="primary" onClick={onCreate}>{t("Tạo dãy trọ")}</Button></div></Card>;
}
