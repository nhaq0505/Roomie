import { App, ConfigProvider, Grid } from 'antd';
import { useNavigate, useParams } from 'react-router-dom';
import ContractDocumentsDrawer from './ContractDocumentsDrawer';
import { roomTheme } from './theme';

export default function RoomDocumentsPage() {
  const screens = Grid.useBreakpoint();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  return (
    <ConfigProvider theme={roomTheme(!screens.md)}>
      <App component={false}>
        <ContractDocumentsDrawer
          roomId={id}
          asPage
          onClose={() => navigate('/rooms')}
        />
      </App>
    </ConfigProvider>
  );
}
