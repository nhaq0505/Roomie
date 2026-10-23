import { DeleteOutlined, DownloadOutlined, FilePdfOutlined, UploadOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Drawer, Empty, Popconfirm, Progress, Spin, Typography, Upload } from 'antd';
import { formatVnd } from '../../shared/format';
import { formatDate, t } from '../../shared/i18n/runtime';
import ProUpsell from '../../shared/ui/ProUpsell';
import { LoadError } from '../../shared/ui/Ui';
import { attachmentsApi } from './api';
import { expenseCategoryLabel, formatBytes } from './categories';
import { prepareAttachment } from './compressImage';
import type { Expense } from './types';

const accept = 'image/jpeg,image/png,image/webp,application/pdf';

export default function AttachmentDrawer({ expense, isPro, onClose }: { expense: Expense | null; isPro: boolean; onClose: () => void }) {
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const expenseId = expense?.id;
  const listQuery = useQuery({ queryKey: ['expenses', 'attachments', expenseId], queryFn: () => attachmentsApi.list(expenseId!), enabled: !!expenseId });
  const usageQuery = useQuery({ queryKey: ['expenses', 'attachment-usage'], queryFn: attachmentsApi.usage, enabled: !!expenseId });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['expenses'] });

  const uploadMutation = useMutation({
    mutationFn: async (file: File) => {
      const prepared = await prepareAttachment(file);
      const limit = usageQuery.data?.maxFileBytes ?? 2 * 1024 * 1024;
      if (prepared.blob.size > limit) throw new Error(t("Tệp chứng từ tối đa {0}.", [formatBytes(limit)]));
      return attachmentsApi.upload(expenseId!, prepared.blob, prepared.name);
    },
    onSuccess: async () => { await refresh(); message.success(t("Đã tải chứng từ lên.")); },
    onError: (error: Error) => message.error(error.message),
  });
  const deleteMutation = useMutation({
    mutationFn: (attachmentId: string) => attachmentsApi.remove(expenseId!, attachmentId),
    onSuccess: async () => { await refresh(); message.success(t("Đã xóa chứng từ.")); },
    onError: (error: Error) => message.error(error.message),
  });

  const items = listQuery.data ?? [];
  const usage = usageQuery.data;
  const maxFiles = usage?.maxFilesPerExpense ?? 5;
  const percent = usage ? Math.min(100, Math.round((usage.usedBytes / usage.limitBytes) * 100)) : 0;

  return <Drawer className="expense-attachments-drawer" title={t("Chứng từ khoản chi")} open={!!expense} onClose={onClose} width={480} destroyOnClose>
    {expense && <div className="expense-attachment-summary">
      <strong>{expenseCategoryLabel(expense.category)} · {formatVnd(expense.amount)}</strong>
      <span>{formatDate(expense.expenseDate)}{expense.buildingName ? ` · ${expense.buildingName}` : ''}{expense.roomName ? ` · ${expense.roomName}` : ''}</span>
    </div>}
    {isPro ? <div className="expense-attachment-upload">
      <Upload accept={accept} showUploadList={false} multiple={false} disabled={uploadMutation.isPending || items.length >= maxFiles}
        beforeUpload={(file) => { uploadMutation.mutate(file); return false; }}>
        <Button type="primary" icon={<UploadOutlined />} loading={uploadMutation.isPending} disabled={items.length >= maxFiles}>{t("Tải ảnh hoặc PDF")}</Button>
      </Upload>
      <Typography.Text type="secondary">{t("Tối đa {0} tệp, mỗi tệp 2 MB. Ảnh được nén tự động.", [maxFiles])}</Typography.Text>
      {usage && <div className="expense-attachment-usage"><span>{t("Dung lượng đã dùng: {0} / {1}", [formatBytes(usage.usedBytes), formatBytes(usage.limitBytes)])}</span><Progress percent={percent} size="small" showInfo={false} status={percent >= 90 ? 'exception' : 'normal'} /></div>}
    </div> : <ProUpsell compact title={t("Lưu ảnh chứng từ với gói Pro")} description={t("Chụp hóa đơn điện, nước, phiếu sửa chữa và lưu kèm khoản chi. Chứng từ đã lưu vẫn xem được khi hết Pro.")} />}
    {listQuery.isError ? <LoadError error={listQuery.error} onRetry={() => void listQuery.refetch()} />
      : listQuery.isPending ? <div className="expense-attachment-loading"><Spin /></div>
        : items.length === 0 ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t("Chưa có chứng từ")} />
          : <ul className="expense-attachment-list">{items.map((item) => <li key={item.id}>
            {item.contentType.startsWith('image/')
              ? <a href={attachmentsApi.url(item.expenseId, item.id)} target="_blank" rel="noreferrer" className="expense-attachment-thumb"><img src={attachmentsApi.url(item.expenseId, item.id)} alt={item.fileName} loading="lazy" /></a>
              : <span className="expense-attachment-thumb expense-attachment-pdf" aria-hidden="true"><FilePdfOutlined /></span>}
            <div className="expense-attachment-meta"><strong title={item.fileName}>{item.fileName}</strong><span>{formatBytes(item.sizeBytes)} · {formatDate(item.createdAt)}</span></div>
            <div className="expense-attachment-actions">
              <Button type="text" icon={<DownloadOutlined />} href={attachmentsApi.url(item.expenseId, item.id, true)} aria-label={t("Tải xuống {0}", [item.fileName])} />
              <Popconfirm title={t("Xóa chứng từ này?")} okText={t("Xóa")} cancelText={t("Hủy")} okButtonProps={{ danger: true }} onConfirm={() => deleteMutation.mutateAsync(item.id)}>
                <Button type="text" danger icon={<DeleteOutlined />} aria-label={t("Xóa {0}", [item.fileName])} loading={deleteMutation.isPending && deleteMutation.variables === item.id} />
              </Popconfirm>
            </div>
          </li>)}</ul>}
  </Drawer>;
}
