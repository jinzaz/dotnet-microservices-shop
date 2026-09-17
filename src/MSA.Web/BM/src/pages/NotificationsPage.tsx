import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api, idempotencyKey, query, type Paged } from '../api';
import { useSession } from '../session';
import { ChipFilter, CopyButton, Empty, ErrorNotice, Field, formatDate, formatRelative, Icon, Modal, PageHeader, Pagination, SearchInput, SkeletonRows, Status, statusLabel, SubmitBar, usePage, useToast, useUrlState } from '../ui';

type Message = { id: string; userId: string; templateCode: string; subject: string; body: string; status: string; retryCount: number; createdOnUtc: string; nextAttemptOnUtc: string; sentOnUtc?: string; lastError?: string };
type Template = { id: string; code: string; subject: string; body: string; isActive: boolean };

const PAGE_SIZE = 25;
const MAX_RECIPIENTS = 100;

export default function NotificationsPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const [status, setStatus] = useUrlState('status');
  const [templateCode, setTemplateCode] = useUrlState('template');
  const [userId, setUserId] = useUrlState('user');
  const [compose, setCompose] = useUrlState('compose', '', { resetPage: false });
  const [page, setPage] = usePage();
  const [selected, setSelected] = useState<Message | null>(null);
  const [sendOpen, setSendOpen] = useState(false);
  const canQuery = can('notification.messages.query');
  const composing = sendOpen || Boolean(compose);

  const messagesQuery = useQuery({
    queryKey: ['notifications', 'list', { status, templateCode, userId, page }],
    queryFn: ({ signal }) => api<Paged<Message>>(`/api/notification/admin/messages?${query({ status, templateCode, userId, skip: (page - 1) * PAGE_SIZE, take: PAGE_SIZE })}`, { signal }),
    enabled: canQuery,
    placeholderData: keepPreviousData,
  });
  const failedQuery = useQuery({
    queryKey: ['notifications', 'failed-count'],
    queryFn: () => api<Paged<Message>>('/api/notification/admin/messages?status=Failed&take=1'),
    enabled: canQuery,
    staleTime: 30_000,
  });
  const templatesQuery = useQuery({ queryKey: ['notification-templates'], queryFn: () => api<Template[]>('/api/notification/admin/templates?active=true'), enabled: canQuery, staleTime: 300_000 });
  const retryMutation = useMutation({
    mutationFn: (message: Message) => api<void>(`/api/notification/admin/messages/${encodeURIComponent(message.id)}/retry`, { method: 'POST' }),
    onSuccess: (_, message) => { toast.success(`已重新排队：${message.subject}`); setSelected(null); void client.invalidateQueries({ queryKey: ['notifications'] }); },
    onError: (error) => toast.error(error),
  });

  const closeComposer = () => { setSendOpen(false); setCompose(''); };
  const rows = messagesQuery.data?.items ?? [];
  const failed = failedQuery.data?.total ?? 0;

  return <div className="page">
    <PageHeader eyebrow="NOTIFICATION / DELIVERY" title="用户消息轨迹" description="审阅全局投递记录，重试失败消息，并主动触达一个或一组用户。"
      actions={can('notification.messages.create') ? <button className="button button--dark" type="button" onClick={() => setSendOpen(true)}><Icon name="plus" />发送通知</button> : undefined} />

    {failed > 0 && status !== 'Failed' ? <button className="alert-banner" type="button" onClick={() => setStatus('Failed')}><Icon name="alert" /><span><strong>{failed} 条消息投递失败</strong>已达到最大重试次数，需要人工确认后重试。</span><Icon name="arrow" /></button> : null}

    {!canQuery ? <Empty title="没有通知查询权限" detail="需要 notification.messages.query 才能查看投递记录。" /> : <section className="panel table-panel">
      <div className="list-toolbar">
        <ChipFilter label="投递状态" value={status} onChange={setStatus} chips={[{ value: '', label: '全部' }, ...['Pending', 'Sending', 'Sent', 'Failed'].map((value) => ({ value, label: statusLabel(value), count: value === 'Failed' && failedQuery.data ? failed : undefined }))]} />
        <div className="list-toolbar__filters">
          <select className="select" value={templateCode} onChange={(event) => setTemplateCode(event.target.value)} aria-label="模板"><option value="">全部模板</option>{templatesQuery.data?.map((template) => <option key={template.id} value={template.code}>{template.code}</option>)}</select>
          <SearchInput value={userId} onChange={setUserId} placeholder="按 User ID 筛选" />
        </div>
      </div>
      {messagesQuery.isError ? <ErrorNotice error={messagesQuery.error} onRetry={() => void messagesQuery.refetch()} /> : null}
      {messagesQuery.isPending ? <SkeletonRows rows={8} /> : rows.length ? <div className={`data-table${messagesQuery.isPlaceholderData ? ' is-stale' : ''}`}><table>
        <thead><tr><th>主题 / 用户</th><th>内容摘要</th><th>模板</th><th>状态</th><th>创建</th><th>送达</th><th>操作</th></tr></thead>
        <tbody>{rows.map((message) => <tr key={message.id} className="is-clickable" onClick={() => setSelected(message)}>
          <td><strong>{message.subject}</strong><small className="mono">{message.userId}</small></td>
          <td className="cell-wrap cell-clamp">{message.body}</td>
          <td><span className="tag">{message.templateCode}</span></td>
          <td><Status>{message.status}</Status>{message.lastError ? <small className="number-bad" title={message.lastError}>{message.lastError}</small> : null}</td>
          <td title={formatDate(message.createdOnUtc)}>{formatRelative(message.createdOnUtc)}<small>重试 {message.retryCount} 次</small></td>
          <td>{message.sentOnUtc ? formatRelative(message.sentOnUtc) : '—'}</td>
          <td onClick={(event) => event.stopPropagation()}><div className="row-actions">
            <button type="button" onClick={() => setSelected(message)}>详情</button>
            {message.status === 'Failed' && can('notification.messages.edit') ? <button className="row-action-primary" type="button" onClick={() => retryMutation.mutate(message)} disabled={retryMutation.isPending}>重试</button> : null}
          </div></td>
        </tr>)}</tbody>
      </table></div> : !messagesQuery.isFetching ? <Empty title="暂无通知" detail={status || templateCode || userId ? '当前筛选条件下没有记录。' : '还没有任何投递记录。'} action={status || templateCode || userId ? <button className="button button--quiet" type="button" onClick={() => { setStatus(''); setTemplateCode(''); setUserId(''); }}>清空筛选</button> : undefined} /> : null}
      <Pagination page={page} pageSize={PAGE_SIZE} total={messagesQuery.data?.total} onChange={setPage} busy={messagesQuery.isFetching} />
    </section>}

    <Modal open={selected !== null} title={selected?.subject ?? '消息详情'} description={selected ? `${selected.templateCode} · ${statusLabel(selected.status)}` : undefined} onClose={() => setSelected(null)}>
      {selected ? <div className="order-detail">
        {selected.lastError ? <p className="notice notice--error"><Icon name="alert" />{selected.lastError}</p> : null}
        <section><h3>消息内容</h3><p className="message-body">{selected.body}</p></section>
        <dl className="detail-list">
          <div><dt>User ID</dt><dd className="mono">{selected.userId}<CopyButton value={selected.userId} /></dd></div>
          <div><dt>创建时间</dt><dd>{formatDate(selected.createdOnUtc)}</dd></div>
          <div><dt>送达时间</dt><dd>{formatDate(selected.sentOnUtc)}</dd></div>
          <div><dt>重试</dt><dd>{selected.retryCount} 次{selected.status !== 'Sent' ? ` · 下次尝试 ${formatDate(selected.nextAttemptOnUtc)}` : ''}</dd></div>
        </dl>
        <div className="detail-actions">
          <button className="button button--quiet" type="button" onClick={() => { setUserId(selected.userId); setSelected(null); }}><Icon name="search" />该用户的全部消息</button>
          {selected.status === 'Failed' && can('notification.messages.edit') ? <button className="button button--dark" type="button" disabled={retryMutation.isPending} onClick={() => retryMutation.mutate(selected)}><Icon name="refresh" />重新投递</button> : null}
        </div>
      </div> : null}
    </Modal>

    <Modal open={composing} wide title="发送用户通知" description={`单次最多 ${MAX_RECIPIENTS} 个用户；重复 User ID 会自动去重。`} onClose={closeComposer}>
      <ComposeForm initialRecipients={compose} templates={templatesQuery.data ?? []} onCancel={closeComposer} onSent={(count) => { toast.success(`已为 ${count} 位用户排队发送通知`); closeComposer(); void client.invalidateQueries({ queryKey: ['notifications'] }); }} />
    </Modal>
  </div>;
}

function ComposeForm({ initialRecipients, templates, onSent, onCancel }: { initialRecipients: string; templates: Template[]; onSent: (count: number) => void; onCancel: () => void }) {
  const [recipients, setRecipients] = useState(initialRecipients);
  const [templateCode, setTemplateCode] = useState('');
  const [subject, setSubject] = useState('');
  const [body, setBody] = useState('');
  const [requestKey] = useState(idempotencyKey);
  const userIds = [...new Set(recipients.split(/[\s,;]+/).map((value) => value.trim()).filter(Boolean))];
  const mutation = useMutation({
    mutationFn: () => api('/api/notification/admin/messages', { method: 'POST', body: { userIds, templateCode: templateCode || null, subject: subject.trim(), body: body.trim() }, headers: { 'Idempotency-Key': requestKey } }),
    onSuccess: () => onSent(userIds.length),
  });
  const applyTemplate = (code: string) => {
    const previous = templates.find((item) => item.code === templateCode);
    const template = templates.find((item) => item.code === code);
    setTemplateCode(code);
    // Only replace text the operator has not customised since the last template was applied.
    if (template && (!subject || subject === previous?.subject)) setSubject(template.subject);
    if (template && (!body || body === previous?.body)) setBody(template.body);
  };
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (userIds.length && userIds.length <= MAX_RECIPIENTS) mutation.mutate();
  };
  return <form className="modal-form compose-form" onSubmit={submit}>
    <div className="compose-form__editor">
      <Field label="收件人 User ID" hint="换行、空格、逗号或分号分隔"><textarea required rows={4} value={recipients} onChange={(event) => setRecipients(event.target.value)} className="mono" /></Field>
      <p className={`recipient-count${userIds.length > MAX_RECIPIENTS ? ' is-invalid' : ''}`}>{userIds.length} / {MAX_RECIPIENTS} 位用户{userIds.length > MAX_RECIPIENTS ? '，超出单次上限' : ''}</p>
      <Field label="模板" hint="选择模板会带入主题与正文，仍可修改"><select value={templateCode} onChange={(event) => applyTemplate(event.target.value)}><option value="">admin-message（自定义）</option>{templates.map((template) => <option key={template.id} value={template.code}>{template.code} · {template.subject}</option>)}</select></Field>
      <Field label="主题"><input required maxLength={500} value={subject} onChange={(event) => setSubject(event.target.value)} /></Field>
      <Field label={`内容（${body.length} / 10000）`}><textarea required rows={7} maxLength={10000} value={body} onChange={(event) => setBody(event.target.value)} /></Field>
    </div>
    <aside className="compose-form__preview" aria-label="预览">
      <p className="eyebrow">PREVIEW</p>
      <div className="message-card"><Icon name="notification" /><div><strong>{subject || '通知主题'}</strong><p>{body || '通知正文会显示在这里。'}</p><small>Morrow 商城 · 刚刚</small></div></div>
    </aside>
    {mutation.isError ? <ErrorNotice error={mutation.error} /> : null}
    <SubmitBar busy={mutation.isPending} label={userIds.length ? `发送给 ${userIds.length} 位用户` : '发送'} onCancel={onCancel} />
  </form>;
}
