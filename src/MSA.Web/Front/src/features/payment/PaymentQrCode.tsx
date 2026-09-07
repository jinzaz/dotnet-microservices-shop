import { useEffect, useState } from 'react';
import * as QRCode from 'qrcode';

function openablePaymentUrl(value: string): string | null {
  try {
    const parsed = new URL(value);
    return ['https:', 'http:', 'alipays:', 'weixin:'].includes(parsed.protocol) ? value : null;
  } catch {
    return null;
  }
}

export function PaymentQrCode({ value, label }: { value: string | null; label: string }) {
  const [source, setSource] = useState('');
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    setSource('');
    setFailed(false);
    if (!value) return () => { active = false; };

    void QRCode.toDataURL(value, {
      errorCorrectionLevel: 'M',
      margin: 2,
      width: 320,
      color: { dark: '#071a3dff', light: '#ffffffff' },
    }).then((result) => {
      if (active) setSource(result);
    }).catch(() => {
      if (active) setFailed(true);
    });

    return () => { active = false; };
  }, [value]);

  if (failed) {
    return <div className="payment-qr__state" role="alert">二维码生成失败，请刷新支付状态。</div>;
  }

  if (!source) {
    return (
      <div className="payment-qr__state" role="status">
        <span className="page-spinner" />
        <span>{value ? '正在生成支付二维码…' : '支付信息准备中…'}</span>
      </div>
    );
  }

  const image = <img src={source} alt={label} width="320" height="320" />;
  const directUrl = value ? openablePaymentUrl(value) : null;
  return directUrl ? (
    <a href={directUrl} target="_blank" rel="noreferrer" aria-label={`${label}，在新窗口打开支付链接`}>
      {image}
    </a>
  ) : image;
}
