import { useCallback, useEffect, useState } from 'react';
import { fetchCaptcha, type Captcha } from '../auth-api';
import { FormField } from './FormField';
import { Icon } from './Icon';

/** 图形验证码状态。enabled 为 false 时不请求（例如登录页的密码模式）。 */
export function useCaptcha(enabled: boolean) {
  const [captcha, setCaptcha] = useState<Captcha | null>(null);
  const [answer, setAnswer] = useState('');
  const [loadError, setLoadError] = useState('');

  const refresh = useCallback(async () => {
    setAnswer('');
    try {
      setCaptcha(await fetchCaptcha());
      setLoadError('');
    } catch (error) {
      setCaptcha(null);
      setLoadError(error instanceof Error && error.message ? error.message : '图形验证码加载失败，点击重试。');
    }
  }, []);

  useEffect(() => {
    if (enabled) void refresh();
  }, [enabled, refresh]);

  // 过期前几秒自动换一张，免得用户答了一张已经失效的。
  useEffect(() => {
    if (!enabled || !captcha) return;
    const timer = window.setTimeout(() => void refresh(), Math.max(5, captcha.expiresInSeconds - 5) * 1_000);
    return () => window.clearTimeout(timer);
  }, [enabled, captcha, refresh]);

  return { captcha, answer, setAnswer, refresh, loadError };
}

type CaptchaFieldProps = {
  id: string;
  state: ReturnType<typeof useCaptcha>;
  error?: string;
  disabled?: boolean;
};

export function CaptchaField({ id, state, error, disabled }: CaptchaFieldProps) {
  const { captcha, answer, setAnswer, refresh, loadError } = state;

  return (
    <FormField
      id={id}
      label="图形验证码"
      value={answer}
      onChange={(event) => setAnswer(event.target.value.replace(/\D/g, '').slice(0, 3))}
      placeholder="计算结果"
      inputMode="numeric"
      autoComplete="off"
      maxLength={3}
      disabled={disabled}
      leading={<Icon name="shield" />}
      trailing={(
        <button
          className="captcha-image"
          type="button"
          onClick={() => void refresh()}
          disabled={disabled}
          aria-label="看不清，换一张"
          title="看不清，换一张"
        >
          {captcha ? <img src={captcha.image} alt="算术图形验证码" width={125} height={40} /> : <span>{loadError ? '点击重试' : '加载中…'}</span>}
        </button>
      )}
      error={error ?? (loadError || undefined)}
    />
  );
}
