import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api, idempotencyKey } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, Icon, Modal, PageHeader, Status, SubmitBar } from '../ui';

type Message = { id: string; userId: string; templateCode: string; subject: string; body: string; status: string; retryCount: number; createdOnUtc: string; nextAttemptOnUtc: string; sentOnUtc?: string; lastError?: string };
type MessagePage = { items: Message[]; skip: number; take: number; total: number };
type Template = { id: string; code: string; subject: string; body: string; isActive: boolean };

export default function NotificationsPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const [filters, setFilters] = useState({ userId: '', status: '', templateCode: '' });
  const [sendOpen, setSendOpen] = useState(false);
  const messagesQuery = useQuery({
    queryKey: ['notifications', filters],
    queryFn: () => {
      const query = new URLSearchParams({ skip: '0', take: '100' });
      if (filters.userId) query.set('userId', filters.userId);
      if (filters.status) query.set('status', filters.status);
      if (filters.templateCode) query.set('templateCode', filters.templateCode);
      return api<MessagePage>(`/api/notification/admin/messages?${query}`);
    },
    enabled: can('notification.messages.query'),
  });
  const templatesQuery = useQuery({ queryKey: ['notification-templates'], queryFn: () => api<Template[]>('/api/notification/admin/templates?active=true'), enabled: can('notification.messages.query') });
  const sendMutation = useMutation({
    mutationFn: (body: unknown) => api('/api/notification/admin/messages', { method: 'POST', body, headers: { 'Idempotency-Key': idempotencyKey() } }),
    onSuccess: () => { setSendOpen(false); void client.invalidateQueries({ queryKey: ['notifications'] }); },
  });
  const retryMutation = useMutation({
    mutationFn: (id: string) => api<void>(`/api/notification/admin/messages/${encodeURIComponent(id)}/retry`, { method: 'POST' }),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['notifications'] }),
  });

  const filter = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    setFilters({ userId: String(data.get('userId') ?? '').trim(), status: String(data.get('status') ?? ''), templateCode: String(data.get('templateCode') ?? '') });
  };
  const send = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const userIds = String(data.get('userIds') ?? '').split(/[\s,;]+/).map((value) => value.trim()).filter(Boolean);
    sendMutation.mutate({ userIds, templateCode: String(data.get('templateCode') ?? '').trim() || null, subject: String(data.get('subject') ?? '').trim(), body: String(data.get('body') ?? '').trim() });
  };

  return <div className="page">
    <PageHeader eyebrow="NOTIFICATION / DELIVERY" title="用户消息轨迹" description="审阅全局投递记录、按模板筛选，并主动触达一个或一组用户。" actions={can('notification.messages.create') ? <button className="button button--dark" type="button" onClick={() => setSendOpen(true)}><Icon name="plus" />发送通知</button> : undefined} />
    <section className="lookup-bar panel"><form onSubmit={filter}><Field label="Identity User ID"><input name="userId" placeholder="留空查询全部用户" /></Field><Field label="状态"><select name="status"><option value="">全部状态</option>{['Pending', 'Sending', 'Sent', 'Failed'].map((value) => <option key={value}>{value}</option>)}</select></Field><Field label="模板"><select name="templateCode"><option value="">全部模板</option>{templatesQuery.data?.map((template) => <option key={template.id} value={template.code}>{template.code}</option>)}</select></Field><button className="button button--dark" type="submit" disabled={!can('notification.messages.query')}><Icon name="search" />筛选</button></form><span>{messagesQuery.data?.total ?? 0} 条记录</span></section>
    {messagesQuery.isError ? <ErrorNotice error={messagesQuery.error} /> : null}{retryMutation.isError ? <ErrorNotice error={retryMutation.error} /> : null}
    {messagesQuery.data?.items.length ? <section className="panel table-panel"><div className="section-heading"><div><p className="eyebrow">MESSAGE LOG</p><h2>触达记录</h2></div><span>{messagesQuery.data.items.length} 条</span></div><div className="data-table"><table><thead><tr><th>用户 / 主题</th><th>内容摘要</th><th>模板</th><th>状态</th><th>创建时间</th><th>送达时间</th><th>操作</th></tr></thead><tbody>{messagesQuery.data.items.map((message) => <tr key={message.id}><td><strong>{message.subject}</strong><small className="mono">{message.userId}</small></td><td className="cell-wrap">{message.body}</td><td>{message.templateCode}</td><td><Status>{message.status}</Status>{message.lastError ? <small>{message.lastError}</small> : null}</td><td>{formatDate(message.createdOnUtc)}<small>重试 {message.retryCount}</small></td><td>{formatDate(message.sentOnUtc)}</td><td>{message.status === 'Failed' && can('notification.messages.edit') ? <button onClick={() => retryMutation.mutate(message.id)} disabled={retryMutation.isPending}>重试</button> : '—'}</td></tr>)}</tbody></table></div></section> : !messagesQuery.isFetching && !messagesQuery.isError ? <Empty title="暂无通知" detail={can('notification.messages.query') ? '调整筛选条件，或主动发送一条通知。' : '当前 BM 身份没有通知查询权限。'} /> : null}
    <Modal open={sendOpen} title="发送用户通知" description="多个用户 ID 可用换行、空格、逗号或分号分隔，单次最多 100 个。" onClose={() => { setSendOpen(false); sendMutation.reset(); }}><form className="modal-form" onSubmit={send}><Field label="Identity User ID"><textarea name="userIds" required rows={4} /></Field><Field label="模板标识"><select name="templateCode"><option value="">admin-message</option>{templatesQuery.data?.map((template) => <option key={template.id} value={template.code}>{template.code} · {template.subject}</option>)}</select></Field><Field label="主题"><input name="subject" required maxLength={500} /></Field><Field label="内容"><textarea name="body" required rows={6} maxLength={10000} /></Field>{sendMutation.isError ? <ErrorNotice error={sendMutation.error} /> : null}<SubmitBar busy={sendMutation.isPending} onCancel={() => setSendOpen(false)} /></form></Modal>
  </div>;
}
