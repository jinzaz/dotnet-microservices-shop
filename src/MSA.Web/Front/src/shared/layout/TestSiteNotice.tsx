import { useState } from 'react';
import { Icon } from '../ui/Icon';

const STORAGE_KEY = 'msa:test-site-notice-dismissed';

// 关闭只在当前标签页会话内有效：重新打开网站还会再提示一次。
function wasDismissed() {
  try {
    return sessionStorage.getItem(STORAGE_KEY) === '1';
  } catch {
    return false;
  }
}

export function TestSiteNotice() {
  const [visible, setVisible] = useState(() => !wasDismissed());
  if (!visible) return null;

  const dismiss = () => {
    setVisible(false);
    try {
      sessionStorage.setItem(STORAGE_KEY, '1');
    } catch {
      // 存不了就只关这一次。
    }
  };

  return (
    <div className="test-site-notice" role="alert">
      <Icon name="alert-circle" />
      <span>当前网站为测试网站，请勿支付购买商品，后果自负</span>
      <button type="button" className="test-site-notice-close" onClick={dismiss} aria-label="关闭提示">
        <Icon name="close" />
      </button>
    </div>
  );
}
