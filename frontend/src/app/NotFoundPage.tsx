import LanguageSelector from '../shared/i18n/LanguageSelector';
import { t } from '../shared/i18n/runtime';
import { Button } from 'antd';
import { HomeOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { FullPageError } from '../shared/ui/Ui';

export default function NotFoundPage() {
  const navigate = useNavigate();
  return <><div className="public-language"><LanguageSelector /></div><FullPageError title={t("Không tìm thấy trang")} subTitle={t("Đường dẫn có thể đã thay đổi hoặc không tồn tại.")} onBack={<Button type="primary" icon={<HomeOutlined />} onClick={() => navigate('/')}>{t("Về trang chính")}</Button>} /></>;
}
