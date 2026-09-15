import { Link } from 'react-router-dom';
import { useSession } from '../session';
import { Icon, PageHeader, type IconName } from '../ui';

const modules: Array<{ bases: string[]; label: string; path: string; icon: IconName; note: string }> = [
  { bases: ['order.fulfillment'], label: '订单履约', path: '/orders', icon: 'order', note: '发货与签收状态推进' },
  { bases: ['catalog.categories', 'catalog.products', 'catalog.promotions', 'catalog.coupons'], label: '商品目录', path: '/catalog', icon: 'catalog', note: '商品、分类、促销与券' },
  { bases: ['inventory.stock', 'inventory.allocations'], label: '库存运营', path: '/inventory', icon: 'inventory', note: '库存流水与活动配额' },
  { bases: ['identity.users', 'user.loyalty-points'], label: '用户运营', path: '/customers', icon: 'user', note: '账号状态与会员积分' },
  { bases: ['payment.transactions'], label: '支付交易', path: '/payments', icon: 'payment', note: '按订单追踪支付状态' },
  { bases: ['notification.messages'], label: '消息通知', path: '/notifications', icon: 'notification', note: '用户触达记录审阅' },
  { bases: ['system.authorization'], label: '权限治理', path: '/access', icon: 'shield', note: '用户、角色与策略' },
];

const actionLabels = [
  ['query', '查询'],
  ['create', '创建'],
  ['edit', '编辑'],
  ['delete', '删除'],
] as const;

export default function DashboardPage() {
  const { session, permissions } = useSession();
  const available = modules.filter((module) => module.bases.some((base) => permissions.some((code) => code.startsWith(`${base}.`))));
  const writes = permissions.filter((code) => /\.(create|edit|delete)$/.test(code)).length;
  const modulesByRoot = new Set(permissions.map((code) => code.split('.').slice(0, -1).join('.'))).size;

  return <div className="page page--dashboard">
    <PageHeader eyebrow="OPERATIONS CONTROL" title={`早上好，${session.name ?? '运营同事'}`} description="权限、服务与操作入口都已按当前 BM 身份实时收敛。" />

    <section className="dashboard-hero">
      <div>
        <p>今天的操作边界</p>
        <strong>{available.length}<small> 个工作域可用</small></strong>
        <span>由 BMDb 的 PBAC 策略计算，与 Identity 角色解耦。</span>
      </div>
      <div className="hero-orbit" aria-hidden="true"><span /><span /><span><Icon name="shield" /></span></div>
      <blockquote>“先确认权限，再执行动作。”<small>MSA OPERATIONS PRINCIPLE</small></blockquote>
    </section>

    <section className="metric-strip" aria-label="权限摘要">
      <article><span>有效权限</span><strong>{permissions.length}</strong><small>MODULE / ACTION</small></article>
      <article><span>覆盖模块</span><strong>{modulesByRoot}</strong><small>PBAC MODULES</small></article>
      <article><span>写入动作</span><strong>{writes}</strong><small>CREATE · EDIT · DELETE</small></article>
      <article className="metric-strip__identity"><span>当前 BM 身份</span><strong>{session.subject?.slice(0, 8) ?? '—'}</strong><small title={session.subject ?? ''}>{session.subject ?? 'Identity subject unavailable'}</small></article>
    </section>
    <p className="capability-note dashboard-capability"><Icon name="alert" />业务聚合统计接口尚未提供，概览只展示真实的 BM 权限与请求链路，不填充演示经营数据。</p>

    <div className="dashboard-grid">
      <section className="panel panel--operations">
        <div className="section-heading"><div><p className="eyebrow">AVAILABLE WORKSPACES</p><h2>运营入口</h2></div><span>{available.length} / {modules.length}</span></div>
        <div className="operation-list">{available.map((module, index) => <Link to={module.path} key={module.path}>
          <span className="operation-list__index">{String(index + 1).padStart(2, '0')}</span><Icon name={module.icon} /><span><strong>{module.label}</strong><small>{module.note}</small></span><Icon name="arrow" />
        </Link>)}</div>
      </section>

      <section className="panel panel--security">
        <div className="section-heading"><div><p className="eyebrow">SECURITY POSTURE</p><h2>权限与安全</h2></div><span className="live-label"><i />系统正常</span></div>
        <div className="security-score"><span><Icon name="shield" /></span><div><strong>最小权限已生效</strong><p>BFF 在 YARP 透传前，按 HTTP 方法和路径匹配 BM 策略。</p></div></div>
        <dl><div><dt>决策来源</dt><dd>BMDb</dd></div><div><dt>默认行为</dt><dd>拒绝</dd></div><div><dt>令牌位置</dt><dd>BFF 服务端</dd></div></dl>
        {permissions.some((code) => code.startsWith('system.authorization.')) ? <Link className="text-link" to="/access">查看策略控制台 <Icon name="arrow" /></Link> : null}
      </section>

      <section className="panel panel--matrix">
        <div className="section-heading"><div><p className="eyebrow">ACCESS MATRIX</p><h2>模块权限矩阵</h2></div><span>实时</span></div>
        <div className="permission-matrix"><div className="permission-matrix__head"><span>业务模块</span>{actionLabels.map(([, label]) => <span key={label}>{label}</span>)}</div>
          {modules.map((module) => <div className="permission-matrix__row" key={module.path}><span><Icon name={module.icon} />{module.label}</span>{actionLabels.map(([action, label]) => {
            const allowed = module.bases.some((base) => permissions.includes(`${base}.${action}`));
            return <span key={action} aria-label={`${module.label}${label}`} className={allowed ? 'is-allowed' : ''}>{allowed ? <Icon name="check" /> : '—'}</span>;
          })}</div>)}
        </div>
      </section>

      <section className="panel panel--pulse">
        <div className="section-heading"><div><p className="eyebrow">SYSTEM PULSE</p><h2>请求链路</h2></div><Icon name="activity" /></div>
        <ol className="flow-list"><li><span>01</span><div><strong>浏览器会话</strong><small>HttpOnly Cookie</small></div></li><li><span>02</span><div><strong>PBAC 决策</strong><small>Method + Path</small></div></li><li><span>03</span><div><strong>YARP 透传</strong><small>Bearer Token</small></div></li><li><span>04</span><div><strong>业务服务</strong><small>API Gateway</small></div></li></ol>
      </section>
    </div>
  </div>;
}
