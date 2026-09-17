import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState, type FormEvent } from 'react';
import { api } from '../api';
import { useSession } from '../session';
import { CopyButton, Empty, ErrorNotice, Field, formatDate, Icon, Modal, PageHeader, SearchInput, SkeletonRows, Status, SubmitBar, useConfirm, useToast, useUrlState } from '../ui';

type Role = { id: string; code: string; displayName: string; isActive: boolean; createdOnUtc: string };
type BmUser = { identityUserId: string; isActive: boolean; createdOnUtc: string; roles: Role[] };
type Permission = { id: string; code: string; module: string; action: 'Query' | 'Create' | 'Edit' | 'Delete'; displayName: string; routes: Array<{ httpMethod: string; pathTemplate: string }> };
type Policy = { id: string; name: string; effect: 'Allow' | 'Deny'; createdOnUtc: string; permissions: Array<{ id: string; code: string; displayName: string }>; userIds: string[]; roles: Role[] };
type Tab = 'users' | 'roles' | 'policies' | 'permissions';
type Editor = { kind: Exclude<Tab, 'permissions'>; item?: BmUser | Role | Policy } | null;

const tabLabels: Record<Tab, string> = { users: 'BM 用户', roles: '角色', policies: '策略', permissions: '权限目录' };

export default function AccessPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const [tabValue, setTab] = useUrlState('tab', 'users');
  const tab = (Object.keys(tabLabels).includes(tabValue) ? tabValue : 'users') as Tab;
  const [search, setSearch] = useUrlState('q');
  const toast = useToast();
  const confirm = useConfirm();
  const [editor, setEditor] = useState<Editor>(null);
  const canQuery = can('system.authorization.query');
  const usersQuery = useQuery({ queryKey: ['pbac', 'users'], queryFn: () => api<BmUser[]>('/bff/pbac/users/'), enabled: canQuery });
  const rolesQuery = useQuery({ queryKey: ['pbac', 'roles'], queryFn: () => api<Role[]>('/bff/pbac/roles/'), enabled: canQuery });
  const policiesQuery = useQuery({ queryKey: ['pbac', 'policies'], queryFn: () => api<Policy[]>('/bff/pbac/policies/'), enabled: canQuery });
  const permissionsQuery = useQuery({ queryKey: ['pbac', 'permission-catalog'], queryFn: () => api<Permission[]>('/bff/pbac/permissions'), enabled: canQuery });
  const saveMutation = useMutation({
    mutationFn: ({ path, method, body }: { path: string; method: string; body: unknown }) => api<unknown>(path, { method, body }),
    onSuccess: () => { toast.success(editor?.item ? '已保存修改' : '已创建'); setEditor(null); void client.invalidateQueries({ queryKey: ['pbac'] }); },
  });
  const deleteMutation = useMutation({
    mutationFn: (path: string) => api<void>(path, { method: 'DELETE' }),
    onSuccess: () => { toast.success('已删除'); void client.invalidateQueries({ queryKey: ['pbac'] }); },
    onError: (error) => toast.error(error),
  });

  const createKind = tab === 'permissions' ? null : tab;
  const listError = usersQuery.error ?? rolesQuery.error ?? policiesQuery.error ?? permissionsQuery.error;
  const remove = async (path: string, label: string) => {
    const result = await confirm({ title: `删除「${label}」？`, detail: '删除后相关授权立即失效，该操作不可撤销。', confirmLabel: '删除', tone: 'danger' });
    if (result) deleteMutation.mutate(path);
  };
  const term = search.trim().toLowerCase();
  const matches = (...values: Array<string | undefined>) => !term || values.some((value) => value?.toLowerCase().includes(term));
  const users = useMemo(() => (usersQuery.data ?? []).filter((item) => matches(item.identityUserId, ...item.roles.map((role) => role.displayName))), [usersQuery.data, term]);
  const roles = useMemo(() => (rolesQuery.data ?? []).filter((item) => matches(item.code, item.displayName)), [rolesQuery.data, term]);
  const policies = useMemo(() => (policiesQuery.data ?? []).filter((item) => matches(item.name, ...item.permissions.map((permission) => permission.code))), [policiesQuery.data, term]);
  const permissions = useMemo(() => (permissionsQuery.data ?? []).filter((item) => matches(item.code, item.displayName, item.module)), [permissionsQuery.data, term]);
  const loading = usersQuery.isPending || rolesQuery.isPending || policiesQuery.isPending || permissionsQuery.isPending;

  const save = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!editor) return;
    const data = new FormData(event.currentTarget);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    const isEdit = Boolean(editor.item);
    if (editor.kind === 'roles') {
      const item = editor.item as Role | undefined;
      saveMutation.mutate({ path: `/bff/pbac/roles/${isEdit ? item?.id : ''}`, method: isEdit ? 'PUT' : 'POST', body: { code: value('code'), displayName: value('displayName'), isActive: data.has('isActive') } });
    } else if (editor.kind === 'users') {
      const item = editor.item as BmUser | undefined;
      const id = isEdit ? item!.identityUserId : value('identityUserId');
      saveMutation.mutate({ path: `/bff/pbac/users/${isEdit ? encodeURIComponent(id) : ''}`, method: isEdit ? 'PUT' : 'POST', body: { ...(isEdit ? {} : { identityUserId: id }), isActive: data.has('isActive'), roleIds: data.getAll('roleIds').map(String) } });
    } else {
      const item = editor.item as Policy | undefined;
      saveMutation.mutate({ path: `/bff/pbac/policies/${isEdit ? item?.id : ''}`, method: isEdit ? 'PUT' : 'POST', body: { name: value('name'), effect: value('effect'), permissionIds: data.getAll('permissionIds').map(String), userIds: data.getAll('userIds').map(String), roleIds: data.getAll('roleIds').map(String) } });
    }
  };

  return <div className="page">
    <PageHeader eyebrow="PBAC / GOVERNANCE" title="成员与权限策略" description="BMDb 独立维护后台用户、角色、Allow / Deny 策略以及模块动作权限。" actions={createKind && can('system.authorization.create') ? <button className="button button--dark" onClick={() => setEditor({ kind: createKind })}><Icon name="plus" />新建{tabLabels[createKind]}</button> : undefined} />
    <section className="security-banner"><Icon name="shield" /><div><strong>Deny takes precedence</strong><p>显式拒绝优先于允许；没有匹配权限的路由默认拒绝。</p></div><span>BMDB POLICY ENGINE</span></section>
    <div className="catalog-toolbar"><div className="tabs" role="tablist">{(Object.keys(tabLabels) as Tab[]).map((value) => <button key={value} type="button" role="tab" aria-selected={tab === value} onClick={() => setTab(value)}>{tabLabels[value]}</button>)}</div><SearchInput value={search} onChange={setSearch} placeholder="搜索用户 ID、角色、策略或权限代码" /></div>
    {listError ? <ErrorNotice error={listError} /> : null}
    {!canQuery ? <Empty title="缺少权限查询能力" detail="需要 system.authorization.query 才能读取 BM 成员与策略目录。" /> : <section className="panel table-panel"><div className="section-heading"><div><p className="eyebrow">AUTHORIZATION DIRECTORY</p><h2>{tabLabels[tab]}</h2></div><span>{tab === 'users' ? users.length : tab === 'roles' ? roles.length : tab === 'policies' ? policies.length : permissions.length} 条</span></div>
      {loading ? <SkeletonRows rows={6} /> : null}
      {!loading && tab === 'users' ? <UsersTable rows={users} canEdit={can('system.authorization.edit')} canDelete={can('system.authorization.delete')} onEdit={(item) => setEditor({ kind: 'users', item })} onDelete={(item) => void remove(`/bff/pbac/users/${encodeURIComponent(item.identityUserId)}`, item.identityUserId)} /> : null}
      {!loading && tab === 'roles' ? <RolesTable rows={roles} canEdit={can('system.authorization.edit')} canDelete={can('system.authorization.delete')} onEdit={(item) => setEditor({ kind: 'roles', item })} onDelete={(item) => void remove(`/bff/pbac/roles/${item.id}`, item.displayName)} /> : null}
      {!loading && tab === 'policies' ? <PoliciesTable rows={policies} canEdit={can('system.authorization.edit')} canDelete={can('system.authorization.delete')} onEdit={(item) => setEditor({ kind: 'policies', item })} onDelete={(item) => void remove(`/bff/pbac/policies/${item.id}`, item.name)} /> : null}
      {!loading && tab === 'permissions' ? <PermissionsTable rows={permissions} /> : null}
    </section>}

    <Modal open={editor !== null} title={`${editor?.item ? '编辑' : '新建'}${editor ? tabLabels[editor.kind] : ''}`} description="所有变更通过 /bff/pbac 接口写入 BMDb。" onClose={() => { setEditor(null); saveMutation.reset(); }} wide={editor?.kind === 'policies'}>
      {editor ? <form key={`${editor.kind}-${editor.item ? 'edit' : 'new'}`} className="modal-form" onSubmit={save}>
        {editor.kind === 'roles' ? <RoleFields item={editor.item as Role | undefined} /> : null}
        {editor.kind === 'users' ? <UserFields item={editor.item as BmUser | undefined} roles={rolesQuery.data ?? []} /> : null}
        {editor.kind === 'policies' ? <PolicyFields item={editor.item as Policy | undefined} permissions={permissionsQuery.data ?? []} users={usersQuery.data ?? []} roles={rolesQuery.data ?? []} /> : null}
        {saveMutation.isError ? <ErrorNotice error={saveMutation.error} /> : null}<SubmitBar busy={saveMutation.isPending} onCancel={() => setEditor(null)} />
      </form> : null}
    </Modal>
  </div>;
}

function UsersTable({ rows, canEdit, canDelete, onEdit, onDelete }: { rows: BmUser[]; canEdit: boolean; canDelete: boolean; onEdit: (row: BmUser) => void; onDelete: (row: BmUser) => void }) {
  if (!rows.length) return <Empty title="还没有 BM 用户" detail="用 Identity User ID 创建后台成员，并分配 BM 角色。" />;
  return <div className="data-table"><table><thead><tr><th>Identity User ID</th><th>状态</th><th>BM 角色</th><th>加入时间</th><th>操作</th></tr></thead><tbody>{rows.map((row) => <tr key={row.identityUserId}><td><span className="cell-with-copy mono"><strong>{row.identityUserId}</strong><CopyButton value={row.identityUserId} label="复制 User ID" /></span></td><td><Status>{row.isActive ? '启用' : '停用'}</Status></td><td><div className="tag-list">{row.roles.map((role) => <span key={role.id}>{role.displayName}</span>)}{!row.roles.length ? '—' : null}</div></td><td>{formatDate(row.createdOnUtc)}</td><td><div className="row-actions">{canEdit ? <button onClick={() => onEdit(row)}>编辑</button> : null}{canDelete ? <button className="danger-link" onClick={() => onDelete(row)}>删除</button> : null}</div></td></tr>)}</tbody></table></div>;
}

function RolesTable({ rows, canEdit, canDelete, onEdit, onDelete }: { rows: Role[]; canEdit: boolean; canDelete: boolean; onEdit: (row: Role) => void; onDelete: (row: Role) => void }) {
  if (!rows.length) return <Empty title="还没有 BM 角色" detail="角色用于复用策略分配。" />;
  return <div className="data-table"><table><thead><tr><th>角色</th><th>代码</th><th>状态</th><th>创建时间</th><th>操作</th></tr></thead><tbody>{rows.map((row) => <tr key={row.id}><td><strong>{row.displayName}</strong><small>{row.id.slice(0, 8)}</small></td><td className="mono">{row.code}</td><td><Status>{row.isActive ? '启用' : '停用'}</Status></td><td>{formatDate(row.createdOnUtc)}</td><td><div className="row-actions">{canEdit ? <button onClick={() => onEdit(row)}>编辑</button> : null}{canDelete ? <button className="danger-link" onClick={() => onDelete(row)}>删除</button> : null}</div></td></tr>)}</tbody></table></div>;
}

function PoliciesTable({ rows, canEdit, canDelete, onEdit, onDelete }: { rows: Policy[]; canEdit: boolean; canDelete: boolean; onEdit: (row: Policy) => void; onDelete: (row: Policy) => void }) {
  if (!rows.length) return <Empty title="还没有 PBAC 策略" detail="策略把权限赋给用户或角色。" />;
  return <div className="data-table"><table><thead><tr><th>策略</th><th>效果</th><th>权限</th><th>作用对象</th><th>创建时间</th><th>操作</th></tr></thead><tbody>{rows.map((row) => <tr key={row.id}><td><strong>{row.name}</strong><small>{row.id.slice(0, 8)}</small></td><td><Status>{row.effect}</Status></td><td>{row.permissions.length}<small>{row.permissions.slice(0, 2).map((item) => item.code).join(' · ')}</small></td><td>{row.roles.length} 角色 / {row.userIds.length} 用户</td><td>{formatDate(row.createdOnUtc)}</td><td><div className="row-actions">{canEdit ? <button onClick={() => onEdit(row)}>编辑</button> : null}{canDelete ? <button className="danger-link" onClick={() => onDelete(row)}>删除</button> : null}</div></td></tr>)}</tbody></table></div>;
}

function PermissionsTable({ rows }: { rows: Permission[] }) {
  if (!rows.length) return <Empty title="权限目录为空" detail="固定权限种子尚未写入 BMDb。" />;
  return <div className="data-table"><table><thead><tr><th>权限</th><th>模块</th><th>动作</th><th>映射路由</th></tr></thead><tbody>{rows.map((row) => <tr key={row.id}><td><strong>{row.displayName}</strong><small className="mono">{row.code}</small></td><td>{row.module}</td><td><Status>{row.action}</Status></td><td>{row.routes.length ? row.routes.map((route) => <code key={`${route.httpMethod}-${route.pathTemplate}`}>{route.httpMethod} {route.pathTemplate}</code>) : <small>暂未映射接口</small>}</td></tr>)}</tbody></table></div>;
}

function RoleFields({ item }: { item?: Role }) {
  return <><div className="form-row"><Field label="角色代码"><input name="code" defaultValue={item?.code} required maxLength={100} /></Field><Field label="显示名称"><input name="displayName" defaultValue={item?.displayName} required maxLength={120} /></Field></div><label className="switch-row"><input name="isActive" type="checkbox" defaultChecked={item?.isActive ?? true} /><span />启用该角色</label></>;
}

function UserFields({ item, roles }: { item?: BmUser; roles: Role[] }) {
  const selected = new Set(item?.roles.map((role) => role.id));
  return <><Field label="Identity User ID" hint="仅保存关联标识，不复制 Identity 用户数据"><input name="identityUserId" defaultValue={item?.identityUserId} disabled={Boolean(item)} required maxLength={200} /></Field><label className="switch-row"><input name="isActive" type="checkbox" defaultChecked={item?.isActive ?? true} /><span />允许进入 BM</label><fieldset className="choice-grid"><legend>分配 BM 角色</legend>{roles.map((role) => <label key={role.id}><input type="checkbox" name="roleIds" value={role.id} defaultChecked={selected.has(role.id)} /><span><strong>{role.displayName}</strong><small>{role.code}</small></span></label>)}</fieldset></>;
}

function PolicyFields({ item, permissions, users, roles }: { item?: Policy; permissions: Permission[]; users: BmUser[]; roles: Role[] }) {
  const selectedPermissions = new Set(item?.permissions.map((permission) => permission.id));
  const selectedUsers = new Set(item?.userIds);
  const selectedRoles = new Set(item?.roles.map((role) => role.id));
  const groups = permissions.reduce((result, permission) => {
    const group = result.get(permission.module) ?? [];
    group.push(permission);
    result.set(permission.module, group);
    return result;
  }, new Map<string, Permission[]>());
  return <><div className="form-row"><Field label="策略名称"><input name="name" defaultValue={item?.name} required maxLength={120} /></Field><Field label="决策效果"><select name="effect" defaultValue={item?.effect ?? 'Allow'}><option value="Allow">Allow · 允许</option><option value="Deny">Deny · 拒绝优先</option></select></Field></div><fieldset className="permission-picker"><legend>选择权限（至少一项）</legend>{[...groups].map(([module, values]) => <section key={module}><strong>{module}<button type="button" className="link-button" onClick={(event) => { const boxes = [...(event.currentTarget.closest('section')?.querySelectorAll<HTMLInputElement>('input[type=checkbox]') ?? [])]; const next = boxes.some((box) => !box.checked); boxes.forEach((box) => { box.checked = next; }); }}>全选</button></strong><div>{values.map((permission) => <label key={permission.id}><input type="checkbox" name="permissionIds" value={permission.id} defaultChecked={selectedPermissions.has(permission.id)} /><span>{permission.action}</span></label>)}</div></section>)}</fieldset><div className="assignment-grid"><fieldset className="choice-grid"><legend>分配给角色</legend>{roles.map((role) => <label key={role.id}><input type="checkbox" name="roleIds" value={role.id} defaultChecked={selectedRoles.has(role.id)} /><span><strong>{role.displayName}</strong><small>{role.code}</small></span></label>)}</fieldset><fieldset className="choice-grid"><legend>直接分配给用户</legend>{users.map((user) => <label key={user.identityUserId}><input type="checkbox" name="userIds" value={user.identityUserId} defaultChecked={selectedUsers.has(user.identityUserId)} /><span><strong>{user.identityUserId}</strong><small>{user.isActive ? '启用' : '停用'}</small></span></label>)}</fieldset></div></>;
}
