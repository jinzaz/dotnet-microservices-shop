import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, Icon, Modal, PageHeader, Status, SubmitBar } from '../ui';

type IdentityUser = { id: string; email: string; displayName: string; isActive: boolean; createdOnUtc: string };
type IdentityUserDetail = IdentityUser & { phoneNumber?: string; phoneNumberConfirmed: boolean; emailConfirmed: boolean; lastLoginOnUtc?: string; roles: string[] };
type PointAccount = { userId: string; balance: number; lifetimeEarned: number; lifetimeSpent: number; createdOnUtc?: string; updatedOnUtc?: string };
type PointEntry = { id: string; delta: number; balanceBefore: number; balanceAfter: number; source: string; reason: string; occurredOnUtc: string };

export default function CustomersPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const [tab, setTab] = useState<'accounts' | 'points'>('accounts');
  const [userId, setUserId] = useState('');
  const [search, setSearch] = useState('');
  const [selectedUserId, setSelectedUserId] = useState('');
  const [adjustOpen, setAdjustOpen] = useState(false);
  const usersQuery = useQuery({ queryKey: ['identity', 'admin-users', search], queryFn: () => api<IdentityUser[]>(`/api/Auth/users/admin?${new URLSearchParams({ skip: '0', take: '100', ...(search ? { search } : {}) })}`), enabled: can('identity.users.query') });
  const rolesQuery = useQuery({ queryKey: ['identity', 'roles'], queryFn: () => api<string[]>('/api/Auth/users/admin/roles'), enabled: can('identity.users.query') });
  const detailQuery = useQuery({ queryKey: ['identity', 'user', selectedUserId], queryFn: () => api<IdentityUserDetail>(`/api/Auth/users/admin/${encodeURIComponent(selectedUserId)}`), enabled: Boolean(selectedUserId) && can('identity.users.query') });
  const pointsQuery = useQuery({ queryKey: ['loyalty', 'account', userId], queryFn: () => api<PointAccount>(`/api/user/admin/points/${encodeURIComponent(userId)}`), enabled: Boolean(userId) && can('user.loyalty-points.query'), retry: false });
  const entriesQuery = useQuery({ queryKey: ['loyalty', 'entries', userId], queryFn: () => api<PointEntry[]>(`/api/user/admin/points/${encodeURIComponent(userId)}/entries?skip=0&take=50`), enabled: Boolean(userId) && can('user.loyalty-points.query'), retry: false });
  const userMutation = useMutation({ mutationFn: ({ id, path, body, method = 'POST' }: { id: string; path: string; body?: unknown; method?: 'POST' | 'DELETE' }) => api<void>(`/api/Auth/users/admin/${encodeURIComponent(id)}/${path}`, { method, body }), onSuccess: () => void client.invalidateQueries({ queryKey: ['identity'] }) });
  const pointsMutation = useMutation({ mutationFn: (body: unknown) => api<PointEntry>(`/api/user/admin/points/${encodeURIComponent(userId)}/adjustments`, { method: 'POST', body }), onSuccess: () => { setAdjustOpen(false); void client.invalidateQueries({ queryKey: ['loyalty'] }); } });

  const assignRole = (user: IdentityUser) => {
    const role = window.prompt(`为 ${user.displayName || user.email} 添加 Identity 角色\n可用角色：${rolesQuery.data?.join('、') ?? '加载中'}`, 'operator')?.trim();
    if (role) userMutation.mutate({ id: user.id, path: `roles/${encodeURIComponent(role)}` });
  };
  const searchUsers = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); setSearch(String(new FormData(event.currentTarget).get('search') ?? '').trim()); };
  const lookup = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); setUserId(String(new FormData(event.currentTarget).get('userId') ?? '').trim()); };
  const adjust = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); const value = (name: string) => String(data.get(name) ?? '').trim(); pointsMutation.mutate({ delta: Number(value('delta')), businessKey: value('businessKey'), source: value('source'), reason: value('reason') }); };

  return <div className="page">
    <PageHeader eyebrow="CUSTOMER / IDENTITY" title="用户与会员运营" description="Identity 账号状态与 UserService 会员积分分域管理，使用同一个 UserId 关联。" />
    <div className="catalog-toolbar"><div className="tabs"><button aria-selected={tab === 'accounts'} onClick={() => setTab('accounts')}>Identity 账号</button><button aria-selected={tab === 'points'} onClick={() => setTab('points')}>会员积分</button></div></div>
    {tab === 'accounts' ? <>
      {usersQuery.isError ? <ErrorNotice error={usersQuery.error} /> : null}{userMutation.isError ? <ErrorNotice error={userMutation.error} /> : null}
      <section className="lookup-bar panel"><form onSubmit={searchUsers}><Field label="搜索用户"><input name="search" placeholder="邮箱、显示名称、手机号或 User ID" /></Field><button className="button button--dark" disabled={!can('identity.users.query')}><Icon name="search" />搜索</button></form><span>{rolesQuery.data?.length ?? 0} 个 Identity 角色</span></section>
      <section className="panel table-panel"><div className="section-heading"><div><p className="eyebrow">IDENTITY DIRECTORY</p><h2>用户账号</h2></div><span>{usersQuery.data?.length ?? 0} 个</span></div>{usersQuery.data?.length ? <div className="data-table"><table><thead><tr><th>用户</th><th>User ID</th><th>账号状态</th><th>注册时间</th><th>操作</th></tr></thead><tbody>{usersQuery.data.map((user) => <tr key={user.id}><td><strong>{user.displayName || '未命名用户'}</strong><small>{user.email}</small></td><td className="mono">{user.id}</td><td><Status>{user.isActive ? '启用' : '停用'}</Status></td><td>{formatDate(user.createdOnUtc)}</td><td><div className="row-actions"><button onClick={() => setSelectedUserId(user.id)}>详情</button>{can('identity.users.edit') ? <><button onClick={() => userMutation.mutate({ id: user.id, path: 'activation', body: { isActive: !user.isActive } })}>{user.isActive ? '停用' : '启用'}</button><button onClick={() => assignRole(user)}>添加角色</button></> : null}</div></td></tr>)}</tbody></table></div> : <Empty title="暂无账号数据" detail={can('identity.users.query') ? 'IdentityService 没有返回匹配用户。' : '当前身份没有用户账号查询权限。'} />}</section>
      {detailQuery.isError ? <ErrorNotice error={detailQuery.error} /> : null}{detailQuery.data ? <section className="panel"><div className="section-heading"><div><p className="eyebrow">IDENTITY PROFILE</p><h2>{detailQuery.data.displayName || detailQuery.data.email}</h2></div><Status>{detailQuery.data.isActive ? '启用' : '停用'}</Status></div><dl><div><dt>User ID</dt><dd className="mono">{detailQuery.data.id}</dd></div><div><dt>手机号</dt><dd>{detailQuery.data.phoneNumber ?? '—'}</dd></div><div><dt>最近登录</dt><dd>{formatDate(detailQuery.data.lastLoginOnUtc)}</dd></div></dl><div className="row-actions">{detailQuery.data.roles.map((role) => <span key={role}><Status>{role}</Status>{can('identity.users.edit') ? <button onClick={() => userMutation.mutate({ id: detailQuery.data!.id, path: `roles/${encodeURIComponent(role)}`, method: 'DELETE' })}>移除</button> : null}</span>)}</div></section> : null}
    </> : <>
      <section className="lookup-bar panel"><form onSubmit={lookup}><Field label="Identity User ID"><input name="userId" required placeholder="输入用户标识" /></Field><button className="button button--dark" type="submit" disabled={!can('user.loyalty-points.query')}><Icon name="search" />查询积分</button></form>{can('user.loyalty-points.edit') && userId ? <button className="button button--accent" onClick={() => setAdjustOpen(true)}><Icon name="plus" />积分调整</button> : <span>USER SERVICE</span>}</section>
      {pointsQuery.isError ? <ErrorNotice error={pointsQuery.error} /> : null}{entriesQuery.isError ? <ErrorNotice error={entriesQuery.error} /> : null}
      {pointsQuery.data ? <section className="points-overview"><article><span>当前余额</span><strong>{pointsQuery.data.balance.toLocaleString('zh-CN')}</strong><small>AVAILABLE POINTS</small></article><article><span>累计获得</span><strong>{pointsQuery.data.lifetimeEarned.toLocaleString('zh-CN')}</strong><small>LIFETIME EARNED</small></article><article><span>累计消耗</span><strong>{pointsQuery.data.lifetimeSpent.toLocaleString('zh-CN')}</strong><small>LIFETIME SPENT</small></article><article><span>最近更新</span><strong className="points-overview__date">{formatDate(pointsQuery.data.updatedOnUtc)}</strong><small>LAST UPDATED</small></article></section> : null}
      <section className="panel table-panel"><div className="section-heading"><div><p className="eyebrow">POINTS LEDGER</p><h2>积分流水</h2></div><span>{entriesQuery.data?.length ?? 0} 条</span></div>{entriesQuery.data?.length ? <div className="data-table"><table><thead><tr><th>发生时间</th><th>变化</th><th>变化前</th><th>变化后</th><th>来源</th><th>原因</th></tr></thead><tbody>{entriesQuery.data.map((entry) => <tr key={entry.id}><td>{formatDate(entry.occurredOnUtc)}</td><td className={entry.delta >= 0 ? 'number-good' : 'number-bad'}><strong>{entry.delta > 0 ? '+' : ''}{entry.delta}</strong></td><td>{entry.balanceBefore}</td><td>{entry.balanceAfter}</td><td><Status>{entry.source}</Status></td><td>{entry.reason}</td></tr>)}</tbody></table></div> : <Empty title="等待用户查询" detail="查询后会显示账户余额与最近 50 条积分流水。" />}</section>
    </>}
    <Modal open={adjustOpen} title="调整会员积分" description={`当前用户：${userId}`} onClose={() => { setAdjustOpen(false); pointsMutation.reset(); }}><form className="modal-form" onSubmit={adjust}><Field label="积分变化" hint="正数增加，负数扣减"><input name="delta" required type="number" min="-1000000000" max="1000000000" /></Field><div className="form-row"><Field label="业务幂等键"><input name="businessKey" defaultValue={crypto.randomUUID()} required maxLength={128} /></Field><Field label="来源"><input name="source" defaultValue="BM_MANUAL" required maxLength={64} /></Field></div><Field label="调整原因"><textarea name="reason" required maxLength={500} rows={4} /></Field>{pointsMutation.isError ? <ErrorNotice error={pointsMutation.error} /> : null}<SubmitBar busy={pointsMutation.isPending} onCancel={() => setAdjustOpen(false)} /></form></Modal>
  </div>;
}
