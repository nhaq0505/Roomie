import { CopyOutlined, LinkOutlined } from '@ant-design/icons';
import { App, Button, Form, Input, Modal, Radio, Space, Tag, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { t } from '../../../shared/i18n/runtime';

interface UtmLinkBuilderModalProps {
  open: boolean;
  onClose: () => void;
}

export function UtmLinkBuilderModal({ open, onClose }: UtmLinkBuilderModalProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm();

  const [baseUrlPreset, setBaseUrlPreset] = useState<string>('landing');
  const [customBaseUrl, setCustomBaseUrl] = useState<string>('');
  const [source, setSource] = useState<string>('facebook');
  const [medium, setMedium] = useState<string>('cpc');
  const [campaign, setCampaign] = useState<string>('');
  const [content, setContent] = useState<string>('');
  const [term, setTerm] = useState<string>('');

  const resolvedBaseUrl = useMemo(() => {
    const origin = typeof window !== 'undefined' ? window.location.origin : 'https://roomie.vn';
    if (baseUrlPreset === 'landing') return `${origin}/`;
    if (baseUrlPreset === 'register') return `${origin}/register`;
    if (baseUrlPreset === 'pricing') return `${origin}/#pricing`;
    return customBaseUrl.trim() || `${origin}/`;
  }, [baseUrlPreset, customBaseUrl]);

  const generatedUrl = useMemo(() => {
    try {
      const url = new URL(resolvedBaseUrl);
      if (source.trim()) url.searchParams.set('utm_source', source.trim().toLowerCase());
      if (medium.trim()) url.searchParams.set('utm_medium', medium.trim().toLowerCase());
      if (campaign.trim()) url.searchParams.set('utm_campaign', campaign.trim().toLowerCase());
      if (content.trim()) url.searchParams.set('utm_content', content.trim());
      if (term.trim()) url.searchParams.set('utm_term', term.trim());
      return url.toString();
    } catch {
      return resolvedBaseUrl;
    }
  }, [resolvedBaseUrl, source, medium, campaign, content, term]);

  const handleCopy = async () => {
    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard) {
        await navigator.clipboard.writeText(generatedUrl);
      } else {
        const input = document.createElement('textarea');
        input.value = generatedUrl;
        document.body.appendChild(input);
        input.select();
        document.execCommand('copy');
        document.body.removeChild(input);
      }
      message.success(t("Đã sao chép liên kết."));
    } catch {
      message.error(t("Không thể sao chép liên kết."));
    }
  };

  const sourcePresets = ['facebook', 'google', 'zalo', 'tiktok', 'email', 'referral'];
  const mediumPresets = ['cpc', 'social', 'banner', 'chat', 'email', 'organic'];

  return (
    <Modal
      title={
        <Space size={8}>
          <LinkOutlined style={{ color: '#17a486' }} />
          <span>{t("Tạo liên kết UTM")}</span>
        </Space>
      }
      open={open}
      onCancel={onClose}
      footer={[
        <Button key="close" onClick={onClose}>
          {t("Đóng")}
        </Button>,
        <Button
          key="copy"
          type="primary"
          icon={<CopyOutlined />}
          onClick={handleCopy}
          style={{ background: '#17a486', borderColor: '#17a486' }}
        >
          {t("Sao chép liên kết")}
        </Button>,
      ]}
      width={640}
      destroyOnClose
    >
      <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
        <Form.Item label={t("Đường dẫn gốc")}>
          <Radio.Group
            value={baseUrlPreset}
            onChange={(e) => setBaseUrlPreset(e.target.value)}
            style={{ marginBottom: 8 }}
          >
            <Radio.Button value="landing">{t("Trang chủ")}</Radio.Button>
            <Radio.Button value="register">{t("Đăng ký")}</Radio.Button>
            <Radio.Button value="pricing">{t("Bảng giá")}</Radio.Button>
            <Radio.Button value="custom">{t("Tùy chọn")}</Radio.Button>
          </Radio.Group>
          {baseUrlPreset === 'custom' && (
            <Input
              value={customBaseUrl}
              onChange={(e) => setCustomBaseUrl(e.target.value)}
              placeholder="https://roomie.vn/custom-page"
              style={{ marginTop: 6 }}
            />
          )}
        </Form.Item>

        <Form.Item label={t("Nguồn")} required>
          <Input
            value={source}
            onChange={(e) => setSource(e.target.value)}
            placeholder="vd: facebook, google, zalo"
          />
          <div style={{ marginTop: 6 }}>
            <Space size={4} wrap>
              {sourcePresets.map((tag) => (
                <Tag
                  key={tag}
                  style={{ cursor: 'pointer' }}
                  color={source === tag ? 'geekblue' : undefined}
                  onClick={() => setSource(tag)}
                >
                  {tag}
                </Tag>
              ))}
            </Space>
          </div>
        </Form.Item>

        <Form.Item label={t("Kênh")}>
          <Input
            value={medium}
            onChange={(e) => setMedium(e.target.value)}
            placeholder="vd: cpc, social, email"
          />
          <div style={{ marginTop: 6 }}>
            <Space size={4} wrap>
              {mediumPresets.map((tag) => (
                <Tag
                  key={tag}
                  style={{ cursor: 'pointer' }}
                  color={medium === tag ? 'geekblue' : undefined}
                  onClick={() => setMedium(tag)}
                >
                  {tag}
                </Tag>
              ))}
            </Space>
          </div>
        </Form.Item>

        <Form.Item label={t("Chiến dịch")}>
          <Input
            value={campaign}
            onChange={(e) => setCampaign(e.target.value)}
            placeholder="vd: phong_tro_t10_2026, khuyen_mai_tet"
          />
        </Form.Item>

        <Space style={{ display: 'flex', width: '100%' }} size={12}>
          <Form.Item label={t("Nội dung")} style={{ flex: 1 }}>
            <Input
              value={content}
              onChange={(e) => setContent(e.target.value)}
              placeholder="vd: banner_hero_a, textlink"
            />
          </Form.Item>

          <Form.Item label={t("Từ khóa")} style={{ flex: 1 }}>
            <Input
              value={term}
              onChange={(e) => setTerm(e.target.value)}
              placeholder="vd: phong_tro_gia_re"
            />
          </Form.Item>
        </Space>

        <div
          style={{
            marginTop: 12,
            padding: '12px 16px',
            background: '#f4f8f7',
            borderRadius: 8,
            border: '1px solid #d3e7e2',
          }}
        >
          <Typography.Text type="secondary" style={{ fontSize: 13, display: 'block', marginBottom: 4 }}>
            {t("Liên kết đã tạo")}:
          </Typography.Text>
          <Typography.Paragraph
            code
            copyable
            style={{
              wordBreak: 'break-all',
              marginBottom: 0,
              fontSize: 13,
              color: '#17383d',
              padding: '6px 8px',
              background: '#fff',
              borderRadius: 6,
            }}
          >
            {generatedUrl}
          </Typography.Paragraph>
        </div>
      </Form>
    </Modal>
  );
}
