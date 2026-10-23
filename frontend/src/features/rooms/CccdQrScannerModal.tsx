import { CameraOutlined, PictureOutlined, QrcodeOutlined, RetweetOutlined, UploadOutlined } from '@ant-design/icons';
import { Alert, Button, Grid, Input, Modal, Segmented, Space, Spin, Typography } from 'antd';
import { useCallback, useEffect, useRef, useState } from 'react';
import { t } from '../../shared/i18n/runtime';
import { decodeCccdQrFromImageBlob, decodeCccdQrFromVideoFrame, parseCccdQr, type ParsedCccdQr } from './cccdQr';

type ScanTab = 'image' | 'camera';

export interface CccdQrScannerModalProps {
  open: boolean;
  title?: string;
  onClose: () => void;
  onDetected: (parsed: ParsedCccdQr) => void;
}

export default function CccdQrScannerModal({
  open,
  title,
  onClose,
  onDetected,
}: CccdQrScannerModalProps) {
  const screens = Grid.useBreakpoint();
  const isMobile = !screens.md;
  const [tab, setTab] = useState<ScanTab>(isMobile ? 'camera' : 'image');
  const [facingMode, setFacingMode] = useState<'environment' | 'user'>('environment');
  const [rawText, setRawText] = useState('');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [isProcessingImage, setIsProcessingImage] = useState(false);
  const [isCameraLoading, setIsCameraLoading] = useState(false);
  const [isDragging, setIsDragging] = useState(false);
  const [cameraRetryTick, setCameraRetryTick] = useState(0);

  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const scanTimerRef = useRef<number | null>(null);
  const hasDetectedRef = useRef(false);

  const stopCamera = useCallback(() => {
    if (scanTimerRef.current !== null) {
      window.clearInterval(scanTimerRef.current);
      scanTimerRef.current = null;
    }
    if (streamRef.current) {
      for (const track of streamRef.current.getTracks()) {
        track.stop();
      }
      streamRef.current = null;
    }
    if (videoRef.current) {
      videoRef.current.srcObject = null;
    }
  }, []);

  const attachVideoRef = useCallback((node: HTMLVideoElement | null) => {
    videoRef.current = node;
    if (node && streamRef.current && node.srcObject !== streamRef.current) {
      node.srcObject = streamRef.current;
      void node.play().catch(() => undefined);
    }
  }, []);

  const handleSuccess = useCallback(
    (parsed: ParsedCccdQr) => {
      if (hasDetectedRef.current) return;
      hasDetectedRef.current = true;
      stopCamera();
      setRawText('');
      setErrorMsg(null);
      onDetected(parsed);
      onClose();
    },
    [onClose, onDetected, stopCamera],
  );

  const processImageBlob = useCallback(
    async (blob: Blob) => {
      setErrorMsg(null);
      setIsProcessingImage(true);
      try {
        const parsed = await decodeCccdQrFromImageBlob(blob);
        if (parsed) {
          handleSuccess(parsed);
        } else {
          setErrorMsg(t("Không tìm thấy mã QR CCCD hợp lệ trong ảnh. Hãy chọn ảnh chụp rõ góc trên bên phải mặt trước CCCD."));
        }
      } catch {
        setErrorMsg(t("Không thể đọc file ảnh đã chọn. Vui lòng thử ảnh khác."));
      } finally {
        setIsProcessingImage(false);
      }
    },
    [handleSuccess],
  );

  useEffect(() => {
    if (!open) {
      stopCamera();
      hasDetectedRef.current = false;
      setErrorMsg(null);
      setRawText('');
      setIsProcessingImage(false);
      return;
    }
    hasDetectedRef.current = false;
    setErrorMsg(null);
    setTab(isMobile ? 'camera' : 'image');
  }, [open, isMobile, stopCamera]);

  useEffect(() => {
    if (!open || tab !== 'camera') {
      stopCamera();
      return;
    }

    let cancelled = false;
    const startCamera = async () => {
      if (!navigator.mediaDevices?.getUserMedia) {
        setErrorMsg(t("Trình duyệt không hỗ trợ mở camera trực tiếp. Hãy chuyển sang tab Chọn ảnh / Dán từ Zalo."));
        return;
      }
      setIsCameraLoading(true);
      setErrorMsg(null);
      try {
        let stream: MediaStream;
        try {
          stream = await navigator.mediaDevices.getUserMedia({
            video: {
              facingMode: { ideal: facingMode },
              width: { ideal: 1920 },
              height: { ideal: 1080 },
            },
            audio: false,
          });
        } catch {
          try {
            stream = await navigator.mediaDevices.getUserMedia({
              video: { facingMode: { ideal: facingMode } },
              audio: false,
            });
          } catch {
            stream = await navigator.mediaDevices.getUserMedia({
              video: true,
              audio: false,
            });
          }
        }
        if (cancelled) {
          for (const track of stream.getTracks()) track.stop();
          return;
        }
        const [videoTrack] = stream.getVideoTracks();
        if (videoTrack?.applyConstraints) {
          void videoTrack
            .applyConstraints({
              advanced: [{ focusMode: 'continuous' } as MediaTrackConstraintSet],
            })
            .catch(() => undefined);
        }
        streamRef.current = stream;
        if (videoRef.current) {
          videoRef.current.srcObject = stream;
          await videoRef.current.play().catch(() => undefined);
        }
        if (!canvasRef.current) {
          canvasRef.current = document.createElement('canvas');
        }
        let scanningFrame = false;
        scanTimerRef.current = window.setInterval(() => {
          if (scanningFrame || hasDetectedRef.current || !videoRef.current || !canvasRef.current) return;
          scanningFrame = true;
          void decodeCccdQrFromVideoFrame(videoRef.current, canvasRef.current)
            .then((parsed) => {
              if (parsed && !cancelled) {
                handleSuccess(parsed);
              }
            })
            .finally(() => {
              scanningFrame = false;
            });
        }, 180);
      } catch {
        if (!cancelled) {
          setErrorMsg(t("Không thể truy cập camera. Hãy cấp quyền camera hoặc dùng tab Chọn ảnh / Dán từ Zalo."));
        }
      } finally {
        if (!cancelled) {
          setIsCameraLoading(false);
        }
      }
    };

    void startCamera();
    return () => {
      cancelled = true;
      stopCamera();
    };
  }, [open, tab, facingMode, cameraRetryTick, handleSuccess, stopCamera]);

  useEffect(() => {
    if (!open) return;
    const onPaste = (event: ClipboardEvent) => {
      const items = event.clipboardData?.items;
      if (!items) return;
      for (const item of Array.from(items)) {
        if (item.type.startsWith('image/')) {
          const file = item.getAsFile();
          if (file) {
            event.preventDefault();
            void processImageBlob(file);
            return;
          }
        }
      }
      const pastedText = event.clipboardData?.getData('text/plain');
      if (pastedText && pastedText.includes('|')) {
        const parsed = parseCccdQr(pastedText);
        if (parsed) {
          event.preventDefault();
          handleSuccess(parsed);
        }
      }
    };

    window.addEventListener('paste', onPaste);
    return () => window.removeEventListener('paste', onPaste);
  }, [open, processImageBlob, handleSuccess]);

  const handleApplyRawText = () => {
    const parsed = parseCccdQr(rawText);
    if (!parsed) {
      setErrorMsg(t("Chuỗi QR không đúng định dạng CCCD Việt Nam."));
      return;
    }
    handleSuccess(parsed);
  };

  return (
    <Modal
      open={open}
      title={
        <Space>
          <QrcodeOutlined />
          <span>{title || t("Quét mã QR trên thẻ CCCD")}</span>
        </Space>
      }
      onCancel={onClose}
      footer={null}
      destroyOnClose
      width={520}
      className="room-editor cccd-qr-modal"
    >
      <input
        ref={fileInputRef}
        type="file"
        accept="image/*"
        style={{ display: 'none' }}
        onChange={(e) => {
          const file = e.target.files?.[0];
          e.target.value = '';
          if (file) {
            void processImageBlob(file);
          }
        }}
      />

      <div style={{ marginBottom: 16 }}>
        <Segmented
          block
          value={tab}
          onChange={(val) => {
            setErrorMsg(null);
            setTab(val as ScanTab);
          }}
          options={[
            {
              value: 'image',
              icon: <PictureOutlined />,
              label: t("Chọn ảnh / Dán Zalo (PC)"),
            },
            {
              value: 'camera',
              icon: <CameraOutlined />,
              label: t("Mở Camera quét"),
            },
          ]}
        />
      </div>

      {errorMsg && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 14 }}
          message={errorMsg}
          action={
            tab === 'camera' ? (
              <Button
                size="small"
                type="primary"
                icon={<CameraOutlined />}
                onClick={() => setCameraRetryTick((c) => c + 1)}
              >
                {t("Thử lại")}
              </Button>
            ) : undefined
          }
        />
      )}

      {tab === 'image' ? (
        <div className="cccd-qr-image-pane">
          <div
            role="button"
            tabIndex={0}
            aria-label={t("Chọn ảnh CCCD từ thiết bị")}
            className={`cccd-qr-dropzone${isDragging ? ' is-dragging' : ''}`}
            onClick={() => fileInputRef.current?.click()}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                fileInputRef.current?.click();
              }
            }}
            onDragOver={(e) => {
              e.preventDefault();
              setIsDragging(true);
            }}
            onDragLeave={() => setIsDragging(false)}
            onDrop={(e) => {
              e.preventDefault();
              setIsDragging(false);
              const file = e.dataTransfer.files?.[0];
              if (file) {
                void processImageBlob(file);
              }
            }}
          >
            {isProcessingImage ? (
              <Space direction="vertical" align="center" size={10}>
                <Spin size="large" />
                <Typography.Text strong>{t("Đang giải mã QR từ ảnh CCCD…")}</Typography.Text>
              </Space>
            ) : (
              <Space direction="vertical" align="center" size={8}>
                <UploadOutlined className="cccd-qr-dropzone-icon" />
                <Typography.Text strong style={{ fontSize: 15 }}>
                  {t("Bấm chọn ảnh mặt trước CCCD hoặc kéo thả vào đây")}
                </Typography.Text>
                <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                  {t("Mẹo trên máy tính: Copy ảnh CCCD từ Zalo rồi bấm Ctrl + V ngay tại cửa sổ này.")}
                </Typography.Text>
              </Space>
            )}
          </div>

          <div className="cccd-qr-raw-box">
            <label htmlFor="cccd-qr-raw-input" className="cccd-qr-raw-label">
              {t("Hoặc dán chuỗi văn bản QR (nếu quét bằng Zalo điện thoại):")}
            </label>
            <Input.TextArea
              id="cccd-qr-raw-input"
              rows={2}
              value={rawText}
              onChange={(e) => {
                const nextVal = e.target.value;
                setRawText(nextVal);
                setErrorMsg(null);
                if (nextVal.includes('|')) {
                  const autoParsed = parseCccdQr(nextVal);
                  if (autoParsed) {
                    handleSuccess(autoParsed);
                  }
                }
              }}
              placeholder="079099001234|025123456|Nguyen Van A|15081999|Nam|..."
            />
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 8 }}>
              <Button
                type="primary"
                disabled={!rawText.trim()}
                onClick={handleApplyRawText}
              >
                {t("Điền thông tin từ chuỗi QR")}
              </Button>
            </div>
          </div>
        </div>
      ) : (
        <div className="cccd-qr-camera-pane">
          <div className="cccd-qr-video-wrapper">
            <video
              ref={attachVideoRef}
              playsInline
              muted
              autoPlay
              className="cccd-qr-video"
            />
            <div className="cccd-qr-viewfinder" aria-hidden="true" />
            {isCameraLoading && (
              <div className="cccd-qr-video-overlay">
                <Spin size="large" />
              </div>
            )}
          </div>
          <Typography.Paragraph type="secondary" style={{ textAlign: 'center', marginTop: 10, marginBottom: 12 }}>
            {t("Đưa góc trên bên phải mặt trước thẻ CCCD vào khung hình để tự động nhận diện.")}
          </Typography.Paragraph>
          <Space style={{ width: '100%', justifyContent: 'center' }} wrap>
            <Button
              icon={<RetweetOutlined />}
              onClick={() => setFacingMode((prev) => (prev === 'environment' ? 'user' : 'environment'))}
            >
              {t("Đổi camera")}
            </Button>
            <Button
              icon={<PictureOutlined />}
              onClick={() => fileInputRef.current?.click()}
            >
              {t("Chọn ảnh CCCD từ thiết bị")}
            </Button>
          </Space>
        </div>
      )}
    </Modal>
  );
}
