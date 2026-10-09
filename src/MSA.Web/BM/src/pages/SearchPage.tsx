import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, query, type Paged } from '../api';
import { useSession } from '../session';
import { CopyButton, Empty, ErrorNotice, formatDate, formatMoney, formatRelative, Icon, PageHeader, SearchInput, SkeletonRows, useConfirm, useDebouncedValue, useToast, useUrlState } from '../ui';

type SearchStatus = {
  alias: string;
  indices: string[];
  documentCount: number | null;
  rebuilding: boolean;
  rebuildStartedOnUtc: string | null;
  lastRebuildCompletedOnUtc: string | null;
  lastRebuildDocumentCount: number | null;
  lastError: string | null;
};

type SearchHit = {
  id: string;
  categoryId: string;
  sku: string;
  name: string;
  nameHighlight: string | null;
  price: number;
  minPrice: number;
  maxPrice: number;
  currency: string;
  variantCount: number;
  image: string | null;
  updatedOnUtc: string;
};

type SearchResult = {
  items: SearchHit[];
  total: number;
  totalIsLowerBound: boolean;
  facets: { categories: { id: string; count: number }[] } | null;
};

type Category = { id: string; name: string };

const sorts = [
  { value: '', label: '综合排序' },
  { value: 'newest', label: '新品优先' },
  { value: 'price_asc', label: '价格从低到高' },
  { value: 'price_desc', label: '价格从高到低' },
];

/** The search service marks hits with STX/ETX instead of HTML tags; render them as <mark> without innerHTML. */
function Highlighted({ text, highlight }: { text: string; highlight: string | null }) {
  if (!highlight?.includes('\u0002')) return <>{text}</>;
  return <>{highlight.split('\u0002').flatMap((segment, index) => {
    const end = segment.indexOf('\u0003');
    if (end < 0) return segment ? [<span key={index}>{segment}</span>] : [];
    return [<mark key={`${index}-hit`}>{segment.slice(0, end)}</mark>, <span key={index}>{segment.slice(end + 1)}</span>];
  })}</>;
}

function unavailable(error: unknown) {
  return error instanceof ApiError && error.status === 503;
}

export default function SearchPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const canQuery = can('system.search.query');
  const canEdit = can('system.search.edit');
  const canCatalog = can('catalog.products.query');
  const canCategories = can('catalog.categories.query');
  const [q, setQ] = useUrlState('q');
  const [sort, setSort] = useUrlState('sort');
  const keyword = useDebouncedValue(q.trim(), 300);

  const status = useQuery({
    queryKey: ['search', 'status'],
    queryFn: ({ signal }) => api<SearchStatus>('/api/search/admin/status', { signal }),
    enabled: canQuery,
    // 重建时每 3 秒刷新一次进度，平时 30 秒。
    refetchInterval: (current) => current.state.data?.rebuilding ? 3_000 : 30_000,
  });
  // 目录里已发布的商品数：和索引文档数对不上，说明同步链路有积压或丢失（定时上架、规格全停售的商品不进索引，略有差异是正常的）。
  const published = useQuery({
    queryKey: ['search', 'catalog-published'],
    queryFn: ({ signal }) => api<Paged<unknown>>(`/api/catalog/admin/products?${query({ lifecycleStatus: 'Published', take: 1 })}`, { signal }),
    enabled: canQuery && canCatalog,
    refetchInterval: 60_000,
  });
  const categories = useQuery({
    queryKey: ['search', 'categories'],
    queryFn: ({ signal }) => api<Paged<Category>>(`/api/catalog/admin/categories?${query({ take: 200 })}`, { signal }),
    enabled: canQuery && canCategories,
    staleTime: 300_000,
  });
  const preview = useQuery({
    queryKey: ['search', 'preview', keyword, sort],
    queryFn: ({ signal }) => api<SearchResult>(`/api/search/products?${query({ q: keyword, sort, take: 20 })}`, { signal }),
    enabled: canQuery,
    placeholderData: keepPreviousData,
    retry: false,
  });

  const rebuild = useMutation({
    mutationFn: (force: boolean) => api<unknown>(`/api/search/admin/rebuild?${query({ force: force || undefined })}`, { method: 'POST' }),
    onSuccess: async () => {
      toast.success('已开始重建，前台在切换完成前继续使用旧索引');
      await client.invalidateQueries({ queryKey: ['search', 'status'] });
    },
    onError: (error) => toast.error(error instanceof ApiError && error.status === 409 ? '已有重建在进行中（可能在另一个副本上）。' : error),
  });

  const confirmRebuild = async (force: boolean) => {
    const result = await confirm({
      title: force ? '强制重建搜索索引？' : '全量重建搜索索引？',
      detail: force
        ? '会先清除上一次重建遗留的锁。只在确认之前的重建进程已经退出（例如服务崩溃重启）时使用，否则会出现两个重建同时进行。'
        : '从商品目录导出全部可搜商品写入新索引，完成后原子切换，前台不会出现空结果。50 万商品通常需要几分钟。改了分词、同义词结构或映射后需要执行。',
      confirmLabel: force ? '强制重建' : '开始重建',
      tone: force ? 'danger' : 'default',
    });
    if (result) rebuild.mutate(force);
  };

  if (!canQuery) return <div className="page"><Empty title="没有查询权限" detail="需要 system.search.query 才能查看搜索索引。" /></div>;

  const data = status.data;
  const categoryNames = new Map((categories.data?.items ?? []).map((category) => [category.id, category.name]));
  const facetCounts = new Map((preview.data?.facets?.categories ?? []).map((bucket) => [bucket.id, bucket.count]));
  const drift = data?.documentCount != null && published.data ? published.data.total - data.documentCount : null;

  return <div className="page">
    <PageHeader
      eyebrow="SEARCH / ELASTICSEARCH"
      title="搜索索引"
      description="商品搜索由 Elasticsearch + IK 分词提供，索引是商品目录的投影：后台改商品后几秒内自动同步，需要时可以从目录全量重建。"
      actions={<div className="row-actions">
        <button className="button button--quiet" type="button" onClick={() => void client.invalidateQueries({ queryKey: ['search'] })}><Icon name="refresh" />刷新</button>
        {canEdit ? <button className="button button--dark" type="button" disabled={rebuild.isPending || data?.rebuilding} onClick={() => void confirmRebuild(false)}>全量重建</button> : null}
      </div>} />

    {status.isError ? <ErrorNotice error={status.error} onRetry={() => void status.refetch()} /> : null}
    {data?.lastError ? <p className="notice notice--error" role="alert"><Icon name="alert" /><span>最近一次错误：{data.lastError}</span>
      {canEdit && !data.rebuilding ? <button className="notice__action" type="button" onClick={() => void confirmRebuild(true)}>强制重建</button> : null}</p> : null}

    <div className="kpi-strip" style={{ gridTemplateColumns: 'repeat(4, minmax(0, 1fr))' }}>
      <div>
        <span>当前索引</span>
        <strong className="mono" style={{ fontSize: 15 }}>{status.isPending ? '—' : data?.indices[0] ?? '未建立'}</strong>
        <small>别名 {data?.alias ?? '—'}{data?.indices[0] ? <CopyButton value={data.indices[0]} label="复制索引名" /> : null}</small>
      </div>
      <div>
        <span>索引文档数</span>
        <strong>{data?.documentCount?.toLocaleString('zh-CN') ?? '—'}</strong>
        <small>{data?.documentCount == null ? 'ES 不可用或索引未建立' : '可被前台搜到的商品'}</small>
      </div>
      <div>
        <span>目录已发布商品</span>
        <strong>{canCatalog ? published.data?.total.toLocaleString('zh-CN') ?? '—' : '无权限'}</strong>
        <small className={drift != null && Math.abs(drift) > 100 ? 'number-bad' : undefined}>
          {drift == null ? '用于核对同步是否有积压' : drift === 0 ? '与索引一致' : `相差 ${drift.toLocaleString('zh-CN')}（定时上架、无在售规格的不计入索引）`}
        </small>
      </div>
      <div>
        <span>重建</span>
        <strong style={{ fontSize: 18 }}>{data?.rebuilding ? '进行中' : data?.lastRebuildCompletedOnUtc ? '空闲' : '—'}</strong>
        <small title={formatDate(data?.rebuilding ? data.rebuildStartedOnUtc : data?.lastRebuildCompletedOnUtc)}>
          {data?.rebuilding
            ? `开始于 ${formatRelative(data.rebuildStartedOnUtc)}`
            : data?.lastRebuildCompletedOnUtc
              ? `${formatRelative(data.lastRebuildCompletedOnUtc)}完成 · ${data.lastRebuildDocumentCount?.toLocaleString('zh-CN') ?? 0} 个`
              : '本副本启动后未重建过'}
        </small>
      </div>
    </div>

    <section className="panel table-panel">
      <div className="section-heading">
        <div><p className="eyebrow">PREVIEW</p><h2>搜索预览</h2></div>
        <div className="list-toolbar__filters">
          <select className="select" value={sort} onChange={(event) => setSort(event.target.value)} aria-label="排序">
            {sorts.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
          <SearchInput value={q} onChange={setQ} placeholder="输入关键词，按前台的规则搜索" />
        </div>
      </div>
      {preview.isError ? unavailable(preview.error)
        ? <p className="notice notice--warning" role="status"><Icon name="alert" />搜索引擎暂不可用（ES 未就绪或熔断中）。前台此时会自动回退到数据库搜索，没有分面与排序。</p>
        : <ErrorNotice error={preview.error} onRetry={() => void preview.refetch()} /> : null}
      {preview.data?.facets?.categories.length ? <p className="capability-note">
        {[...facetCounts.entries()].slice(0, 12).map(([id, count]) => `${categoryNames.get(id) ?? id.slice(0, 8)} ${count.toLocaleString('zh-CN')}`).join(' · ')}
      </p> : null}
      {preview.isPending ? <SkeletonRows rows={5} /> : preview.data?.items.length ? <div className={`data-table${preview.isPlaceholderData ? ' is-stale' : ''}`}><table>
        <thead><tr><th>#</th><th>商品</th><th>分类</th><th>价格</th><th>规格</th><th>更新</th></tr></thead>
        <tbody>{preview.data.items.map((hit, index) => <tr key={hit.id}>
          <td className="mono">{index + 1}</td>
          <td>
            <span className="cell-with-copy">
              {hit.image ? <img src={hit.image} alt="" width={36} height={36} style={{ borderRadius: 6, objectFit: 'cover' }} loading="lazy" /> : null}
              <strong><Highlighted text={hit.name} highlight={hit.nameHighlight} /></strong>
            </span>
            <small className="mono">{hit.sku}<CopyButton value={hit.id} label="复制商品 ID" /></small>
          </td>
          <td>{categoryNames.get(hit.categoryId) ?? <span className="mono">{hit.categoryId.slice(0, 8)}</span>}</td>
          <td>{hit.minPrice === hit.maxPrice ? formatMoney(hit.minPrice, hit.currency) : `${formatMoney(hit.minPrice, hit.currency)} 起`}</td>
          <td>{hit.variantCount} 款</td>
          <td title={formatDate(hit.updatedOnUtc)}>{formatRelative(hit.updatedOnUtc)}</td>
        </tr>)}</tbody>
      </table></div> : !preview.isError ? <Empty title="没有命中" detail={keyword ? `没有与“${keyword}”相关的可售商品。` : '索引里还没有商品。'} /> : null}
      {preview.data ? <p className="capability-note"><Icon name="search" />共 {preview.data.total.toLocaleString('zh-CN')}{preview.data.totalIsLowerBound ? '+' : ''} 个结果，这里只显示前 20 个；顺序与前台一致。</p> : null}
    </section>

    <section className="panel">
      <div className="section-heading"><div><p className="eyebrow">RUNBOOK</p><h2>常见情况</h2></div></div>
      <dl className="detail-list detail-list--wide">
        <div><dt>改了商品，前台搜不到新内容</dt><dd>同步一般在几秒内完成。超过 1 分钟仍未生效，到「消息运维」查看搜索服务的死信与失败消息。</dd></div>
        <div><dt>文档数与目录差很多</dt><dd>多半是 ES 停机期间消息进了死信。恢复后执行一次全量重建即可补齐，死信可以直接丢弃。</dd></div>
        <div><dt>改了分词、同义词或映射</dt><dd>执行全量重建；同义词只是增删词条时，改配置后重启搜索服务即可生效，不需要重建。</dd></div>
        <div><dt>重建一直显示进行中</dt><dd>重建进程崩溃会留下锁，2 小时后自动失效；确认没有重建在跑时可以「强制重建」。</dd></div>
      </dl>
    </section>
  </div>;
}
