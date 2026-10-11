import { useEffect, useState } from 'react';
import * as QRCode from 'qrcode';
import { Button } from '../../shared/ui/Button';

/** 支付宝网页支付（AliVibePay）返回的是待提交的 HTML 表单，而不是可扫码链接。 */
export function isPagePayForm(value: string | null | undefined): value is string {
  return Boolean(value?.trimStart().startsWith('<form'));
}

/** 只取出服务端生成的 form 并在新窗口提交，不执行其中的脚本；本页继续轮询支付结果。 */
function submitPagePayForm(html: string) {
  const form = new DOMParser().parseFromString(html, 'text/html').querySelector('form');
  if (!form) return;
  form.target = '_blank';
  form.style.display = 'none';
  document.body.appendChild(form);
  form.submit();
  form.remove();
}

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
    if (!value || isPagePayForm(value)) return () => { active = false; };

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

  if (isPagePayForm(value)) {
    return (
      <div className="payment-qr__state">
        <span>将在新窗口打开支付宝收银台，可扫码或登录账户付款</span>
        <Button type="button" onClick={() => submitPagePayForm(value)}>前往支付宝付款</Button>
      </div>
    );
  }

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
