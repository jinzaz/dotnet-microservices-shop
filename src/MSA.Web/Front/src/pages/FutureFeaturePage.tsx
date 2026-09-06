import { Link, useLocation } from 'react-router-dom';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Icon } from '../shared/ui/Icon';

const featureByPath: Record<string, { title: string; description: string; phase: string }> = {
  '/products': { title: '全部商品', description: '商品筛选、分页和详情将在下一阶段连接 Catalog 与 Inventory。', phase: '商品体验' },
  '/cart': { title: '购物袋', description: '购物车数量、优惠码与价格确认将在下一阶段连接 Basket。', phase: '购物车' },
  '/orders': { title: '我的订单', description: '订单列表、履约时间线与退款状态将在后续连接 Ordering 与 Payment。', phase: '订单中心' },
  '/register': { title: '创建账户', description: '注册流程将在下一阶段接入 Identity 注册接口与密码规则提示。', phase: '账户能力' },
  '/forgot-password': { title: '找回密码', description: '后端尚未提供密码找回接口，页面会在接口就绪后启用。', phase: '账户能力' },
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
