import { Link, useLocation } from 'react-router-dom';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Icon } from '../shared/ui/Icon';

const featureByPath: Record<string, { title: string; description: string; phase: string }> = {
  '/products': { title: '全部商品', description: '商品筛选、分页和详情将在下一阶段连接 Catalog 与 Inventory。', phase: '商品体验' },
  '/cart': { title: '购物袋', description: '购物车数量、优惠码与价格确认将在下一阶段连接 Basket。', phase: '购物车' },
};

export default function FutureFeaturePage() {
  const { pathname } = useLocation();
  const feature = featureByPath[pathname] ?? featureByPath['/products'];

  return (
    <CommerceShell>
      <section className="future-page">
        <Icon name="clipboard" />
        <span>{feature?.phase}</span>
        <h1>{feature?.title}</h1>
        <p>{feature?.description}</p>
        <Link className="button button--primary button--regular" to="/">返回首页</Link>
      </section>
    </CommerceShell>
  );
}
