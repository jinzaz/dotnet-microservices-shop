import { useEffect, useState, type FormEvent } from 'react';
import { getAntiforgeryToken } from '../auth-api';
import { AuthLayout } from '../components/AuthLayout';
import { Icon } from '../components/Icon';
import { getConsentRequest } from '../return-url';

const domainNames: Record<string, string> = {
  user: '用户与会员',
  catalog: '商品与营销目录',
  inventory: '库存与秒杀',
  notification: '通知消息',
  basket: '购物车',
  order: '订单与履约',
  payment: '支付交易',
};

function scopeDescription(scope: string) {
  if (scope === 'openid') return ['确认您的身份', '让应用识别当前登录用户'];
  if (scope === 'profile') return ['读取基础资料', '包括显示名称和用户名'];
  if (scope === 'email') return ['读取邮箱地址', '用于显示当前操作账号'];
  if (scope === 'roles') return ['读取 Identity 角色', '仅提供统一身份信息，不授予 BM 业务权限'];
  if (scope === 'offline_access') return ['保持后台会话', '允许 BFF 在会话期内安全续期'];

  const [domain, , action] = scope.split('.');
  const name = domainNames[domain ?? ''] ?? domain ?? scope;
  return action === 'write'
    ? [`管理${name}`, '可代表您创建或修改相关业务数据']
    : [`查看${name}`, '可读取相关后台业务数据'];
}

export default function ConsentPage() {
  const request = getConsentRequest(window.location.search);
  const [token, setToken] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    void getAntiforgeryToken().then(setToken).catch((reason: unknown) =>
      setError(reason instanceof Error ? reason.message : '无法建立安全授权会话。'));
  }, []);

  const submit = (event: FormEvent<HTMLFormElement>) => {
    if (!token) {
      event.preventDefault();
      setError('安全令牌尚未准备完成，请稍后重试。');
      return;
    }
  };

  return (
    <AuthLayout>
      <section className="consent-form">
        <div className="consent-form__mark"><Icon name="shield" /></div>
        <div className="login-form__heading">
          <p className="consent-form__eyebrow">授权确认</p>
          <h1>允许此应用访问？</h1>
          <p><strong>{request?.clientId ?? '未知应用'}</strong> 希望以您的身份访问以下信息与能力。</p>
        </div>

        {!request ? <p className="form-message form-message--error" role="alert">授权请求无效或已经过期，请返回应用重新发起登录。</p> : <>
          <div className="consent-client"><span>{request.clientId.slice(0, 1).toUpperCase()}</span><div><strong>{request.clientId}</strong><small>已注册 OpenID Connect 客户端</small></div><em>正在请求访问</em></div>
          <ul className="consent-scopes">{request.scopes.map((scope) => {
            const [title, detail] = scopeDescription(scope);
            return <li key={scope}><span><Icon name="check" /></span><div><strong>{title}</strong><small>{detail}</small></div><code>{scope}</code></li>;
          })}</ul>
          <p className="consent-warning"><Icon name="shield" />管理写入范围可能改变业务数据；最终是否放行仍由 BM 权限表按当前 UserId 判定。</p>
          {error ? <p className="form-message form-message--error" role="alert">{error}</p> : null}
          <form className="consent-actions" method="post" action={request.action} onSubmit={submit}>
            {request.parameters.map(([name, value], index) => (
              <input key={`${name}-${index}`} type="hidden" name={name} value={value} />
            ))}
            <input type="hidden" name="__RequestVerificationToken" value={token} />
            <button className="button consent-actions__deny" type="submit" name="consent" value="deny" disabled={!token}>拒绝</button>
            <button className="button consent-actions__approve" type="submit" name="consent" value="approve" disabled={!token}>允许并继续</button>
          </form>
        </>}
        <p className="login-trust"><Icon name="shield" />Authorization Code + PKCE · 授权决定由 IdentityService 处理</p>
      </section>
    </AuthLayout>
  );
}
