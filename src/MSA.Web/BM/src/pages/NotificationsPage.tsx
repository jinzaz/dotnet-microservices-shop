import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, Icon, PageHeader, Status } from '../ui';

type Message = { id: string; subject: string; body: string; status: string; createdOnUtc: string; sentOnUtc?: string };

export default function NotificationsPage() {
  const { can } = useSession();
  const [userId, setUserId] = useState('');
  const query = useQuery({ queryKey: ['notifications', userId], queryFn: () => api<Message[]>(`/api/notification/users/${encodeURIComponent(userId)}?skip=0&take=50`), enabled: Boolean(userId) && can('notification.messages.query'), retry: false });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); setUserId(String(new FormData(event.currentTarget).get('userId') ?? '').trim()); };

  return <div className="page">
    <PageHeader eyebrow="NOTIFICATION / DELIVERY" title="用户消息轨迹" description="审阅 NotificationService 已创建和已送达的用户通知。" actions={<button className="button button--quiet" type="button" disabled title="等待发送接口"><Icon name="plus" />发送通知 · 待接口</button>} />
    <section className="lookup-bar panel"><form onSubmit={submit}><Field label="Identity User ID"><input name="userId" required placeholder="输入用户标识" /></Field><button className="button button--dark" type="submit" disabled={!can('notification.messages.query')}><Icon name="search" />读取通知</button></form><span>最多返回最近 50 条</span></section>
    {query.isError ? <ErrorNotice error={query.error} /> : null}
    {query.data?.length ? <section className="panel table-panel"><div className="section-heading"><div><p className="eyebrow">MESSAGE LOG</p><h2>触达记录</h2></div><span>{query.data.length} 条</span></div><div className="data-table"><table><thead><tr><th>主题</th><th>内容摘要</th><th>状态</th><th>创建时间</th><th>送达时间</th></tr></thead><tbody>{query.data.map((message) => <tr key={message.id}><td><strong>{message.subject}</strong><small>{message.id.slice(0, 8)}</small></td><td className="cell-wrap">{message.body}</td><td><Status>{message.status}</Status></td><td>{formatDate(message.createdOnUtc)}</td><td>{formatDate(message.sentOnUtc)}</td></tr>)}</tbody></table></div></section> : !query.isFetching && !query.isError ? <Empty title="还没有选择用户" detail="输入 Identity User ID，查看该用户的通知投递轨迹。" /> : null}
    <p className="capability-note"><Icon name="alert" />缺少后台消息列表、模板、主动发送、失败重试和批量触达接口。</p>
  </div>;
}
