import { LinkOutlined } from '@ant-design/icons';
import { Button, Col, DatePicker, Form, Input, InputNumber, Modal, Row, Select, Typography } from 'antd';
import dayjs from 'dayjs';
import { dateFormat, languages, t } from '../../shared/i18n/runtime';
import MoneyInput from '../../shared/ui/MoneyInput';
import PhoneInput from '../../shared/ui/PhoneInput';
import ContractDocumentsDrawer from './ContractDocumentsDrawer';
import MoveoutDrawer from './MoveoutDrawer';
import type { useRoomsPage } from './useRoomsPage';

export default function RoomDialogs({ state }: { state: ReturnType<typeof useRoomsPage> }) {
  const { numberRule, message, moveoutRoom, setMoveoutRoom, documentRoom, setDocumentRoom, mode, setMode, buildingModalMode, setBuildingModalMode, editingBuilding, buildingForm, createBuildingMutation, updateBuildingMutation, selectedRoom, linkInfo, setLinkInfo, linkLanguage, setLinkLanguage, tenantLinkUrl, supplemental, setSupplemental, supplementalForm, roomForm, occupancyForm, editOccupancyForm, createMutation, supplementalMutation, updateMutation, occupancyMutation, editOccupancyMutation, activeBuildings, renewLink, isCreating, isBusy } = state;
  const minReading = selectedRoom?.previousReading ?? selectedRoom?.initialReading ?? 0;
  const minWaterReading = selectedRoom?.previousWaterReading ?? selectedRoom?.initialWaterReading ?? 0;
  const waterSelection = Form.useWatch('waterPerPerson', roomForm);
  const isWaterByMeter = waterSelection === 'meter';
  const isBuildingBusy = createBuildingMutation.isPending || updateBuildingMutation.isPending;

  return <>
    {moveoutRoom && <MoveoutDrawer room={moveoutRoom} onClose={() => setMoveoutRoom(null)} />}
    {documentRoom && <ContractDocumentsDrawer room={documentRoom} onClose={() => setDocumentRoom(null)} />}
    <Modal
      className="room-editor"
      title={buildingModalMode === 'edit' ? t("Sửa dãy trọ") : t("Thêm dãy trọ mới")}
      open={buildingModalMode !== null}
      onCancel={() => { if (!isBuildingBusy) setBuildingModalMode(null); }}
      onOk={() => buildingForm.submit()}
      okText={buildingModalMode === 'edit' ? t("Lưu thay đổi") : t("Tạo dãy trọ")}
      cancelText={t("Hủy")}
      confirmLoading={isBuildingBusy}
      destroyOnClose
    >
      <Typography.Paragraph className="modal-intro">{t("Đặt tên và nhập địa chỉ để dễ nhận biết dãy trọ của bạn.")}</Typography.Paragraph>
      <Form
        form={buildingForm}
        layout="vertical"
        requiredMark={false}
        onFinish={(values) => {
          if (buildingModalMode === 'edit' && editingBuilding) {
            updateBuildingMutation.mutate({ id: editingBuilding.id, values });
          } else {
            createBuildingMutation.mutate(values);
          }
        }}
      >
        <Form.Item label={t("Tên dãy trọ")} name="name" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên dãy trọ.") }]}>
          <Input placeholder={t("Ví dụ: Dãy trọ đường Hoa Sứ")} maxLength={100} />
        </Form.Item>
        <Form.Item label={t("Địa chỉ")} name="address" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập địa chỉ.") }]}>
          <Input.TextArea placeholder={t("Số nhà, đường, phường/xã, quận/huyện")} autoSize={{ minRows: 2, maxRows: 4 }} maxLength={250} />
        </Form.Item>
      </Form>
    </Modal>
    <Modal className="room-editor" maskClosable={false} title={isCreating ? (mode === 'duplicate' ? t("Nhân bản {0}", [selectedRoom?.name ?? t("phòng")]) : t("Thêm phòng mới")) : mode === 'settings' ? t("Thông tin {0}", [selectedRoom?.name ?? t("phòng")]) : t("Thêm người thuê · {0}", [selectedRoom?.name ?? ''])} open={mode !== null && mode !== 'edit-occupancy'} onCancel={() => setMode(null)} onOk={() => (mode === 'occupancy' ? occupancyForm.submit() : roomForm.submit())} okText={isCreating ? t("Tạo phòng") : mode === 'settings' ? t("Lưu thay đổi") : t("Lưu người thuê")} cancelText={t("Hủy")} confirmLoading={isBusy} width={660} destroyOnClose>
      {mode === 'occupancy' ? <><Typography.Paragraph className="modal-intro">{t("Nhập tiền cọc thỏa thuận riêng với khách. Khoản cọc được tạo riêng và thanh toán qua link khách sau khi lưu.")}</Typography.Paragraph><Form form={occupancyForm} layout="vertical" requiredMark={false} onFinish={(values) => { if (!selectedRoom) return; occupancyMutation.mutate({ id: selectedRoom.id, values: { ...values, people: Number(values.people || 1), startDate: values.startDate.format('YYYY-MM-DD') } }); }}><Form.Item label={t("Tên người thuê")} name="tenantName" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên người thuê.") }]}><Input placeholder={t("Họ và tên")} /></Form.Item><Row gutter={14}><Col xs={24} sm={15}><Form.Item label={t("Số điện thoại")} name="phone"><PhoneInput maxLength={60} /></Form.Item></Col><Col xs={24} sm={9}><Form.Item label={t("Số người ở")} name="people" rules={[{ required: true, type: 'number', min: 1, max: 30, message: t("Nhập từ 1 đến 30 người.") }]}><InputNumber min={1} max={30} precision={0} style={{ width: '100%' }} /></Form.Item></Col></Row><Row gutter={14}><Col xs={24} sm={12}><Form.Item label={t("Chỉ số điện ban đầu")} name="initialReading" rules={[{ required: true, message: t("Vui lòng nhập giá trị.") }, { type: 'number', min: minReading, max: 1000000000, message: t("Nhập chỉ số từ {0} trở lên.", [minReading]) }]}><InputNumber min={minReading} max={1000000000} step={1} precision={0} addonAfter="kWh" style={{ width: '100%' }} /></Form.Item></Col><Col xs={24} sm={12}><Form.Item label={t("Ngày chuyển vào")} name="startDate" rules={[{ required: true, message: t("Vui lòng chọn ngày.") }]}><DatePicker format={dateFormat()} disabledDate={(date) => date.isAfter(dayjs(), 'day')} style={{ width: '100%' }} /></Form.Item></Col></Row><Form.Item label={t("Tiền cọc yêu cầu")} name="depositAmount" extra={t("Nhập 0 nếu không thu cọc. Đây là khoản phải thu, chưa ghi nhận khách đã trả.")} rules={[{ required: true, type: 'number', min: 0, max: 2147483647, message: t("Nhập số tiền cọc hợp lệ.") }]}><MoneyInput min={0} max={2147483647} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Form></> : <>
        {isCreating && <Typography.Paragraph className="modal-intro">{mode === 'duplicate' ? t("Đã sao chép dãy trọ và các mức phí. Chỉnh tên phòng trước khi tạo. Phòng mới chưa có người thuê, tiền cọc hoặc hóa đơn.") : t("Nhập giá thuê và các khoản phí để dùng khi lập hóa đơn hàng tháng.")}</Typography.Paragraph>}
        <Form form={roomForm} layout="vertical" requiredMark={false} onFinish={(values) => { const waterByMeter = values.waterPerPerson === 'meter'; const payload = { ...values, waterPerPerson: values.waterPerPerson === true, waterByMeter }; return isCreating ? createMutation.mutate(payload) : selectedRoom && updateMutation.mutate({ id: selectedRoom.id, values: payload }); }}>
          {isCreating && <Form.Item label={t("Dãy trọ")} name="buildingId" rules={[{ required: true, message: t("Vui lòng chọn dãy trọ.") }]}><Select placeholder={t("Chọn dãy trọ")} options={activeBuildings.map((b) => ({ value: b.id, label: b.name }))} /></Form.Item>}
          <Form.Item label={t("Tên phòng")} name="name" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên phòng.") }]}><Input placeholder={t("Ví dụ: Phòng 01")} maxLength={80} /></Form.Item>
          <Row gutter={14}><Col xs={24} sm={12}><Form.Item label={t("Giá thuê / tháng")} name="rent" rules={numberRule}><MoneyInput min={0} step={100000} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col><Col xs={24} sm={12}><Form.Item label={t("Giá điện / kWh")} name="electricityRate" rules={numberRule}><MoneyInput min={0} step={100} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col></Row>
          <Row gutter={14}><Col xs={24} sm={12}><Form.Item label={isWaterByMeter ? t("Giá nước / m³") : t("Tiền nước")} name="waterFee" rules={numberRule}><MoneyInput min={0} step={isWaterByMeter ? 1000 : 10000} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col><Col xs={24} sm={12}><Form.Item label={t("Cách tính tiền nước")} name="waterPerPerson" rules={[{ required: true }]}><Select options={[{ value: false, label: t("Theo phòng / tháng") }, { value: true, label: t("Theo người / tháng") }, { value: 'meter', label: t("Theo chỉ số đồng hồ (m³)") }]} /></Form.Item></Col></Row>
          {isWaterByMeter && <Form.Item label={t("Chỉ số nước ban đầu")} name="initialWaterReading" rules={numberRule}><InputNumber min={isCreating ? 0 : minWaterReading} max={1000000000} step={1} precision={0} addonAfter="m³" style={{ width: '100%' }} /></Form.Item>}
          <Form.Item label={t("Phí dịch vụ / tháng")} name="serviceFee" rules={numberRule}><MoneyInput min={0} step={10000} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
        </Form>
      </>}
    </Modal>
    <Modal className="room-editor" title={t("Sửa thông tin người thuê")} open={mode === 'edit-occupancy'} maskClosable={false} onCancel={() => { if (!editOccupancyMutation.isPending) setMode(null); }} onOk={() => editOccupancyForm.submit()} okText={t("Lưu thay đổi")} cancelText={t("Hủy")} confirmLoading={editOccupancyMutation.isPending} cancelButtonProps={{ disabled: editOccupancyMutation.isPending }} closable={!editOccupancyMutation.isPending} width={610} destroyOnClose>
      <Typography.Paragraph>{t("Số người mới áp dụng cho hóa đơn nháp chưa thu tiền và kỳ chưa lập hóa đơn. Hóa đơn đã phát hành giữ nguyên.")}</Typography.Paragraph>
      <Form form={editOccupancyForm} layout="vertical" onFinish={(values) => { if (selectedRoom?.occupancy && !editOccupancyMutation.isPending) editOccupancyMutation.mutate({ id: selectedRoom.id, values: { ...values, people: Number(values.people || 1), occupancyId: selectedRoom.occupancy.id } }); }}>
        <Form.Item name="tenantName" label={t("Tên người thuê")} rules={[{ required: true, whitespace: true, max: 160, message: t("Vui lòng nhập tên người thuê.") }]}><Input maxLength={160} /></Form.Item>
        <Row gutter={14}>
          <Col xs={24} sm={12}>
            <Form.Item name="phone" label={t("Số điện thoại")}><PhoneInput maxLength={60} /></Form.Item>
          </Col>
          <Col xs={24} sm={12}>
            <Form.Item name="people" label={t("Số người ở")} rules={[{ required: true, type: 'integer', min: 1, max: 30, message: t("Nhập từ 1 đến 30 người.") }]}><InputNumber min={1} max={30} precision={0} style={{ width: '100%' }} /></Form.Item>
          </Col>
        </Row>
      </Form>
    </Modal>
    <Modal className="room-editor" title={t("Tạo hóa đơn bổ sung · {0}", [supplemental?.room.name ?? ''])} open={!!supplemental} maskClosable={false} onCancel={() => { if (!supplementalMutation.isPending) setSupplemental(null); }} onOk={() => supplementalForm.submit()} okText={t("Tạo hóa đơn")} cancelText={t("Hủy")} confirmLoading={supplementalMutation.isPending} cancelButtonProps={{ disabled: supplementalMutation.isPending }} closable={!supplementalMutation.isPending} width={610} destroyOnClose>
      <Typography.Paragraph className="modal-intro">{t("Khoản thu riêng cho")} {supplemental?.room.occupancy?.tenantName}{t(". Sau khi tạo, khách xem và thanh toán qua link đang dùng.")}</Typography.Paragraph>
      <Form form={supplementalForm} layout="vertical" requiredMark={false} onFinish={(values) => { if (supplemental?.room.occupancy && !supplementalMutation.isPending) supplementalMutation.mutate({ roomId: supplemental.room.id, body: { requestId: supplemental.requestId, occupancyId: supplemental.room.occupancy.id, description: values.description, amount: values.amount, dueDate: values.dueDate.format('YYYY-MM-DD') } }); }}>
        <Form.Item label={t("Nội dung khoản thu")} name="description" rules={[{ required: true, whitespace: true, message: t("Nhập nội dung khoản thu.") }]}><Input.TextArea maxLength={500} rows={3} placeholder={t("Ví dụ: Phí sửa khóa, tiền thuê bổ sung…")} /></Form.Item>
        <Form.Item label={t("Số tiền")} name="amount" rules={[{ required: true, type: 'number', min: 1, max: 2147483647, message: t("Nhập số tiền từ 1 đến 2.147.483.647 đồng.") }]}><MoneyInput min={1} max={2147483647} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
        <Form.Item label={t("Hạn thanh toán")} name="dueDate" rules={[{ required: true, message: t("Chọn hạn thanh toán.") }]}><DatePicker format={dateFormat()} style={{ width: '100%' }} /></Form.Item>
      </Form>
    </Modal>
    <Modal className="room-editor room-link-modal" title={t("Link hóa đơn · {0}", [linkInfo?.room?.name ?? t("Người thuê")])} open={!!linkInfo} onCancel={() => setLinkInfo(null)} footer={<div className="room-link-footer">{linkInfo?.room && <Button danger type="text" onClick={() => renewLink(linkInfo.room!)}>{t("Thu hồi & tạo link mới")}</Button>}<Button onClick={() => setLinkInfo(null)}>{t("Đóng")}</Button></div>}>
      <div className="link-info">
        <div className="link-info-icon"><LinkOutlined /></div>
        <Typography.Paragraph>{linkInfo?.newlyIssued ? t("Đã tạo link mới và thu hồi link cũ. ") : ''}{t("Gửi link này cho")} {linkInfo?.room?.occupancy?.tenantName || t("người thuê")}  {t("để xem hóa đơn và thanh toán.")}</Typography.Paragraph>
        <div className="tenant-link-language">
          <label>{t("Ngôn ngữ cho khách")}</label>
          <Select aria-label={t("Ngôn ngữ cho khách")} value={linkLanguage} onChange={setLinkLanguage} options={[...languages]} />
          <span>{t("Chọn ngôn ngữ của link trước khi gửi cho khách.")}</span>
        </div>
        <div className="room-link-copy-row">
          <Input readOnly aria-label={t("Link hóa đơn của người thuê")} value={linkInfo ? tenantLinkUrl() : ''} onFocus={(e) => e.currentTarget.select()} />
          <Button type="primary" onClick={async () => { if (!linkInfo) return; const url = tenantLinkUrl(); try { await navigator.clipboard.writeText(url); message.success(t("Đã sao chép liên kết.")); } catch { message.info(t("Hãy chọn và sao chép liên kết ở trên.")); } }}>{t("Sao chép")}</Button>
        </div>
        {linkInfo && (
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12, marginTop: 12 }}>
            <Button href={tenantLinkUrl()} target="_blank" rel="noopener noreferrer" icon={<LinkOutlined />}>{t("Mở trang khách")}</Button>
          </div>
        )}
      </div>
    </Modal>
  </>;
}
