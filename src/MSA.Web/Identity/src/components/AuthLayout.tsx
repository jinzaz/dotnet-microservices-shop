import type { ReactNode } from 'react';
import { storeUrl } from '../config';
import { Icon, type IconName } from './Icon';

const servicePromises: ReadonlyArray<{ icon: IconName; title: string; detail: string }> = [
  { icon: 'shield', title: '精选品质', detail: '严格筛选，长期主义' },
  { icon: 'truck', title: '便捷配送', detail: '多仓发货，快速送达' },
  { icon: 'refresh', title: '安心售后', detail: '7 天无理由退换' },
  { icon: 'headset', title: '贴心客服', detail: '在线解答，及时响应' },
];

const footerGroups = [
  { title: '购物指南', links: ['购物流程', '支付方式', '配送说明', '售后服务'] },
  { title: '关于我们', links: ['品牌故事', '联系我们', '加入我们'] },
  { title: '帮助中心', links: ['常见问题', '账户相关', '订单相关'] },
] as const;

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="login-page">
      <header className="site-header site-header--minimal">
        <div className="site-header__inner">
          <a className="brand" href={storeUrl} aria-label="返回 MSA Store 首页">MSA Store</a>
          <a className="back-link" href={storeUrl}>
            <Icon name="arrow-right" />
            返回首页
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
            <a className="brand brand--inverse" href={storeUrl}>MSA Store</a>
            <p>把日常，选得更好。</p>
            <p>从质感家居到随身好物，认真挑选每一件值得长期使用的东西。</p>
          </div>
          {footerGroups.map((group) => (
            <div className="footer-group" key={group.title}>
              <strong>{group.title}</strong>
              {group.links.map((label) => <span key={label}>{label}</span>)}
            </div>
          ))}
          <div className="footer-group footer-group--social">
            <strong>关注我们</strong>
            <span>微信 · 微博 · 小红书</span>
            <small>© {new Date().getFullYear()} MSA Store</small>
          </div>
        </div>
      </footer>
    </div>
  );
}
