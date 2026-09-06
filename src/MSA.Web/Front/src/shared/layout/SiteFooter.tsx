import { Link } from 'react-router-dom';

const footerGroups = [
  { title: '购物指南', links: ['购物流程', '支付方式', '配送说明', '售后服务'] },
  { title: '关于我们', links: ['品牌故事', '联系我们', '加入我们'] },
  { title: '帮助中心', links: ['常见问题', '账户相关', '订单相关'] },
] as const;

export function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="site-footer__inner">
        <div className="site-footer__brand">
          <Link className="brand brand--inverse" to="/">MSA Store</Link>
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
  );
}
