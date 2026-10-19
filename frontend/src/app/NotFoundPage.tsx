import LanguageSelector from '../shared/i18n/LanguageSelector';
import { t } from '../shared/i18n/runtime';
import { Button } from 'antd';
import { HomeOutlined } from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { FullPageError } from '../shared/ui/Ui';

export default function NotFoundPage() {
  return <><div className="public-language"><LanguageSelector /></div><FullPageError title={t("Không tìm thấy trang")} subTitle={t("Đường dẫn có thể đã thay đổi hoặc không tồn tại.")} onBack={<Button type="primary" icon={<HomeOutlined />}><Link to="/">{t("Về trang chính")}</Link></Button>} /></>;
}
