import type { ReactNode } from 'react';
import { storeUrl } from '../config';
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
          <a className="brand" href={storeUrl} aria-label="返回 MSA Store 首页">MSA Identity</a>
          <a className="back-link" href={storeUrl}>
            <Icon name="arrow-right" />
            返回商城
          </a>
        </div>
      </header>

      <main className="login-layout">
        <div className="login-media">
          <img
            src="/images/hero-lifestyle.jpg"
            alt="自然光下的手冲壶、陶瓷杯与绿植"
            width="1672"
            height="941"
          />
        </div>
        <div className="login-panel">{children}</div>
      </main>

      <section className="service-promises" aria-label="服务承诺">
        <div className="service-promises__inner">
          {servicePromises.map((item) => (
            <div className="service-promise" key={item.title}>
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
            <a className="brand brand--inverse" href={storeUrl}>MSA Identity</a>
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
