import { CopyOutlined, EditOutlined, EllipsisOutlined, FileAddOutlined, FileTextOutlined, InboxOutlined, LinkOutlined, RollbackOutlined, UserAddOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { MenuProps, TableProps } from 'antd';
import { App, Button, Dropdown, Form, Tag } from 'antd';
import dayjs from 'dayjs';
import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { formatVnd } from '../../shared/format';
import { getLanguage, t, type Language } from '../../shared/i18n/runtime';
import { buildingsApi } from '../buildings/index';
import { invoicesApi } from '../invoices/index';
import { subscriptionApi } from '../subscription/index';
import { roomsApi, type Room } from './index';


type RoomMode = 'create' | 'duplicate' | 'settings' | 'occupancy' | 'edit-occupancy' | null;
type ArchiveFilter = 'Active' | 'Archived';


export function useRoomsPage() {
  const numberRule = [{ required: true, message: t("Vui lòng nhập giá trị.") }, { type: 'number' as const, min: 0, message: t("Giá trị không được nhỏ hơn 0.") }];
  const { message, modal } = App.useApp();
  const client = useQueryClient();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [moveoutRoom, setMoveoutRoom] = useState<Room | null>(null);
  const [documentRoom, setDocumentRoom] = useState<Room | null>(null);
  const [mode, setMode] = useState<RoomMode>(null);
  const [selectedRoom, setSelectedRoom] = useState<Room | null>(null);
  const [linkInfo, setLinkInfo] = useState<{ token: string; url: string; room?: Room; newlyIssued?: boolean } | null>(null);
  const [linkLanguage, setLinkLanguage] = useState<Language>(getLanguage);
  const [archiveFilter, setArchiveFilter] = useState<ArchiveFilter>('Active');
  const tenantLinkUrl = () => {
    if (!linkInfo) return '';
    const url = new URL(linkInfo.url, window.location.origin);
    url.searchParams.set('lang', linkLanguage);
    return url.toString();
  };
  const [supplemental, setSupplemental] = useState<{ room: Room; requestId: string } | null>(null);
  const [supplementalForm] = Form.useForm();
  const [roomForm] = Form.useForm();
  const [occupancyForm] = Form.useForm();
  const [editOccupancyForm] = Form.useForm();
  const buildingFilter = searchParams.get('building') || undefined;
  const [statusFilter, setStatusFilter] = useState<'all' | 'occupied' | 'vacant'>('all');
  const buildingsQuery = useQuery({ queryKey: ['buildings', 'room-options'], queryFn: () => buildingsApi.list(true) });
  const roomsQuery = useQuery({
    queryKey: ['rooms', 'archive-view'],
    queryFn: () => roomsApi.list(undefined, true),
    refetchOnWindowFocus: true,
    refetchInterval: 5000,
    refetchIntervalInBackground: false,
  });
  const subQuery = useQuery({ queryKey: ['subscription'], queryFn: subscriptionApi.getStatus });

  const refreshData = async () => { await Promise.all([client.invalidateQueries({ queryKey: ['rooms'] }), client.invalidateQueries({ queryKey: ['dashboard'] }), client.invalidateQueries({ queryKey: ['buildings'] }), client.invalidateQueries({ queryKey: ['invoices'] }), client.invalidateQueries({ queryKey: ['subscription'] }), client.invalidateQueries({ queryKey: ['readings'] })]); };
  const createMutation = useMutation({ mutationFn: roomsApi.create, onSuccess: async () => { await refreshData(); message.success(t("Đã thêm phòng.")); setMode(null); roomForm.resetFields(); }, onError: (e: Error) => message.error(e.message) });
  const supplementalMutation = useMutation({ mutationFn: ({ roomId, body }: { roomId: string; body: Parameters<typeof invoicesApi.createSupplemental>[1] }) => invoicesApi.createSupplemental(roomId, body), onSuccess: async () => { await refreshData(); message.success(t("Đã tạo hóa đơn bổ sung trên link khách.")); setSupplemental(null); supplementalForm.resetFields(); }, onError: (e: Error) => message.error(e.message) });
  const updateMutation = useMutation({ mutationFn: ({ id, values }: { id: string; values: Parameters<typeof roomsApi.update>[1] }) => roomsApi.update(id, values), onSuccess: async () => { await refreshData(); message.success(t("Đã cập nhật thông tin phòng.")); setMode(null); }, onError: (e: Error) => message.error(e.message) });
  const occupancyMutation = useMutation({ mutationFn: ({ id, values }: { id: string; values: Parameters<typeof roomsApi.startOccupancy>[1] }) => roomsApi.startOccupancy(id, values), onSuccess: async () => { await refreshData(); message.success(t("Đã ghi nhận người thuê và khoản cọc nếu có.")); setMode(null); }, onError: (e: Error) => message.error(e.message) });
  const editOccupancyMutation = useMutation({ mutationFn: ({ id, values }: { id: string; values: Parameters<typeof roomsApi.editOccupancy>[1] }) => roomsApi.editOccupancy(id, values), onSuccess: async () => { await refreshData(); message.success(t("Đã cập nhật người thuê.")); setMode(null); }, onError: (e: Error) => message.error(e.message) });
  const archiveMutation = useMutation({ mutationFn: roomsApi.archive, onSuccess: async () => { await refreshData(); message.success(t("Đã lưu trữ phòng. Lịch sử và hóa đơn vẫn được giữ.")); }, onError: (error: Error) => message.error(error.message) });
  const restoreMutation = useMutation({ mutationFn: roomsApi.restore, onSuccess: async () => { await refreshData(); message.success(t("Đã khôi phục phòng.")); }, onError: (error: Error) => message.error(error.message) });

  const rotateMutation = useMutation({
    mutationFn: roomsApi.rotateLink,
    onSuccess: (result, id) => {
      setLinkInfo((prev) => ({ ...result, room: roomsQuery.data?.find((room) => room.id === id) ?? prev?.room, newlyIssued: true }));
      void refreshData();
    },
    onError: (e: Error) => {
      const msg = e.message || '';
      if (/thiết lập thanh toán|payos/i.test(msg)) {
        message.error(t("Vui lòng thiết lập tài khoản nhận tiền (VietQR hoặc payOS) tại mục Cài đặt thanh toán trước khi cấp link cho khách."));
        modal.confirm({
          title: t("Cần thiết lập tài khoản nhận tiền"),
          content: t("Bạn chỉ cần chọn ngân hàng và nhập số tài khoản VietQR (hoặc kết nối payOS) để bắt đầu cấp link hóa đơn cho khách thuê."),
          okText: t("Mở Cài đặt thanh toán"),
          cancelText: t("Để sau"),
          centered: true,
          onOk: () => navigate('/settings/payment'),
        });
      } else {
        message.error(msg);
      }
    },
  });

  const isPro = subQuery.data?.isPro ?? false;
  const maxRooms = subQuery.data?.maxRooms ?? 5;
  const activeRoomCount = (roomsQuery.data ?? []).filter((room) => !room.isArchived).length;
  const isQuotaReached = !isPro && activeRoomCount >= maxRooms;

  const visibleRooms = useMemo(() => (roomsQuery.data ?? []).filter((room) => archiveFilter === 'Archived' ? !!room.isArchived : !room.isArchived), [roomsQuery.data, archiveFilter]);
  const buildingRooms = useMemo(() => visibleRooms.filter((room) => !buildingFilter || room.buildingId === buildingFilter), [visibleRooms, buildingFilter]);
  const rooms = useMemo(() => buildingRooms.filter((room) => statusFilter === 'all' || (statusFilter === 'occupied' ? !!room.occupancy : !room.occupancy)), [buildingRooms, statusFilter]);
  const occupiedCount = buildingRooms.filter((room) => room.occupancy).length;
  const vacancyCount = buildingRooms.length - occupiedCount;
  const activeBuildings = (buildingsQuery.data ?? []).filter((building) => !building.isArchived);
  const openCreate = () => {
    if (isQuotaReached) {
      modal.confirm({
        title: t("Đã đạt giới hạn phòng của gói Miễn phí"),
        content: t("Gói Miễn phí chỉ cho phép quản lý tối đa {0} phòng. Vui lòng nâng cấp lên gói Pro để tạo thêm phòng không giới hạn.", [maxRooms]),
        okText: t("Nâng cấp ngay"),
        cancelText: t("Để sau"),
        onOk: () => navigate('/account-status'),
      });
      return;
    }
    setSelectedRoom(null); roomForm.resetFields(); roomForm.setFieldsValue({ rent: 0, electricityRate: 3500, waterFee: 0, waterPerPerson: false, serviceFee: 0, buildingId: activeBuildings.some((building) => building.id === buildingFilter) ? buildingFilter : (activeBuildings.length === 1 ? activeBuildings[0].id : undefined) }); setMode('create');
  };
  const openDuplicate = (room: Room) => {
    if (isQuotaReached) {
      modal.confirm({
        title: t("Đã đạt giới hạn phòng của gói Miễn phí"),
        content: t("Gói Miễn phí chỉ cho phép quản lý tối đa {0} phòng. Vui lòng nâng cấp lên gói Pro để tạo thêm phòng không giới hạn.", [maxRooms]),
        okText: t("Nâng cấp ngay"),
        cancelText: t("Để sau"),
        onOk: () => navigate('/account-status'),
      });
      return;
    }
    const baseName = room.name.slice(0, 60);
    const existingNames = new Set((roomsQuery.data ?? []).filter((candidate) => candidate.buildingId === room.buildingId).map((candidate) => candidate.name.toLocaleLowerCase()));
    let name = t("{0} (bản sao)", [baseName]);
    let suffix = 2;
    while (existingNames.has(name.toLocaleLowerCase())) name = t("{0} (bản sao {1})", [baseName, suffix++]);
    setSelectedRoom(room);
    roomForm.resetFields();
    roomForm.setFieldsValue({ buildingId: room.buildingId, name, rent: room.rent, electricityRate: room.electricityRate, waterFee: room.waterFee, waterPerPerson: room.waterPerPerson ?? false, serviceFee: room.serviceFee });
    setMode('duplicate');
  };
  const openSettings = (room: Room) => { setSelectedRoom(room); roomForm.resetFields(); roomForm.setFieldsValue({ name: room.name, rent: room.rent, electricityRate: room.electricityRate, waterFee: room.waterFee, waterPerPerson: room.waterPerPerson ?? false, serviceFee: room.serviceFee }); setMode('settings'); };
  const openOccupancy = (room: Room) => { navigate(`/rooms/${encodeURIComponent(room.id)}/tenant`); };

  const getActions = (room: Room): MenuProps['items'] => [
    ...(room.occupancy ? [{ key: 'documents', icon: <FileTextOutlined />, label: t("Xuất hợp đồng & CT01") }] : []),
    { key: 'edit', icon: <EditOutlined />, label: t("Sửa thông tin phòng") },
    ...(room.occupancy ? [{ key: 'edit-occupancy', icon: <EditOutlined />, label: t("Sửa thông tin người thuê") }] : []),
    { key: 'duplicate', icon: <CopyOutlined />, label: t("Nhân bản phòng") },
    { key: 'supplemental', icon: <FileAddOutlined />, label: t("Tạo hóa đơn bổ sung"), disabled: !room.occupancy },
    ...(room.occupancy?.depositInvoiceId ? [{ key: 'deposit', label: <Link to={`/invoices?invoice=${room.occupancy.depositInvoiceId}`}>{t("Thu / quyết toán cọc")}</Link> }] : []),
    room.occupancy ? { key: 'moveout', danger: true, label: t("Trả phòng & quyết toán") } : { key: 'occupancy', icon: <UserAddOutlined />, label: t("Thêm người thuê") },
    { type: 'divider' },
    { key: 'archive', icon: <InboxOutlined />, label: t("Lưu trữ phòng") },

  ];
  const onAction = (key: string, room: Room) => {
    if (key === 'documents' && room.occupancy) navigate(`/rooms/${encodeURIComponent(room.id)}/documents`);
    if (key === 'edit') openSettings(room);
    if (key === 'edit-occupancy' && room.occupancy) navigate(`/rooms/${encodeURIComponent(room.id)}/tenant`);
    if (key === 'duplicate') openDuplicate(room);
    if (key === 'supplemental' && room.occupancy) { supplementalForm.resetFields(); supplementalForm.setFieldsValue({ dueDate: dayjs() }); setSupplemental({ room, requestId: crypto.randomUUID() }); }
    if (key === 'occupancy') openOccupancy(room);
    if (key === 'moveout') setMoveoutRoom(room);
    if (key === 'archive') modal.confirm({ title: t("Lưu trữ phòng {0}?", [room.name]), content: t("Phòng sẽ được lưu trữ khỏi danh sách đang hoạt động. Lịch sử thuê, hóa đơn và khoản thu vẫn được giữ."), okText: t("Lưu trữ phòng"), cancelText: t("Hủy"), onOk: () => archiveMutation.mutateAsync(room.id) });

  };

  const renewLink = (room: Room) => {
    modal.confirm({ title: t("Thu hồi link của {0}?", [room.name]), content: t("Tạo liên kết riêng để người thuê xem hóa đơn và thanh toán. Nếu đã cấp link trước đó, link cũ sẽ hết hiệu lực."), className: 'room-editor', centered: true, autoFocusButton: 'cancel', okText: t("Thu hồi & tạo link mới"), cancelText: t("Hủy"), okButtonProps: { danger: true }, onOk: () => rotateMutation.mutateAsync(room.id) });
  };
  const issueLink = (room: Room) => {
    setLinkLanguage(getLanguage());
    if (room.occupancy?.token && room.occupancy.url) {
      setLinkInfo({ token: room.occupancy.token, url: room.occupancy.url, room, newlyIssued: false });
    } else {
      rotateMutation.mutate(room.id);
    }
  };
  useEffect(() => {
    if (searchParams.get('create') === '1' && buildingsQuery.isSuccess) {
      if (buildingsQuery.data.length > 0) {
        openCreate();
        const next = new URLSearchParams(searchParams);
        next.delete('create');
        setSearchParams(next, { replace: true });
      } else {
        message.info(t("Vui lòng tạo dãy trọ trước khi thêm phòng."));
        const next = new URLSearchParams(searchParams);
        next.delete('create');
        setSearchParams(next, { replace: true });
      }
    } else if (searchParams.get('links') === '1') {
      setStatusFilter('occupied');
      const next = new URLSearchParams(searchParams);
      next.delete('links');
      setSearchParams(next, { replace: true });
    } else if (searchParams.get('documents') && roomsQuery.isSuccess) {
      const targetId = searchParams.get('documents');
      const targetRoom = (roomsQuery.data ?? []).find((r) => r.id === targetId && r.occupancy);
      if (targetRoom) setDocumentRoom(targetRoom);
      const next = new URLSearchParams(searchParams);
      next.delete('documents');
      setSearchParams(next, { replace: true });
    }
  }, [searchParams, buildingsQuery.isSuccess, buildingsQuery.data, roomsQuery.isSuccess, roomsQuery.data]);

  const roomStatus = (room: Room) => room.isArchived ? <Tag className="archived-state-tag">{t("Đã lưu trữ")}</Tag> : <Tag className={`room-state ${room.occupancy ? 'room-state-occupied' : 'room-state-vacant'}`}>{room.occupancy ? t("Đang thuê") : t("Còn trống")}</Tag>;
  const roomActions = (room: Room) => <div className="room-actions">
    {room.isArchived ? <Button icon={<RollbackOutlined />} loading={restoreMutation.isPending && restoreMutation.variables === room.id} onClick={() => restoreMutation.mutate(room.id)}>{t("Khôi phục")}</Button> : <>
    {room.occupancy ? <Button icon={<LinkOutlined />} loading={rotateMutation.isPending && rotateMutation.variables === room.id} onClick={() => issueLink(room)}>{t("Cấp link cho khách")}</Button> : <Button icon={<UserAddOutlined />} onClick={() => openOccupancy(room)}>{t("Thêm người thuê")}</Button>}
    <Dropdown menu={{ items: getActions(room), onClick: ({ key }) => onAction(key, room) }} trigger={['click']}><Button type="text" className="room-more" icon={<EllipsisOutlined />} aria-label={t("Tác vụ {0}", [room.name])} /></Dropdown>
    </>}
  </div>;
  const compactRoom = (room: Room) => <>
    <div className="room-card-head"><div><strong>{room.name}</strong><span>{room.buildingName || buildingsQuery.data?.find((building) => building.id === room.buildingId)?.name || t("Dãy trọ")}</span></div>{roomStatus(room)}</div>
    <div className="room-card-tenant"><span className="room-field-label">{t("Người thuê")}</span>{room.occupancy ? <div><strong>{room.occupancy.tenantName}</strong>{room.occupancy.coResidents && room.occupancy.coResidents.length > 0 && <span>+ {room.occupancy.coResidents.map((m) => m.fullName).join(', ')}</span>}<span>{room.occupancy.people}  {t("người")}{room.occupancy.phone ? ` · ${room.occupancy.phone}` : ''}</span><span>{t("Cọc yêu cầu")} {formatVnd(room.occupancy.depositRequired)}  {t("· Đang giữ")} {formatVnd(room.occupancy.depositHeld)}</span></div> : <span>{t("Chưa có người thuê")}</span>}</div>
    <div className="room-card-rent"><span className="room-field-label">{t("Giá thuê / tháng")}</span><strong>{formatVnd(room.rent)}</strong></div>
    {roomActions(room)}
  </>;
  const columns: TableProps<Room>['columns'] = [
    { title: t("Phòng"), key: 'room', render: (_, room) => <div className="room-list-name"><strong>{room.name}</strong><span>{room.buildingName || buildingsQuery.data?.find((building) => building.id === room.buildingId)?.name || t("Dãy trọ")}</span></div> },
    { title: t("Người thuê"), key: 'tenant', render: (_, room) => room.occupancy ? <div className="room-list-tenant"><strong>{room.occupancy.tenantName}</strong>{room.occupancy.coResidents && room.occupancy.coResidents.length > 0 && <span>+ {room.occupancy.coResidents.map((m) => m.fullName).join(', ')}</span>}<span>{room.occupancy.people}  {t("người")}{room.occupancy.phone ? ` · ${room.occupancy.phone}` : ''}</span><span>{t("Cọc yêu cầu")} {formatVnd(room.occupancy.depositRequired)}  {t("· Đang giữ")} {formatVnd(room.occupancy.depositHeld)}</span></div> : <span className="room-field-label">{t("Chưa có người thuê")}</span> },
    { title: t("Giá thuê / tháng"), dataIndex: 'rent', width: 160, align: 'right', render: (value: number) => <strong className="room-price">{formatVnd(value)}</strong> },
    { title: t("Trạng thái"), key: 'status', width: 118, render: (_, room) => roomStatus(room) },
    { title: '', key: 'actions', width: 240, align: 'right', render: (_, room) => roomActions(room) },
  ];
  const isCreating = mode === 'create' || mode === 'duplicate';
  const isBusy = createMutation.isPending || updateMutation.isPending || occupancyMutation.isPending;

  const clearFilters = () => { setStatusFilter('all'); setArchiveFilter('Active'); setSearchParams({}); };

  return { numberRule, message, modal, navigate, setSearchParams, moveoutRoom, setMoveoutRoom, documentRoom, setDocumentRoom, mode, setMode, selectedRoom, linkInfo, setLinkInfo, linkLanguage, setLinkLanguage, archiveFilter, setArchiveFilter, tenantLinkUrl, supplemental, setSupplemental, supplementalForm, roomForm, occupancyForm, editOccupancyForm, buildingFilter, statusFilter, setStatusFilter, buildingsQuery, roomsQuery, createMutation, supplementalMutation, updateMutation, occupancyMutation, editOccupancyMutation, isPro, maxRooms, activeRoomCount, isQuotaReached, buildingRooms, rooms, occupiedCount, vacancyCount, activeBuildings, openCreate, renewLink, compactRoom, columns, isCreating, isBusy, clearFilters };
}
