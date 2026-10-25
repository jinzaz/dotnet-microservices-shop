import { useEffect, useState, type FormEvent } from 'react';
import { getErrorMessage } from '../../shared/api/api-error';
import { Icon } from '../../shared/ui/Icon';
import type { SmsCodeRequested } from './profile-api';
import type { UserProfile } from './types';

type ProfileSectionProps = {
  profile: UserProfile;
  onEdit: () => void;
  onRequestPhoneCode: () => Promise<SmsCodeRequested>;
  onConfirmPhone: (code: string) => Promise<void>;
};

export function ProfileSection({
  profile,
  onEdit,
  onRequestPhoneCode,
  onConfirmPhone,
}: ProfileSectionProps) {
  const [confirming, setConfirming] = useState(false);
  const [code, setCode] = useState('');
  const [pending, setPending] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    setConfirming(false);
    setCode('');
    setMessage('');
    setError('');
  }, [profile.phoneNumber]);

  const requestCode = async () => {
    setPending(true);
    setError('');
    try {
      const result = await onRequestPhoneCode();
      setConfirming(true);
      setMessage(`验证码已发送，${Math.ceil(result.expiresInSeconds / 60)} 分钟内有效。`);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setPending(false);
    }
  };

  const confirm = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!/^[0-9]{6}$/.test(code)) {
      setError('请输入 6 位数字验证码。');
      return;
    }
    setPending(true);
    setError('');
    try {
      await onConfirmPhone(code);
      setMessage('手机号验证完成。');
      setConfirming(false);
      setCode('');
    } catch (confirmationError) {
      setError(getErrorMessage(confirmationError));
    } finally {
      setPending(false);
    }
  };

  return (
    <section className="account-section profile-section" id="profile">
      <div className="account-section__heading">
        <h2>个人资料</h2>
        <button className="outline-action" type="button" onClick={onEdit}>
          <Icon name="edit" />
          编辑资料
        </button>
      </div>
      <dl className="profile-list">
        <div><dt>姓名</dt><dd>{profile.displayName}</dd></div>
        <div><dt>邮箱</dt><dd>{profile.email ?? '未绑定'}</dd></div>
        <div>
          <dt>手机号</dt>
          <dd>
            {profile.phoneNumber
              ? `${profile.phoneNumber}（${profile.phoneNumberConfirmed ? '已验证' : '未验证'}）`
              : '未填写'}
          </dd>
        </div>
        {profile.phoneNumber && !profile.phoneNumberConfirmed ? <div className="phone-confirmation-row">
          <dt>手机号验证</dt>
          <dd>
            {confirming ? <form className="phone-confirmation" onSubmit={confirm}>
              <input value={code} onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))} inputMode="numeric" autoComplete="one-time-code" maxLength={6} aria-label="短信验证码" placeholder="6 位验证码" />
              <button className="outline-action outline-action--accent" type="submit" disabled={pending}>确认</button>
              <button className="outline-action" type="button" disabled={pending} onClick={() => void requestCode()}>重发</button>
            </form> : <button className="outline-action outline-action--accent" type="button" disabled={pending} onClick={() => void requestCode()}>{pending ? '发送中…' : '发送验证码'}</button>}
            {message ? <small className="phone-confirmation__message">{message}</small> : null}
            {error ? <small className="phone-confirmation__error" role="alert">{error}</small> : null}
          </dd>
        </div> : null}
      </dl>
    </section>
  );
}
