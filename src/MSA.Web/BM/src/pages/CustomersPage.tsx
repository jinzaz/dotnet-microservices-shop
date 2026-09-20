import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, idempotencyKey, query } from '../api';
import { UserPicker } from '../pickers';
import { useSession } from '../session';
import { CopyButton, Empty, ErrorNotice, Field, formatDate, formatRelative, Icon, Loading, Modal, PageHeader, Pagination, SearchInput, SkeletonRows, Status, SubmitBar, useConfirm, usePage, useToast, useUrlState } from '../ui';

type IdentityUser = { id: string; email: string; displayName: string; isActive: boolean; createdOnUtc: string };
type IdentityUserDetail = IdentityUser & { phoneNumber?: string; phoneNumberConfirmed: boolean; emailConfirmed: boolean; lastLoginOnUtc?: string; roles: string[] };
type PointAccount = { userId: string; balance: number; lifetimeEarned: number; lifetimeSpent: number; createdOnUtc?: string; updatedOnUtc?: string };
type PointEntry = { id: string; delta: number; balanceBefore: number; balanceAfter: number; source: string; reason: string; occurredOnUtc: string };

const PAGE_SIZE = 25;
const ENTRY_PAGE_SIZE = 20;

export default function CustomersPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const [tab, setTab] = useUrlState('tab', can('identity.users.query') ? 'accounts' : 'points');
  const [search, setSearch] = useUrlState('q');
  const [selectedUserId, setSelectedUserId] = useUrlState('user', '', { resetPage: false });
  const [pointsUserId, setPointsUserId] = useUrlState('points');
  const [page, setPage] = usePage();
  const [roleUser, setRoleUser] = useState<IdentityUser | null>(null);
  const [adjustOpen, setAdjustOpen] = useState(false);
  const canEditUsers = can('identity.users.edit');

  const usersQuery = useQuery({
    queryKey: ['identity', 'admin-users', search, page],
    queryFn: ({ signal }) => api<IdentityUser[]>(`/api/Auth/users/admin?${query({ search, skip: (page - 1) * PAGE_SIZE, take: PAGE_SIZE + 1 })}`, { signal }),
    enabled: tab === 'accounts' && can('identity.users.query'),
    placeholderData: keepPreviousData,
  });
  const rolesQuery = useQuery({ queryKey: ['identity', 'roles'], queryFn: () => api<string[]>('/api/Auth/users/admin/roles'), enabled: can('identity.users.query'), staleTime: 300_000 });
  const detailQuery = useQuery({ queryKey: ['identity', 'user', selectedUserId], queryFn: () => api<IdentityUserDetail>(`/api/Auth/users/admin/${encodeURIComponent(selectedUserId)}`), enabled: Boolean(selectedUserId) && can('identity.users.query') });
  const pointsQuery = useQuery({ queryKey: ['loyalty', 'account', pointsUserId], queryFn: () => api<PointAccount>(`/api/user/admin/points/${encodeURIComponent(pointsUserId)}`), enabled: Boolean(pointsUserId) && can('user.loyalty-points.query') });
  const [entryPage, setEntryPage] = useState(1);
  const entriesQuery = useQuery({
    queryKey: ['loyalty', 'entries', pointsUserId, entryPage],
    queryFn: () => api<PointEntry[]>(`/api/user/admin/points/${encodeURIComponent(pointsUserId)}/entries?${query({ skip: (entryPage - 1) * ENTRY_PAGE_SIZE, take: ENTRY_PAGE_SIZE + 1 })}`),
    enabled: Boolean(pointsUserId) && can('user.loyalty-points.query'),
    placeholderData: keepPreviousData,
  });

  const userMutation = useMutation({
    mutationFn: ({ id, path, body, method = 'POST' }: { id: string; path: string; body?: unknown; method?: 'POST' | 'DELETE'; success: string }) => api<void>(`/api/Auth/users/admin/${encodeURIComponent(id)}/${path}`, { method, body }),
    onSuccess: (_, { success }) => { toast.success(success); setRoleUser(null); void client.invalidateQueries({ queryKey: ['identity'] }); },
    onError: (error) => toast.error(error),
  });
  const pointsMutation = useMutation({
    mutationFn: (body: { delta: number }) => api<PointEntry>(`/api/user/admin/points/${encodeURIComponent(pointsUserId)}/adjustments`, { method: 'POST', body }),
    onSuccess: (entry) => { toast.success(`积分已${entry.delta >= 0 ? '增加' : '扣减'} ${Math.abs(entry.delta)}，当前余额 ${entry.balanceAfter}`); setAdjustOpen(false); setEntryPage(1); void client.invalidateQueries({ queryKey: ['loyalty'] }); },
  });

  const toggleActivation = async (user: IdentityUser) => {
    const name = user.displayName || user.email;
    if (user.isActive) {
      const result = await confirm({ title: `停用账号「${name}」？`, detail: '停用后该用户无法登录商城和 BM，已签发的会话会在令牌过期后失效。', confirmLabel: '停用账号', tone: 'danger' });
      if (!result) return;
    }
    userMutation.mutate({ id: user.id, path: 'activation', body: { isActive: !user.isActive }, success: `已${user.isActive ? '停用' : '启用'}「${name}」` });
  };
  const removeRole = async (user: IdentityUserDetail, role: string) => {
    const result = await confirm({ title: `移除角色 ${role}？`, detail: `用户：${user.displayName || user.email}`, confirmLabel: '移除', tone: 'danger' });
    if (result) userMutation.mutate({ id: user.id, path: `roles/${encodeURIComponent(role)}`, method: 'DELETE', success: `已移除角色 ${role}` });
  };
  const assignRole = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!roleUser) return;
    const role = String(new FormData(event.currentTarget).get('role') ?? '');
    userMutation.mutate({ id: roleUser.id, path: `roles/${encodeURIComponent(role)}`, success: `已为「${roleUser.displayName || roleUser.email}」添加角色 ${role}` });
  };
  const lookup = (ids: string[]) => {
    setEntryPage(1);
    setPointsUserId(ids[0] ?? '');
  };
  const openPoints = (userId: string) => {
    setSelectedUserId('');
    setEntryPage(1);
    setTab('points');
    setPointsUserId(userId);
  };

  const users = usersQuery.data?.slice(0, PAGE_SIZE) ?? [];
  const entries = entriesQuery.data?.slice(0, ENTRY_PAGE_SIZE) ?? [];

  return <div className="page">
    <PageHeader eyebrow="CUSTOMER / IDENTITY" title="用户与会员运营" description="Identity 账号状态与 UserService 会员积分分域管理，使用同一个 UserId 关联。" />
    <div className="catalog-toolbar"><div className="tabs" role="tablist">
      {can('identity.users.query') ? <button type="button" role="tab" aria-selected={tab === 'accounts'} onClick={() => setTab('accounts')}>Identity 账号</button> : null}
      {can('user.loyalty-points.query') ? <button type="button" role="tab" aria-selected={tab === 'points'} onClick={() => setTab('points')}>会员积分</button> : null}
    </div></div>

    {tab === 'accounts' ? <section className="panel table-panel">
      <div className="list-toolbar"><div className="list-toolbar__filters"><SearchInput value={search} onChange={setSearch} placeholder="邮箱、显示名称、手机号或 User ID" /></div><span className="list-toolbar__count">{rolesQuery.data?.length ?? 0} 个 Identity 角色</span></div>
      {usersQuery.isError ? <ErrorNotice error={usersQuery.error} onRetry={() => void usersQuery.refetch()} /> : null}
      {!can('identity.users.query') ? <Empty title="没有账号查询权限" detail="需要 identity.users.query 才能查看用户账号。" />
        : usersQuery.isPending ? <SkeletonRows rows={8} />
          : users.length ? <div className={`data-table${usersQuery.isPlaceholderData ? ' is-stale' : ''}`}><table><thead><tr><th>用户</th><th>User ID</th><th>账号状态</th><th>注册时间</th><th>操作</th></tr></thead><tbody>{users.map((user) => <tr key={user.id} className="is-clickable" onClick={() => setSelectedUserId(user.id)}>
            <td><div className="avatar-cell"><span>{(user.displayName || user.email || '?').slice(0, 1).toUpperCase()}</span><div><strong>{user.displayName || '未命名用户'}</strong><small>{user.email}</small></div></div></td>
            <td><span className="cell-with-copy mono">{user.id.slice(0, 13)}…<CopyButton value={user.id} label="复制 User ID" /></span></td>
            <td><Status>{user.isActive ? '启用' : '停用'}</Status></td>
            <td title={formatDate(user.createdOnUtc)}>{formatRelative(user.createdOnUtc)}</td>
            <td onClick={(event) => event.stopPropagation()}><div className="row-actions">
              <button type="button" onClick={() => setSelectedUserId(user.id)}>详情</button>
              {canEditUsers ? <><button type="button" className={user.isActive ? 'danger-link' : undefined} onClick={() => void toggleActivation(user)}>{user.isActive ? '停用' : '启用'}</button><button type="button" onClick={() => setRoleUser(user)}>添加角色</button></> : null}
            </div></td>
          </tr>)}</tbody></table></div> : <Empty title="暂无账号数据" detail={search ? '没有匹配的用户，换个关键词试试。' : 'IdentityService 没有返回用户。'} />}
      <Pagination page={page} pageSize={PAGE_SIZE} hasMore={(usersQuery.data?.length ?? 0) > PAGE_SIZE} onChange={setPage} busy={usersQuery.isFetching} />
    </section> : <>
      <section className="lookup-bar panel"><div className="lookup-bar__picker"><Field label="查询用户积分" hint={can('user.loyalty-points.query') ? '搜索并选中用户后自动查询' : '需要 user.loyalty-points.query 权限'}><UserPicker value={pointsUserId ? [pointsUserId] : []} onChange={lookup} canSearch={can('identity.users.query')} single /></Field></div>{can('user.loyalty-points.edit') && pointsQuery.data ? <button className="button button--accent" type="button" onClick={() => { pointsMutation.reset(); setAdjustOpen(true); }}><Icon name="plus" />积分调整</button> : <span>USER SERVICE</span>}</section>
      {pointsQuery.isError ? <ErrorNotice error={pointsQuery.error} /> : null}
      {pointsQuery.data ? <section className="points-overview"><article><span>当前余额</span><strong>{pointsQuery.data.balance.toLocaleString('zh-CN')}</strong><small>AVAILABLE POINTS</small></article><article><span>累计获得</span><strong>{pointsQuery.data.lifetimeEarned.toLocaleString('zh-CN')}</strong><small>LIFETIME EARNED</small></article><article><span>累计消耗</span><strong>{pointsQuery.data.lifetimeSpent.toLocaleString('zh-CN')}</strong><small>LIFETIME SPENT</small></article><article><span>最近更新</span><strong className="points-overview__date">{formatDate(pointsQuery.data.updatedOnUtc)}</strong><small>LAST UPDATED</small></article></section> : null}
      <section className="panel table-panel"><div className="section-heading"><div><p className="eyebrow">POINTS LEDGER</p><h2>积分流水</h2></div><span>{pointsUserId ? `用户 ${pointsUserId.slice(0, 8)}…` : ''}</span></div>
        {!pointsUserId ? <Empty title="等待用户查询" detail="搜索并选择用户，或从 Identity 账号详情中点击「查看积分」。" />
          : entriesQuery.isPending ? <SkeletonRows rows={5} />
            : entries.length ? <div className="data-table"><table><thead><tr><th>发生时间</th><th>变化</th><th>变化前</th><th>变化后</th><th>来源</th><th>原因</th></tr></thead><tbody>{entries.map((entry) => <tr key={entry.id}><td title={formatDate(entry.occurredOnUtc)}>{formatRelative(entry.occurredOnUtc)}<small>{formatDate(entry.occurredOnUtc)}</small></td><td className={entry.delta >= 0 ? 'number-good' : 'number-bad'}><strong>{entry.delta > 0 ? '+' : ''}{entry.delta}</strong></td><td>{entry.balanceBefore}</td><td>{entry.balanceAfter}</td><td><span className="tag">{entry.source}</span></td><td className="cell-wrap">{entry.reason}</td></tr>)}</tbody></table></div>
              : <Empty title="暂无积分流水" detail="该用户还没有积分变动。" />}
        <Pagination page={entryPage} pageSize={ENTRY_PAGE_SIZE} hasMore={(entriesQuery.data?.length ?? 0) > ENTRY_PAGE_SIZE} onChange={setEntryPage} busy={entriesQuery.isFetching} />
      </section>
    </>}

    <Modal open={Boolean(selectedUserId)} title={detailQuery.data?.displayName || detailQuery.data?.email || '用户详情'} description="Identity 账号与角色" onClose={() => setSelectedUserId('')}>
      {detailQuery.isPending ? <Loading label="正在读取账号" /> : null}
      {detailQuery.isError ? <div className="modal-form"><ErrorNotice error={detailQuery.error} /></div> : null}
      {detailQuery.data ? <div className="order-detail">
        <div className="order-detail__summary">
          <div><span>账号状态</span><p><Status>{detailQuery.data.isActive ? '启用' : '停用'}</Status></p><small>注册于 {formatDate(detailQuery.data.createdOnUtc)}</small></div>
          <div><span>最近登录</span><strong className="summary-date">{formatRelative(detailQuery.data.lastLoginOnUtc)}</strong><small>{formatDate(detailQuery.data.lastLoginOnUtc)}</small></div>
        </div>
        <dl className="detail-list">
          <div><dt>User ID</dt><dd className="mono">{detailQuery.data.id}<CopyButton value={detailQuery.data.id} label="复制 User ID" /></dd></div>
          <div><dt>邮箱</dt><dd>{detailQuery.data.email} {detailQuery.data.emailConfirmed ? <Status>已验证</Status> : <small>未验证</small>}</dd></div>
          <div><dt>手机号</dt><dd>{detailQuery.data.phoneNumber ?? '—'} {detailQuery.data.phoneNumber ? detailQuery.data.phoneNumberConfirmed ? <Status>已验证</Status> : <small>未验证</small> : null}</dd></div>
        </dl>
        <section><h3>Identity 角色</h3><div className="tag-list tag-list--actions">{detailQuery.data.roles.map((role) => <span key={role}>{role}{canEditUsers ? <button type="button" aria-label={`移除角色 ${role}`} onClick={() => void removeRole(detailQuery.data!, role)}><Icon name="close" /></button> : null}</span>)}{!detailQuery.data.roles.length ? <small>未分配角色</small> : null}{canEditUsers ? <button className="tag-add" type="button" onClick={() => setRoleUser(detailQuery.data!)}><Icon name="plus" />添加</button> : null}</div></section>
        <div className="detail-actions">
          {can('user.loyalty-points.query') ? <button className="button button--quiet" type="button" onClick={() => openPoints(detailQuery.data!.id)}><Icon name="key" />查看积分</button> : null}
          {can('order.fulfillment.query') ? <Link className="button button--quiet" to={`/orders?user=${encodeURIComponent(detailQuery.data.id)}`}><Icon name="order" />查看订单</Link> : null}
          {can('notification.messages.create') ? <Link className="button button--quiet" to={`/notifications?compose=${encodeURIComponent(detailQuery.data.id)}`}><Icon name="notification" />发送通知</Link> : null}
          {canEditUsers ? <button className={`button ${detailQuery.data.isActive ? 'button--danger' : 'button--dark'}`} type="button" onClick={() => void toggleActivation(detailQuery.data!)}>{detailQuery.data.isActive ? '停用账号' : '启用账号'}</button> : null}
        </div>
      </div> : null}
    </Modal>

    <Modal open={roleUser !== null} title="添加 Identity 角色" description={roleUser ? roleUser.displayName || roleUser.email : undefined} onClose={() => setRoleUser(null)}>
      {roleUser ? <form className="modal-form" onSubmit={assignRole}>
        <fieldset className="choice-grid choice-grid--columns"><legend>选择角色</legend>{(rolesQuery.data ?? []).map((role, index) => <label key={role}><input type="radio" name="role" value={role} required defaultChecked={index === 0} /><span><strong>{role}</strong></span></label>)}</fieldset>
        {!rolesQuery.data?.length ? <p className="muted-copy">IdentityService 没有返回可分配角色。</p> : null}
        <SubmitBar busy={userMutation.isPending} label="添加角色" onCancel={() => setRoleUser(null)} />
      </form> : null}
    </Modal>

    <Modal open={adjustOpen} title="调整会员积分" description={pointsQuery.data ? `当前余额 ${pointsQuery.data.balance.toLocaleString('zh-CN')}` : undefined} onClose={() => setAdjustOpen(false)}>
      <PointsAdjustForm balance={pointsQuery.data?.balance ?? 0} busy={pointsMutation.isPending} error={pointsMutation.error} onCancel={() => setAdjustOpen(false)} onSubmit={(body) => pointsMutation.mutate(body)} />
    </Modal>
  </div>;
}

function PointsAdjustForm({ balance, busy, error, onSubmit, onCancel }: { balance: number; busy: boolean; error: unknown; onSubmit: (body: { delta: number; businessKey: string; source: string; reason: string }) => void; onCancel: () => void }) {
  const [delta, setDelta] = useState('');
  const [businessKey] = useState(idempotencyKey);
  const next = balance + (Number(delta) || 0);
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    onSubmit({ delta: Number(delta), businessKey, source: String(data.get('source') ?? '').trim(), reason: String(data.get('reason') ?? '').trim() });
  };
  return <form className="modal-form" onSubmit={submit}>
    <Field label="积分变化" hint="正数增加，负数扣减"><input type="number" required min={-balance} max={1_000_000_000} step="1" value={delta} onChange={(event) => setDelta(event.target.value)} autoFocus /></Field>
    {delta ? <div className={`balance-preview${next < 0 ? ' is-invalid' : ''}`}><span><small>当前余额</small><strong>{balance.toLocaleString('zh-CN')}</strong></span><Icon name="arrow" /><span><small>调整后余额</small><strong>{next.toLocaleString('zh-CN')}</strong></span></div> : null}
    <Field label="来源"><select name="source" defaultValue="BM_MANUAL"><option value="BM_MANUAL">BM_MANUAL · 人工调整</option><option value="BM_COMPENSATION">BM_COMPENSATION · 售后补偿</option><option value="BM_CAMPAIGN">BM_CAMPAIGN · 活动奖励</option><option value="BM_CORRECTION">BM_CORRECTION · 差错更正</option></select></Field>
    <Field label="调整原因" hint="将记录在积分流水中，用户可见"><textarea name="reason" required maxLength={500} rows={3} /></Field>
    <p className="muted-copy">业务幂等键 <span className="mono">{businessKey}</span> 保证重复提交不会重复入账。</p>
    {error ? <ErrorNotice error={error} /> : null}
    <SubmitBar busy={busy} onCancel={onCancel} />
  </form>;
}
