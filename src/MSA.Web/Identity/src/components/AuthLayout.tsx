import type { ReactNode } from 'react';
import { storeUrl } from '../config';
import { DottedGlobe } from './DottedGlobe';
import { Icon, type IconName } from './Icon';

const servicePromises: ReadonlyArray<{ icon: IconName; title: string; detail: string }> = [
  { icon: 'shield', title: '统一身份', detail: '一处登录，应用隔离' },
  { icon: 'lock', title: '服务端会话', detail: '业务令牌不落入浏览器' },
  { icon: 'refresh', title: '可撤销授权', detail: '短令牌与安全续期' },
  { icon: 'user', title: '最小权限', detail: '按应用展示并确认范围' },
];

const footerGroups = [
  { title: '登录安全', links: ['授权码 + PKCE', 'HttpOnly 会话', '防伪令牌'] },
  { title: '账户保护', links: ['登录锁定', '短信校验', '会话撤销'] },
  { title: '授权控制', links: ['明确确认', '范围可见', '业务权限隔离'] },
] as const;

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="login-page">
      <header className="site-header site-header--minimal">
        <div className="site-header__inner">
          <a className="brand" href={storeUrl} aria-label="返回 MSA Store 首页">
            <span className="brand__tile" aria-hidden="true">M</span>
            MSA Identity
          </a>
          <a className="back-link" href={storeUrl}>
            <Icon name="arrow-right" />
            返回商城
          </a>
        </div>
      </header>

      <main className="login-layout">
        <section className="login-media" aria-label="关于 MSA Identity">
          <DottedGlobe className="login-media__globe" />
          <div className="login-media__copy">
            <p className="eyebrow"><span>[ 统一身份 · MSA IDENTITY ]</span><span>ID 01</span></p>
            <h2 className="display">一次登录，<br />每一步都<em>可验证</em>。</h2>
            <p className="login-media__lede">
              认证与授权集中在 Identity 处理：浏览器只持有 HttpOnly 会话，业务令牌留在服务端，每一次授权范围都清楚可见。
            </p>
          </div>
          <figure className="proof-panel">
            <figcaption>
              <span>AUTHORIZATION REQUEST</span>
              <span className="status-pill"><Icon name="check" />PKCE</span>
            </figcaption>
            <pre><code>
              <span className="proof-panel__muted">GET</span> /connect/authorize{'\n'}
              {'  '}response_type         = <b>code</b>{'\n'}
              {'  '}code_challenge_method = <b>S256</b>{'\n'}
              {'  '}scope                 = openid profile{'\n'}
              <span className="proof-panel__muted">→</span> session  <b>HttpOnly</b> · SameSite=Lax
            </code></pre>
          </figure>
        </section>
        <div className="login-panel">
          <div className="login-card">{children}</div>
        </div>
      </main>

      <div className="neon-divider" aria-hidden="true" />

      <section className="service-promises" aria-label="服务承诺">
        <div className="service-promises__inner">
          {servicePromises.map((item, index) => (
            <div className="service-promise" key={item.title}>
              <span className="service-promise__index">S 0{index + 1}</span>
              <Icon name={item.icon} />
              <div>
                <strong>{item.title}</strong>
                <span>{item.detail}</span>
              </div>
            </div>
          ))}
        </div>
      </section>

      <footer className="site-footer">
        <div className="site-footer__inner">
          <div className="site-footer__brand">
            <a className="brand" href={storeUrl}>
              <span className="brand__tile" aria-hidden="true">M</span>
              MSA Identity
            </a>
            <p>统一登录，边界清晰。</p>
            <p>身份平台只负责认证与授权范围，后台业务权限由 BM 独立判定。</p>
          </div>
          {footerGroups.map((group) => (
            <div className="footer-group" key={group.title}>
              <strong>{group.title}</strong>
              {group.links.map((label) => <span key={label}>{label}</span>)}
            </div>
          ))}
          <div className="footer-group footer-group--social">
            <strong>MSA Platform</strong>
            <span>Identity · BFF · PBAC</span>
            <small>© {new Date().getFullYear()} MSA Identity</small>
          </div>
        </div>
      </footer>
    </div>
  );
}
