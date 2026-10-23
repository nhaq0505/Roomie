import {
  BookOutlined,
  DeleteOutlined,
  EditOutlined,
  EyeOutlined,
  NotificationOutlined,
  PlusOutlined,
} from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  App,
  Button,
  Col,
  DatePicker,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Row,
  Select,
  Space,
  Switch,
  Table,
  Tabs,
  Tag,
} from 'antd';
import dayjs from 'dayjs';
import { useState } from 'react';
import { dateFormat, t } from '../../shared/i18n/runtime';
import { LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { marketerApi } from './api';
import type {
  MarketerArticleItem,
  MarketerBannerItem,
  UpsertMarketerArticleRequest,
  UpsertMarketerBannerRequest,
} from './types';

export default function MarketerContentPage() {
  const { message } = App.useApp();
  const client = useQueryClient();

  const [activeTab, setActiveTab] = useState<string>('articles');

  // Articles state
  const [articleModalOpen, setArticleModalOpen] = useState(false);
  const [editingArticle, setEditingArticle] = useState<MarketerArticleItem | null>(null);
  const [articleForm] = Form.useForm();

  // Banners state
  const [bannerModalOpen, setBannerModalOpen] = useState(false);
  const [editingBanner, setEditingBanner] = useState<MarketerBannerItem | null>(null);
  const [bannerForm] = Form.useForm();

  const articlesQuery = useQuery({
    queryKey: ['marketer-articles'],
    queryFn: marketerApi.listArticles,
  });

  const bannersQuery = useQuery({
    queryKey: ['marketer-banners'],
    queryFn: marketerApi.listBanners,
  });

  const saveArticleMutation = useMutation({
    mutationFn: (values: UpsertMarketerArticleRequest) => {
      if (editingArticle) {
        return marketerApi.updateArticle(editingArticle.id, values);
      }
      return marketerApi.createArticle(values);
    },
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: ['marketer-articles'] }),
        client.invalidateQueries({ queryKey: ['public-guides'] }),
      ]);
      message.success(editingArticle ? t("Đã cập nhật bài viết cẩm nang.") : t("Đã xuất bản bài viết cẩm nang mới."));
      setArticleModalOpen(false);
    },
    onError: (e: Error) => message.error(e.message),
  });

  const deleteArticleMutation = useMutation({
    mutationFn: (id: string) => marketerApi.deleteArticle(id),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['marketer-articles'] });
      message.success(t("Đã xóa bài viết cẩm nang."));
    },
    onError: (e: Error) => message.error(e.message),
  });

  const saveBannerMutation = useMutation({
    mutationFn: (values: {
      title: string;
      message: string;
      ctaText?: string;
      ctaUrl?: string;
      promoCode?: string;
      placement: string;
      targetSegment: string;
      themeColor: string;
      isActive: boolean;
      dateRange?: [dayjs.Dayjs, dayjs.Dayjs];
    }) => {
      const payload: UpsertMarketerBannerRequest = {
        title: values.title,
        message: values.message,
        ctaText: values.ctaText || null,
        ctaUrl: values.ctaUrl || null,
        promoCode: values.promoCode || null,
        placement: values.placement,
        targetSegment: values.targetSegment,
        themeColor: values.themeColor || 'teal',
        isActive: values.isActive ?? true,
        startsAt: values.dateRange?.[0] ? values.dateRange[0].startOf('day').toISOString() : null,
        endsAt: values.dateRange?.[1] ? values.dateRange[1].endOf('day').toISOString() : null,
      };
      if (editingBanner) {
        return marketerApi.updateBanner(editingBanner.id, payload);
      }
      return marketerApi.createBanner(payload);
    },
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: ['marketer-banners'] }),
        client.invalidateQueries({ queryKey: ['active-banners'] }),
      ]);
      message.success(editingBanner ? t("Đã cập nhật banner chiến dịch.") : t("Đã tạo banner chiến dịch mới."));
      setBannerModalOpen(false);
    },
    onError: (e: Error) => message.error(e.message),
  });

  const deleteBannerMutation = useMutation({
    mutationFn: (id: string) => marketerApi.deleteBanner(id),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['marketer-banners'] });
      message.success(t("Đã xóa banner chiến dịch."));
    },
    onError: (e: Error) => message.error(e.message),
  });

  const openCreateArticle = () => {
    setEditingArticle(null);
    articleForm.resetFields();
    articleForm.setFieldsValue({
      category: t("Vận hành nhà trọ"),
      readingTimeMinutes: 5,
      ctaText: t("Dùng thử miễn phí cho 5 phòng"),
      ctaUrl: '/register',
      isPublished: true,
    });
    setArticleModalOpen(true);
  };

  const openEditArticle = (item: MarketerArticleItem) => {
    setEditingArticle(item);
    articleForm.resetFields();
    articleForm.setFieldsValue({
      slug: item.slug,
      title: item.title,
      summary: item.summary,
      category: item.category,
      readingTimeMinutes: item.readingTimeMinutes,
      contentMarkdown: item.contentMarkdown,
      seoTitle: item.seoTitle ?? '',
      seoDescription: item.seoDescription ?? '',
      ctaText: item.ctaText ?? '',
      ctaUrl: item.ctaUrl ?? '/register',
      isPublished: item.isPublished,
    });
    setArticleModalOpen(true);
  };

  const openCreateBanner = () => {
    setEditingBanner(null);
    bannerForm.resetFields();
    bannerForm.setFieldsValue({
      placement: 'Both',
      targetSegment: 'All',
      themeColor: 'teal',
      ctaText: t("Nhận ưu đãi ngay"),
      ctaUrl: '/register',
      isActive: true,
    });
    setBannerModalOpen(true);
  };

  const openEditBanner = (item: MarketerBannerItem) => {
    setEditingBanner(item);
    bannerForm.resetFields();
    bannerForm.setFieldsValue({
      title: item.title,
      message: item.message,
      ctaText: item.ctaText ?? '',
      ctaUrl: item.ctaUrl ?? '',
      promoCode: item.promoCode ?? '',
      placement: item.placement,
      targetSegment: item.targetSegment,
      themeColor: item.themeColor,
      isActive: item.isActive,
      dateRange:
        item.startsAt && item.endsAt ? [dayjs(item.startsAt), dayjs(item.endsAt)] : undefined,
    });
    setBannerModalOpen(true);
  };

  if (articlesQuery.isError) {
    return <LoadError error={articlesQuery.error} onRetry={() => void articlesQuery.refetch()} />;
  }

  return (
    <div className="marketer-page">
      <PageTitle
        eyebrow="Content & On-site Marketing"
        title={t("Cẩm nang SEO & Banner Chiến dịch")}
        subtitle={t("Xuất bản bài viết hướng dẫn tại /huong-dan/:slug và cấu hình thanh thông báo ưu đãi trên Trang chủ / Bảng điều khiển chủ trọ.")}
      />

      <Tabs
        activeKey={activeTab}
        onChange={setActiveTab}
        size="large"
        items={[
          {
            key: 'articles',
            label: (
              <span>
                <BookOutlined style={{ marginRight: 6 }} />
                {t("Bài viết Cẩm nang SEO")}
              </span>
            ),
            children: (
              <SectionCard
                title={t("Danh sách Bài viết Cẩm nang (/huong-dan/:slug)")}
                extra={
                  <Button type="primary" icon={<PlusOutlined />} onClick={openCreateArticle}>
                    {t("Viết bài cẩm nang mới")}
                  </Button>
                }
              >
                <Table<MarketerArticleItem>
                  rowKey="id"
                  loading={articlesQuery.isLoading}
                  dataSource={articlesQuery.data ?? []}
                  scroll={{ x: 900 }}
                  pagination={{ pageSize: 10 }}
                  columns={[
                    {
                      title: t("Tiêu đề & Đường dẫn"),
                      key: 'title',
                      render: (_: unknown, r: MarketerArticleItem) => (
                        <div>
                          <div style={{ fontWeight: 600 }}>{r.title}</div>
                          <div style={{ fontSize: 12, color: '#0f766e' }}>
                            <code>/huong-dan/{r.slug}</code>
                          </div>
                        </div>
                      ),
                    },
                    {
                      title: t("Chuyên mục"),
                      dataIndex: 'category',
                      key: 'category',
                      render: (cat: string) => <Tag color="cyan">{cat}</Tag>,
                    },
                    {
                      title: t("Trạng thái"),
                      dataIndex: 'isPublished',
                      key: 'isPublished',
                      render: (pub: boolean) =>
                        pub ? (
                          <Tag color="green">{t("Đã xuất bản")}</Tag>
                        ) : (
                          <Tag color="default">{t("Bản nháp")}</Tag>
                        ),
                    },
                    {
                      title: t("Lượt xem / Nhấn CTA"),
                      key: 'stats',
                      align: 'right',
                      render: (_: unknown, r: MarketerArticleItem) => (
                        <span>
                          <strong>{r.viewCount}</strong> {t("xem")} ·{' '}
                          <strong style={{ color: '#0f766e' }}>{r.ctaClickCount}</strong> CTA
                        </span>
                      ),
                    },
                    {
                      title: t("Cập nhật"),
                      dataIndex: 'updatedAt',
                      key: 'updatedAt',
                      render: (val: string) => dayjs(val).format(dateFormat()),
                    },
                    {
                      title: t("THAO TÁC"),
                      key: 'actions',
                      render: (_: unknown, r: MarketerArticleItem) => (
                        <Space size={6}>
                          <Button
                            size="small"
                            icon={<EyeOutlined />}
                            href={`/huong-dan/${encodeURIComponent(r.slug)}`}
                            target="_blank"
                          >
                            {t("Xem")}
                          </Button>
                          <Button
                            size="small"
                            icon={<EditOutlined />}
                            onClick={() => openEditArticle(r)}
                          >
                            {t("Sửa")}
                          </Button>
                          <Popconfirm
                            title={t("Xóa bài viết này?")}
                            okText={t("Xóa")}
                            cancelText={t("Hủy")}
                            okButtonProps={{ danger: true }}
                            onConfirm={() => deleteArticleMutation.mutate(r.id)}
                          >
                            <Button size="small" danger icon={<DeleteOutlined />} />
                          </Popconfirm>
                        </Space>
                      ),
                    },
                  ]}
                />
              </SectionCard>
            ),
          },
          {
            key: 'banners',
            label: (
              <span>
                <NotificationOutlined style={{ marginRight: 6 }} />
                {t("Banner Chiến dịch & In-App")}
              </span>
            ),
            children: (
              <SectionCard
                title={t("Quản lý Banner Ưu đãi (Landing Page & Dashboard Chủ trọ)")}
                extra={
                  <Button type="primary" icon={<PlusOutlined />} onClick={openCreateBanner}>
                    {t("Tạo Banner mới")}
                  </Button>
                }
              >
                <Table<MarketerBannerItem>
                  rowKey="id"
                  loading={bannersQuery.isLoading}
                  dataSource={bannersQuery.data ?? []}
                  scroll={{ x: 900 }}
                  pagination={{ pageSize: 10 }}
                  columns={[
                    {
                      title: t("Tiêu đề & Nội dung"),
                      key: 'title',
                      render: (_: unknown, r: MarketerBannerItem) => (
                        <div>
                          <div style={{ fontWeight: 600 }}>{r.title}</div>
                          <div style={{ fontSize: 12, color: '#64748b' }}>{r.message}</div>
                          {r.promoCode && (
                            <Tag color="gold" style={{ marginTop: 4 }}>
                              Promo: <strong>{r.promoCode}</strong>
                            </Tag>
                          )}
                        </div>
                      ),
                    },
                    {
                      title: t("Vị trí hiển thị"),
                      dataIndex: 'placement',
                      key: 'placement',
                      render: (p: string) => {
                        if (p === 'LandingTop') return <Tag color="blue">{t("Đầu trang chủ")}</Tag>;
                        if (p === 'OwnerDashboard') return <Tag color="purple">{t("Bảng điều khiển Chủ trọ")}</Tag>;
                        return <Tag color="geekblue">{t("Cả hai nơi")}</Tag>;
                      },
                    },
                    {
                      title: t("Tệp mục tiêu"),
                      dataIndex: 'targetSegment',
                      key: 'targetSegment',
                      render: (seg: string) => <Tag>{seg}</Tag>,
                    },
                    {
                      title: t("Hiển thị / Nhấn"),
                      key: 'stats',
                      align: 'right',
                      render: (_: unknown, r: MarketerBannerItem) => (
                        <span>
                          <strong>{r.impressionsCount}</strong> {t("hiển thị")} ·{' '}
                          <strong style={{ color: '#0f766e' }}>{r.clicksCount}</strong> {t("nhấn")}
                        </span>
                      ),
                    },
                    {
                      title: t("Trạng thái"),
                      dataIndex: 'isActive',
                      key: 'isActive',
                      render: (active: boolean) =>
                        active ? <Tag color="green">{t("Đang bật")}</Tag> : <Tag>{t("Đang tắt")}</Tag>,
                    },
                    {
                      title: t("THAO TÁC"),
                      key: 'actions',
                      render: (_: unknown, r: MarketerBannerItem) => (
                        <Space size={6}>
                          <Button
                            size="small"
                            icon={<EditOutlined />}
                            onClick={() => openEditBanner(r)}
                          >
                            {t("Sửa")}
                          </Button>
                          <Popconfirm
                            title={t("Xóa banner này?")}
                            okText={t("Xóa")}
                            cancelText={t("Hủy")}
                            okButtonProps={{ danger: true }}
                            onConfirm={() => deleteBannerMutation.mutate(r.id)}
                          >
                            <Button size="small" danger icon={<DeleteOutlined />} />
                          </Popconfirm>
                        </Space>
                      ),
                    },
                  ]}
                />
              </SectionCard>
            ),
          },
        ]}
      />

      {/* Article Modal */}
      <Modal
        open={articleModalOpen}
        title={editingArticle ? t("Chỉnh sửa bài viết cẩm nang") : t("Viết bài cẩm nang SEO mới")}
        okText={editingArticle ? t("Lưu thay đổi") : t("Xuất bản bài viết")}
        cancelText={t("Hủy")}
        confirmLoading={saveArticleMutation.isPending}
        onCancel={() => setArticleModalOpen(false)}
        onOk={() => articleForm.submit()}
        width={780}
        destroyOnClose
      >
        <Form
          form={articleForm}
          layout="vertical"
          onFinish={(values) => saveArticleMutation.mutate(values)}
        >
          <Row gutter={16}>
            <Col xs={24} sm={14}>
              <Form.Item
                label={t("Tiêu đề bài viết")}
                name="title"
                rules={[{ required: true, message: t("Vui lòng nhập tiêu đề bài viết.") }]}
              >
                <Input placeholder={t("Ví dụ: Cách tính tiền điện nước phòng trọ minh bạch")} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={10}>
              <Form.Item
                label={t("Đường dẫn tĩnh (Slug)")}
                name="slug"
                rules={[{ required: true, message: t("Vui lòng nhập slug.") }]}
              >
                <Input placeholder="cach-tinh-tien-dien-nuoc-phong-tro" />
              </Form.Item>
            </Col>
          </Row>

          <Row gutter={16}>
            <Col xs={24} sm={10}>
              <Form.Item label={t("Chuyên mục")} name="category" required>
                <Input placeholder={t("Vận hành nhà trọ, Tài chính & Công nợ...")} />
              </Form.Item>
            </Col>
            <Col xs={12} sm={7}>
              <Form.Item label={t("Thời lượng đọc (phút)")} name="readingTimeMinutes" required>
                <InputNumber min={1} max={60} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={12} sm={7}>
              <Form.Item label={t("Xuất bản công khai")} name="isPublished" valuePropName="checked">
                <Switch checkedChildren={t("Công khai")} unCheckedChildren={t("Nháp")} />
              </Form.Item>
            </Col>
          </Row>

          <Form.Item
            label={t("Tóm tắt ngắn (Hiển thị trên thẻ bài viết & Meta Description)")}
            name="summary"
            rules={[{ required: true, message: t("Vui lòng nhập tóm tắt bài viết.") }]}
          >
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>

          <Form.Item
            label={t("Nội dung bài viết (Hỗ trợ Markdown: ## Tiêu đề, - Danh sách)")}
            name="contentMarkdown"
            rules={[{ required: true, message: t("Vui lòng nhập nội dung bài viết.") }]}
          >
            <Input.TextArea rows={10} />
          </Form.Item>

          <Row gutter={16}>
            <Col xs={24} sm={12}>
              <Form.Item label={t("Nút kêu gọi hành động (CTA Text)")} name="ctaText">
                <Input placeholder={t("Dùng thử miễn phí cho 5 phòng")} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label={t("Đường dẫn đích CTA (CTA URL)")} name="ctaUrl">
                <Input placeholder="/register?promo=CAMNANG" />
              </Form.Item>
            </Col>
          </Row>

          <Form.Item noStyle shouldUpdate>
            {() => {
              const titleVal = (articleForm.getFieldValue('title') as string) || '';
              const summaryVal = (articleForm.getFieldValue('summary') as string) || '';
              const mdVal = (articleForm.getFieldValue('contentMarkdown') as string) || '';
              const ctaUrlVal = (articleForm.getFieldValue('ctaUrl') as string) || '';
              const hasHeading = /(^|\n)##\s+/m.test(mdVal);
              const titleOk = titleVal.trim().length >= 20 && titleVal.trim().length <= 120;
              const summaryOk = summaryVal.trim().length >= 40 && summaryVal.trim().length <= 500;
              const ctaOk = ctaUrlVal.trim().length > 0;
              return (
                <div
                  style={{
                    marginTop: 8,
                    padding: '10px 14px',
                    background: '#f8fafc',
                    borderRadius: 8,
                    border: '1px solid #e2e8f0',
                  }}
                >
                  <div style={{ fontSize: 12, fontWeight: 700, color: '#334155', marginBottom: 6 }}>
                    {t("Kiểm tra chuẩn SEO & xem trước")}:
                  </div>
                  <Space size={6} wrap>
                    <Tag color={titleOk ? 'green' : 'default'}>
                      {t("Tiêu đề 20-120 ký tự")} ({titleVal.trim().length})
                    </Tag>
                    <Tag color={summaryOk ? 'green' : 'default'}>
                      {t("Mô tả Meta 40-500 ký tự")} ({summaryVal.trim().length})
                    </Tag>
                    <Tag color={hasHeading ? 'green' : 'default'}>
                      {t("Có thẻ tiêu đề phụ (##)")}
                    </Tag>
                    <Tag color={ctaOk ? 'green' : 'default'}>
                      {t("Có liên kết chuyển đổi CTA")}
                    </Tag>
                  </Space>
                </div>
              );
            }}
          </Form.Item>
        </Form>
      </Modal>

      {/* Banner Modal */}
      <Modal
        open={bannerModalOpen}
        title={editingBanner ? t("Chỉnh sửa Banner chiến dịch") : t("Tạo Banner chiến dịch mới")}
        okText={editingBanner ? t("Lưu thay đổi") : t("Tạo Banner")}
        cancelText={t("Hủy")}
        confirmLoading={saveBannerMutation.isPending}
        onCancel={() => setBannerModalOpen(false)}
        onOk={() => bannerForm.submit()}
        width={680}
        destroyOnClose
      >
        <Form
          form={bannerForm}
          layout="vertical"
          onFinish={(values) => saveBannerMutation.mutate(values)}
        >
          <Form.Item
            label={t("Tiêu đề ngắn")}
            name="title"
            rules={[{ required: true, message: t("Vui lòng nhập tiêu đề banner.") }]}
          >
            <Input placeholder={t("Ví dụ: Ưu đãi tháng 10: Tặng 2 tháng Roomie Pro")} />
          </Form.Item>

          <Form.Item
            label={t("Thông điệp chi tiết")}
            name="message"
            rules={[{ required: true, message: t("Vui lòng nhập thông điệp.") }]}
          >
            <Input.TextArea rows={2} placeholder={t("Nhập mã TET2026 hoặc nhấn vào đây để kích hoạt tự động.")} />
          </Form.Item>

          <Row gutter={16}>
            <Col xs={24} sm={8}>
              <Form.Item label={t("Vị trí hiển thị")} name="placement" required>
                <Select
                  options={[
                    { value: 'LandingTop', label: t("Đầu trang chủ") },
                    { value: 'OwnerDashboard', label: t("Bảng điều khiển Chủ trọ") },
                    { value: 'Both', label: t("Cả hai nơi") },
                  ]}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item label={t("Phân khúc chủ trọ")} name="targetSegment" required>
                <Select
                  options={[
                    { value: 'All', label: t("Tất cả") },
                    { value: 'FreeOnly', label: t("Chỉ chủ trọ Free") },
                    { value: 'NearRoomLimit', label: t("Sắp chạm trần 4-5 phòng") },
                    { value: 'ProExpiringSoon', label: t("Pro sắp hết hạn") },
                  ]}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item label={t("Tông màu")} name="themeColor" required>
                <Select
                  options={[
                    { value: 'teal', label: t("Xanh ngọc (Teal)") },
                    { value: 'gold', label: t("Vàng ấm (Gold)") },
                    { value: 'coral', label: t("Cam san hô (Coral)") },
                    { value: 'blue', label: t("Xanh dương (Blue)") },
                  ]}
                />
              </Form.Item>
            </Col>
          </Row>

          <Row gutter={16}>
            <Col xs={24} sm={8}>
              <Form.Item label={t("Nhãn nút bấm (CTA)")} name="ctaText">
                <Input placeholder={t("Nhận ưu đãi ngay")} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item label={t("Đường dẫn CTA")} name="ctaUrl">
                <Input placeholder="/register?promo=TET2026" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item label={t("Mã ưu đãi đính kèm")} name="promoCode">
                <Input placeholder="TET2026" />
              </Form.Item>
            </Col>
          </Row>

          <Row gutter={16}>
            <Col xs={24} sm={16}>
              <Form.Item label={t("Thời gian hiển thị (Bỏ trống = Luôn hiển thị)")} name="dateRange">
                <DatePicker.RangePicker format={dateFormat()} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item label={t("Kích hoạt")} name="isActive" valuePropName="checked">
                <Switch checkedChildren={t("Đang bật")} unCheckedChildren={t("Đang tắt")} />
              </Form.Item>
            </Col>
          </Row>

          <Form.Item noStyle shouldUpdate>
            {() => {
              const titleVal = (bannerForm.getFieldValue('title') as string) || t("Tiêu đề banner ưu đãi");
              const msgVal = (bannerForm.getFieldValue('message') as string) || t("Nội dung thông điệp chiến dịch");
              const themeVal = (bannerForm.getFieldValue('themeColor') as string) || 'teal';
              const promoVal = (bannerForm.getFieldValue('promoCode') as string) || '';
              const ctaVal = (bannerForm.getFieldValue('ctaText') as string) || '';
              return (
                <div style={{ marginTop: 6 }}>
                  <div style={{ fontSize: 12, fontWeight: 700, color: '#334155', marginBottom: 6 }}>
                    {t("Xem trước hiển thị Banner")}:
                  </div>
                  <div className={`active-marketing-banner theme-${themeVal}`} style={{ marginBottom: 0 }}>
                    <div>
                      <strong>{titleVal}</strong> — <span>{msgVal}</span>
                      {promoVal ? (
                        <Tag color="gold" style={{ marginLeft: 8 }}>
                          <strong>{promoVal}</strong>
                        </Tag>
                      ) : null}
                    </div>
                    {ctaVal ? (
                      <Button size="small" type="primary" style={{ background: '#fff', color: '#0f766e', borderColor: '#fff', fontWeight: 600 }}>
                        {ctaVal}
                      </Button>
                    ) : null}
                  </div>
                </div>
              );
            }}
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
