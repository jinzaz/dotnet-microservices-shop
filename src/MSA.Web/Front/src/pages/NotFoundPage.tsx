import { Link } from 'react-router-dom';
import { CommerceShell } from '../shared/layout/CommerceShell';

export default function NotFoundPage() {
  return (
    <CommerceShell>
      <section className="future-page">
        <span>404</span>
        <h1>页面没有找到</h1>
        <p>这个地址可能已经改变，回到首页继续浏览吧。</p>
        <Link className="button button--primary button--regular" to="/">返回首页</Link>
      </section>
    </CommerceShell>
  );
}
