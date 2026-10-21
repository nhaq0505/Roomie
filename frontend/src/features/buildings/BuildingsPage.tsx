import { ArrowRightOutlined, BankOutlined, EditOutlined, EnvironmentOutlined, HomeOutlined, PlusOutlined, RollbackOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Col, Form, Input, Modal, Row, Select, Space, Spin, Tag, Typography } from 'antd';
import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { t } from '../../shared/i18n/runtime';
import { EmptyPanel, LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { roomsApi } from '../rooms/index';
import { buildingsApi, type Building } from './index';

type ArchiveFilter = 'Active' | 'Archived';

export default function BuildingsPage() {
  const { message, modal } = App.useApp();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [open, setOpen] = useState(false);
  const [editingBuilding, setEditingBuilding] = useState<Building | null>(null);
  const [archiveFilter, setArchiveFilter] = useState<ArchiveFilter>('Active');
  const [form] = Form.useForm<{ name: string; address: string }>();
  const [editForm] = Form.useForm<{ name: string; address: string }>();
  const buildingsQuery = useQuery({ queryKey: ['buildings', 'archive-view'], queryFn: () => buildingsApi.list(true) });
  const roomsQuery = useQuery({ queryKey: ['rooms', 'archive-view'], queryFn: () => roomsApi.list(undefined, true) });
  const createMutation = useMutation({
    mutationFn: buildingsApi.create,
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ['buildings'] }); message.success(t("Đã thêm dãy trọ.")); setOpen(false); form.resetFields(); },
    onError: (error: Error) => message.error(error.message),
  });
  const updateMutation = useMutation({
    mutationFn: ({ id, values }: { id: string; values: { name: string; address: string } }) => buildingsApi.update(id, values),
    onSuccess: async () => {
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['buildings'] }), queryClient.invalidateQueries({ queryKey: ['rooms'] }), queryClient.invalidateQueries({ queryKey: ['dashboard'] })]);
      message.success(t("Đã cập nhật dãy trọ."));
      setEditingBuilding(null);
    },
    onError: (error: Error) => message.error(error.message),
  });
  const openEditBuilding = (building: Building) => {
    setEditingBuilding(building);
    editForm.setFieldsValue({ name: building.name, address: building.address ?? '' });
  };
  const archiveMutation = useMutation({
    mutationFn: buildingsApi.archive,
    onSuccess: async () => { await Promise.all([queryClient.invalidateQueries({ queryKey: ['buildings'] }), queryClient.invalidateQueries({ queryKey: ['rooms'] }), queryClient.invalidateQueries({ queryKey: ['dashboard'] })]); message.success(t("Đã lưu trữ dãy trọ. Lịch sử và hóa đơn vẫn được giữ.")); },
    onError: (error: Error) => message.error(error.message),
  });
  const restoreMutation = useMutation({
    mutationFn: buildingsApi.restore,
    onSuccess: async (result) => { await Promise.all([queryClient.invalidateQueries({ queryKey: ['buildings'] }), queryClient.invalidateQueries({ queryKey: ['rooms'] }), queryClient.invalidateQueries({ queryKey: ['dashboard'] })]); message.success(t("Đã khôi phục dãy trọ và {0} phòng trong cùng lần lưu trữ.", [result.restoredRoomIds.length])); },
    onError: (error: Error) => message.error(error.message),
  });
  const allBuildings = buildingsQuery.data ?? [];
  const activeBuildings = allBuildings.filter((building) => !building.isArchived);
  const archivedBuildings = allBuildings.filter((building) => building.isArchived);
  const buildings = archiveFilter === 'Active' ? activeBuildings : archivedBuildings;
  const activeRoomCounts = useMemo(() => (roomsQuery.data ?? []).filter((room) => !room.isArchived).reduce<Record<string, number>>((acc, room) => { acc[room.buildingId] = (acc[room.buildingId] ?? 0) + 1; return acc; }, {}), [roomsQuery.data]);
  const archivedRoomCounts = useMemo(() => (roomsQuery.data ?? []).filter((room) => room.isArchived).reduce<Record<string, number>>((acc, room) => { acc[room.buildingId] = (acc[room.buildingId] ?? 0) + 1; return acc; }, {}), [roomsQuery.data]);
  useEffect(() => { if (params.get('create') === '1' && archiveFilter === 'Active') { setOpen(true); setParams({}, { replace: true }); } }, [params, setParams, archiveFilter]);
  const confirmArchive = (building: Building) => modal.confirm({
    title: t("Lưu trữ dãy trọ {0}?", [building.name]),
    content: t("Dãy trọ và các phòng đang hoạt động sẽ được ẩn khỏi danh sách hiện tại. Lịch sử thuê, hóa đơn và khoản thu vẫn được giữ; có thể khôi phục cùng nhóm sau này."),
    okText: t("Lưu trữ dãy trọ"), cancelText: t("Hủy"), okButtonProps: { danger: true },
    onOk: () => archiveMutation.mutateAsync(building.id),
  });

  return <>
    <PageTitle eyebrow={t("KHÔNG GIAN CỦA BẠN")} title={t("Dãy trọ")} subtitle={t("Sắp xếp các địa điểm cho thuê và thêm phòng vào từng dãy.")} extra={archiveFilter === 'Active' ? <Button type="primary" icon={<PlusOutlined />} onClick={() => setOpen(true)}>{t("Tạo dãy trọ")}</Button> : undefined} />
    <div className="archive-filter-toolbar"><label><span>{t("Trạng thái")}</span><Select aria-label={t("Lọc trạng thái lưu trữ")} value={archiveFilter} onChange={setArchiveFilter} options={[{ value: 'Active', label: t("Đang hoạt động ({0})", [activeBuildings.length]) }, { value: 'Archived', label: t("Đã lưu trữ ({0})", [archivedBuildings.length]) }]} /></label></div>
    {buildingsQuery.isError ? <LoadError error={buildingsQuery.error} onRetry={() => void buildingsQuery.refetch()} /> : buildingsQuery.isPending ? <div className="content-card"><Spin /></div> : buildings.length === 0 ? <SectionCard><EmptyPanel title={archiveFilter === 'Archived' ? t("Chưa có dãy trọ được lưu trữ") : t("Bạn chưa thêm dãy trọ nào")} description={archiveFilter === 'Archived' ? t("Khi bạn lưu trữ một dãy trọ, dãy trọ đó sẽ xuất hiện ở đây để khôi phục.") : t("Bắt đầu bằng cách tạo dãy trọ đầu tiên. Sau đó bạn có thể thêm phòng và người thuê.")} action={archiveFilter === 'Active' ? <Button type="primary" icon={<PlusOutlined />} onClick={() => setOpen(true)}>{t("Tạo dãy trọ đầu tiên")}</Button> : undefined} /></SectionCard> : <>
      {archiveFilter === 'Active' ? <div className="building-summary-strip"><div className="summary-icon"><BankOutlined /></div><div><strong>{activeBuildings.length} {t("dãy trọ đang hoạt động")}</strong><span>{roomsQuery.isError ? t("Không thể tải số phòng lúc này") : t("{0} phòng đang hoạt động", [(roomsQuery.data ?? []).filter((room) => !room.isArchived).length])}</span></div><Link to="/rooms">{t("Quản lý phòng")} <ArrowRightOutlined /></Link></div> : <div className="building-summary-strip archived-summary"><div className="summary-icon"><BankOutlined /></div><div><strong>{archivedBuildings.length} {t("dãy trọ đã lưu trữ")}</strong><span>{t("Lịch sử thuê và hóa đơn vẫn được giữ.")}</span></div></div>}
      {roomsQuery.isError && <div className="subtle-error"><LoadError error={roomsQuery.error} onRetry={() => void roomsQuery.refetch()} /></div>}
      <Row gutter={[18, 18]}>{buildings.map((building) => {
        const roomCount = archiveFilter === 'Active' ? activeRoomCounts[building.id] : archivedRoomCounts[building.id];
        return <Col key={building.id} xs={24} md={12} xl={8}><Card className={`building-card ${building.isArchived ? 'building-card-archived' : ''}`} bordered={false}>
          <div className="building-card-header"><div className="building-card-icon" aria-hidden="true"><BankOutlined /></div>{building.isArchived && <Tag className="building-archived-tag">{t("Đã lưu trữ")}</Tag>}<div className="building-room-count"><strong>{roomCount ?? building.roomCount ?? 0}</strong><small>{t("phòng")}</small></div></div>
          <div className="building-card-body"><div className="building-card-title"><div><Typography.Title level={4}>{building.name}</Typography.Title><span><EnvironmentOutlined /> {building.address || t("Chưa có địa chỉ")}</span></div></div>
            <div className="building-card-footer">{building.isArchived && <span>{t("Đã lưu trữ {0}", [building.archivedAt ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date(building.archivedAt)) : ''])}</span>}{building.isArchived ? <Button icon={<RollbackOutlined />} loading={restoreMutation.isPending && restoreMutation.variables === building.id} onClick={() => restoreMutation.mutate(building.id)}>{t("Khôi phục")}</Button> : <><Link className="building-view-rooms" to={`/rooms?building=${encodeURIComponent(building.id)}`}>{t("Xem phòng")} <ArrowRightOutlined /></Link><Space size={0} wrap><Button type="text" icon={<EditOutlined />} onClick={() => openEditBuilding(building)} aria-label={t("Sửa dãy trọ")}>{t("Sửa")}</Button><Button danger type="text" loading={archiveMutation.isPending && archiveMutation.variables === building.id} onClick={() => confirmArchive(building)}>{t("Lưu trữ")}</Button></Space></>}</div>
          </div>
        </Card></Col>;
      })}</Row>
      {archiveFilter === 'Active' && <SectionCard className="building-tip-card"><div className="building-tip"><div className="tip-illustration"><HomeOutlined /></div><div><strong>{t("Quản lý phòng trong từng dãy")}</strong><span>{t("Tạo phòng mới, cập nhật giá thuê và ghi nhận người chuyển vào trong mục Phòng & người thuê.")}</span></div><Button type="primary" ghost onClick={() => navigate('/rooms')}>{t("Đi đến phòng")}</Button></div></SectionCard>}
    </>}
    <Modal title={t("Thêm dãy trọ mới")} open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} okText={t("Tạo dãy trọ")} cancelText={t("Hủy")} confirmLoading={createMutation.isPending} destroyOnClose>
      <Typography.Paragraph className="modal-intro">{t("Đặt tên và nhập địa chỉ để dễ nhận biết dãy trọ của bạn.")}</Typography.Paragraph><Form form={form} layout="vertical" onFinish={(values) => createMutation.mutate(values)} requiredMark={false}><Form.Item label={t("Tên dãy trọ")} name="name" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên dãy trọ.") }]}><Input placeholder={t("Ví dụ: Dãy trọ đường Hoa Sứ")} maxLength={100} /></Form.Item><Form.Item label={t("Địa chỉ")} name="address" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập địa chỉ.") }]}><Input.TextArea placeholder={t("Số nhà, đường, phường/xã, quận/huyện")} autoSize={{ minRows: 2, maxRows: 4 }} maxLength={250} /></Form.Item></Form>
    </Modal>
    <Modal title={t("Sửa dãy trọ")} open={!!editingBuilding} onCancel={() => { if (!updateMutation.isPending) setEditingBuilding(null); }} onOk={() => editForm.submit()} okText={t("Lưu thay đổi")} cancelText={t("Hủy")} confirmLoading={updateMutation.isPending} destroyOnClose>
      <Typography.Paragraph className="modal-intro">{t("Đặt tên và nhập địa chỉ để dễ nhận biết dãy trọ của bạn.")}</Typography.Paragraph><Form form={editForm} layout="vertical" onFinish={(values) => { if (editingBuilding && !updateMutation.isPending) updateMutation.mutate({ id: editingBuilding.id, values }); }} requiredMark={false}><Form.Item label={t("Tên dãy trọ")} name="name" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên dãy trọ.") }]}><Input placeholder={t("Ví dụ: Dãy trọ đường Hoa Sứ")} maxLength={100} /></Form.Item><Form.Item label={t("Địa chỉ")} name="address" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập địa chỉ.") }]}><Input.TextArea placeholder={t("Số nhà, đường, phường/xã, quận/huyện")} autoSize={{ minRows: 2, maxRows: 4 }} maxLength={250} /></Form.Item></Form>
    </Modal>
  </>;
}
