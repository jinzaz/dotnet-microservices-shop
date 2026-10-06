import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent, type ReactNode } from 'react';
import { api, query } from '../api';
import { useSession } from '../session';
import { ChipFilter, CopyButton, Empty, ErrorNotice, Field, formatDate, formatRelative, Icon, Modal, PageHeader, SkeletonRows, SubmitBar, useConfirm, useToast, useUrlState } from '../ui';

type Stage = 'Staged' | 'Active' | 'Retiring' | 'Retired' | 'Expired' | 'Revoked';
type Certificate = {
  id: string; consumer: string; purpose: string; slot: string; name: string; source: 'Imported' | 'Generated' | 'Acme';
  stage: Stage; retireAt: string | null; subject: string; issuer: string; thumbprint: string; keyAlgorithm: string; dnsNames: string[];
  notBefore: string; notAfter: string; activateAt: string; revokedAt: string | null; revokedReason: string | null;
  exportable: boolean; autoRotate: boolean; acmeSubscriptionId: string | null; createdAt: string; createdBy: string;
};
type Purpose = { code: string; displayName: string; consumers: string[]; acceptedKeys: string; canGenerate: boolean; activationLeadMinutes: number; retentionHours: number; rotateAfterDays: number; validityDays: number };
type RenewalRun = { startedAt: string; finishedAt: string; rotated: number; acmeRenewed: number; acmeFailed: number; tlsDeployed: number; warnings: string[]; errors: string[] };
type Catalog = {
  consumers: string[]; purposes: Purpose[]; dnsProviders: Array<{ name: string; displayName: string; configured: boolean }>;
  acmeProductionDirectory: string; acmeStagingDirectory: string; tlsDeploymentConfigured: boolean;
  renewalIntervalMinutes: number; nextRenewalRunAt: string | null; lastRenewalRun: RenewalRun | null;
};
type Subscription = {
  id: string; slot: string; domains: string[]; challengeType: 'Dns01' | 'Http01'; dnsProvider: string | null; directoryUrl: string; contactEmail: string;
  enabled: boolean; status: 'Pending' | 'Succeeded' | 'Failed'; lastError: string | null; lastAttemptAt: string | null; lastSucceededAt: string | null;
  nextAttemptAt: string | null; failureCount: number; renewRequested: boolean; running: boolean; currentCertificateId: string | null; currentNotAfter: string | null; renewAt: string | null;
};
type Audit = { id: string; at: string; actor: string; action: string; certificateId: string | null; acmeSubscriptionId: string | null; detail: string | null };
type ExportedFiles = { files: Array<{ fileName: string; contentType: string; contentBase64: string }> };

const base = '/api/certificate/admin';
const consumerLabels: Record<string, string> = { identity: '认证中心', 'bff-web': '商城 BFF', 'bff-bm': '后台 BFF', edge: '入口 nginx' };
const stages: Record<Stage, [label: string, tone: 'good' | 'warn' | 'bad' | 'neutral']> = {
  Staged: ['待生效', 'warn'], Active: ['当前', 'good'], Retiring: ['退役中', 'neutral'], Retired: ['已退役', 'neutral'], Expired: ['已过期', 'bad'], Revoked: ['已吊销', 'bad'],
};
const sourceLabels: Record<Certificate['source'], string> = { Imported: '导入', Generated: '平台生成', Acme: 'ACME' };
const auditLabels: Record<string, string> = {
  import: '导入', generate: '生成', 'acme-issue': 'ACME 签发', schedule: '调整生效时间', revoke: '吊销', update: '修改设置', 'export-private-key': '导出私钥',
  'acme-create': '新增 ACME 托管', 'acme-update': '修改 ACME 托管', 'acme-delete': '删除 ACME 托管', 'acme-renew-requested': '请求立即续订', 'acme-failed': 'ACME 续订失败',
};
const EXPIRY_WARNING_DAYS = 30;
const DAY = 86_400_000;

function Badge({ tone, children }: { tone: 'good' | 'warn' | 'bad' | 'neutral'; children: ReactNode }) {
  return <span className={`status status--${tone}`}><i />{children}</span>;
}

function daysLeft(value: string) {
  return Math.floor((new Date(value).getTime() - Date.now()) / DAY);
}

/** 本地时间输入框 → ISO；留空表示交给服务端决定。 */
function toIso(value: FormDataEntryValue | null) {
  return typeof value === 'string' && value ? new Date(value).toISOString() : null;
}

async function fileToBase64(file: File) {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  for (let index = 0; index < bytes.length; index += 0x8000) binary += String.fromCharCode(...bytes.subarray(index, index + 0x8000));
  return btoa(binary);
}

function download(result: ExportedFiles) {
  for (const file of result.files) {
    const bytes = Uint8Array.from(atob(file.contentBase64), (character) => character.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: file.contentType }));
    const link = Object.assign(document.createElement('a'), { href: url, download: file.fileName });
    link.click();
    URL.revokeObjectURL(url);
  }
}

export default function CertificatesPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const canQuery = can('system.certificates.query');
  const canCreate = can('system.certificates.create');
  const canEdit = can('system.certificates.edit');
  const canDelete = can('system.certificates.delete');
  const canExportKey = can('system.certificate-keys.query');
  const [consumer, setConsumer] = useUrlState('consumer', '');
  const [inactive, setInactive] = useUrlState('inactive', '');
  const [dialog, setDialog] = useState<'generate' | 'import' | null>(null);
  const [editing, setEditing] = useState<Certificate | null>(null);
  const [exporting, setExporting] = useState<Certificate | null>(null);
  const [subscription, setSubscription] = useState<Subscription | 'new' | null>(null);

  const catalog = useQuery({ queryKey: ['certificates', 'catalog'], queryFn: ({ signal }) => api<Catalog>(`${base}/catalog`, { signal }), enabled: canQuery, refetchInterval: 60_000 });
  const certificates = useQuery({
    queryKey: ['certificates', 'list', consumer, inactive],
    queryFn: ({ signal }) => api<Certificate[]>(`${base}/certificates?${query({ consumer, includeInactive: inactive === '1' })}`, { signal }),
    enabled: canQuery,
    refetchInterval: 60_000,
  });
  const subscriptions = useQuery({ queryKey: ['certificates', 'acme'], queryFn: ({ signal }) => api<Subscription[]>(`${base}/acme/subscriptions`, { signal }), enabled: canQuery, refetchInterval: 30_000 });
  const audits = useQuery({ queryKey: ['certificates', 'audits'], queryFn: ({ signal }) => api<Audit[]>(`${base}/audits?take=30`, { signal }), enabled: canQuery });

  const refresh = () => client.invalidateQueries({ queryKey: ['certificates'] });
  const run = useMutation({
    mutationFn: async (action: () => Promise<unknown>) => action(),
    onSuccess: refresh,
    onError: (error) => toast.error(error),
  });

  const runRenewal = () => run.mutate(async () => {
    await api(`${base}/renewal/run`, { method: 'POST' });
    toast.info('已触发证书检查，几秒后刷新可看到结果。');
  });
  const activateNow = async (certificate: Certificate) => {
    const result = await confirm({ title: `立即启用「${certificate.name}」？`, detail: '它会马上成为当前证书。签名证书提前生效时，资源服务可能还没拉到新公钥，令牌会短暂验签失败；没有特殊原因请等排期。', confirmLabel: '立即生效' });
    if (result) run.mutate(async () => { await api(`${base}/certificates/${certificate.id}/activate`, { method: 'POST', body: {} }); toast.success('已生效'); });
  };
  const revoke = async (certificate: Certificate) => {
    const result = await confirm({
      title: `吊销「${certificate.name}」？`,
      detail: '吊销后各服务在一分钟内不再加载它；如果它是当前证书，上一张未退役的证书会重新成为当前证书。此操作不可撤销。',
      confirmLabel: '吊销', tone: 'danger', input: { label: '原因', maxLength: 500, placeholder: '例如：私钥可能泄露' },
    });
    if (result) run.mutate(async () => { await api(`${base}/certificates/${certificate.id}/revoke`, { method: 'POST', body: { reason: result.value } }); toast.success('已吊销'); });
  };
  const exportPublic = (certificate: Certificate) => run.mutate(async () => download(await api<ExportedFiles>(`${base}/certificates/${certificate.id}/export?format=pem`)));
  const renew = (item: Subscription) => run.mutate(async () => { await api(`${base}/acme/subscriptions/${item.id}/renew`, { method: 'POST' }); toast.info(`已安排 ${item.slot} 立即续订`); });
  const removeSubscription = async (item: Subscription) => {
    const result = await confirm({ title: `删除 ${item.slot} 的 ACME 托管？`, detail: '已签发的证书继续使用，但到期前不会再自动续订。', confirmLabel: '删除', tone: 'danger' });
    if (result) run.mutate(async () => { await api(`${base}/acme/subscriptions/${item.id}`, { method: 'DELETE' }); toast.success('已删除'); });
  };

  if (!canQuery) return <div className="page"><Empty title="没有证书查询权限" detail="需要 system.certificates.query 才能查看证书库。" /></div>;

  const purposes = catalog.data?.purposes ?? [];
  const purposeName = (code: string) => purposes.find((purpose) => purpose.code === code)?.displayName ?? code;
  const consumerChips = [{ value: '', label: '全部' }, ...(catalog.data?.consumers ?? []).map((value) => ({ value, label: consumerLabels[value] ?? value }))];

  return <div className="page">
    <PageHeader
      eyebrow="SECURITY / CERTIFICATES"
      title="证书与密钥"
      description="认证中心签名 / 加密、BFF 登录状态与请求体加密、站点 HTTPS 证书都在这里。到期前自动轮换或经 ACME 续订，换证书不需要重启服务。"
      actions={<>
        <button className="button button--quiet" type="button" onClick={() => void refresh()}><Icon name="refresh" />刷新</button>
        {canEdit ? <button className="button button--quiet" type="button" disabled={run.isPending} onClick={runRenewal}><Icon name="activity" />立即检查续期</button> : null}
        {canCreate ? <button className="button button--quiet" type="button" onClick={() => setDialog('import')}><Icon name="upload" />导入</button> : null}
        {canCreate ? <button className="button button--dark" type="button" onClick={() => setDialog('generate')}><Icon name="plus" />生成证书</button> : null}
      </>} />

    {catalog.data ? <RenewalStatus catalog={catalog.data} /> : null}

    <section className="panel table-panel">
      <div className="section-heading">
        <div><p className="eyebrow">STORE</p><h2>证书库</h2></div>
        <div className="row-actions">
          <ChipFilter label="使用方" value={consumer} onChange={setConsumer} chips={consumerChips} />
          <label className="switch-row"><input type="checkbox" checked={inactive === '1'} onChange={(event) => setInactive(event.target.checked ? '1' : '')} /><span />含已退役</label>
        </div>
      </div>
      {certificates.isError ? <ErrorNotice error={certificates.error} onRetry={() => void certificates.refetch()} /> : null}
      {certificates.isPending ? <SkeletonRows rows={4} /> : certificates.data?.length ? <div className="data-table"><table>
        <thead><tr><th>证书</th><th>使用方 / 用途</th><th>阶段</th><th>有效期至</th><th>生效时间</th><th>来源</th><th>操作</th></tr></thead>
        <tbody>{certificates.data.map((certificate) => {
          const [label, tone] = stages[certificate.stage];
          const remaining = daysLeft(certificate.notAfter);
          const usable = certificate.stage === 'Active' || certificate.stage === 'Staged' || certificate.stage === 'Retiring';
          return <tr key={certificate.id}>
            <td><strong>{certificate.name}</strong><small className="mono" title={certificate.subject}>{certificate.thumbprint.slice(0, 16)}<CopyButton value={certificate.thumbprint} label="复制 SHA-256 指纹" /></small>{certificate.dnsNames.length ? <div className="tag-list">{certificate.dnsNames.map((name) => <span key={name}>{name}</span>)}</div> : null}</td>
            <td>{consumerLabels[certificate.consumer] ?? certificate.consumer}<small>{purposeName(certificate.purpose)}{certificate.slot !== 'default' ? ` · ${certificate.slot}` : ''} · {certificate.keyAlgorithm}</small></td>
            <td><Badge tone={tone}>{label}</Badge>{certificate.retireAt && certificate.stage === 'Retiring' ? <small title={formatDate(certificate.retireAt)}>{formatRelative(certificate.retireAt)}退役</small> : null}{certificate.revokedReason ? <small className="number-bad">{certificate.revokedReason}</small> : null}</td>
            <td title={formatDate(certificate.notAfter)}>{formatDate(certificate.notAfter)}{usable ? <small className={remaining < EXPIRY_WARNING_DAYS ? 'number-bad' : undefined}>剩 {remaining} 天</small> : null}</td>
            <td title={formatDate(certificate.activateAt)}>{formatRelative(certificate.activateAt)}</td>
            <td>{sourceLabels[certificate.source]}<small>{certificate.purpose === 'tls' ? (certificate.source === 'Acme' ? '自动续订' : '需手动续期') : certificate.autoRotate ? '自动轮换' : '不自动轮换'}</small></td>
            <td><div className="row-actions">
              {certificate.stage === 'Staged' && canEdit ? <button type="button" className="row-action-primary" onClick={() => void activateNow(certificate)}>立即生效</button> : null}
              <button type="button" onClick={() => exportPublic(certificate)} disabled={run.isPending}>公钥</button>
              {canExportKey && certificate.exportable && certificate.stage !== 'Revoked' ? <button type="button" onClick={() => setExporting(certificate)}>私钥</button> : null}
              {canEdit && certificate.stage !== 'Revoked' ? <button type="button" onClick={() => setEditing(certificate)}>设置</button> : null}
              {canDelete && usable ? <button type="button" onClick={() => void revoke(certificate)}>吊销</button> : null}
            </div></td>
          </tr>;
        })}</tbody>
      </table></div> : <Empty title="证书库是空的" detail="还没有证书时各服务使用环境变量里的 pfx（开发环境用开发证书）。迁移时先把现有 pfx 导入并立即生效。" />}
      <p className="capability-note"><Icon name="shield" />新证书按提前量排期生效（签名证书 2 天，其余 10 分钟），被替换的证书保留一段时间用于验签 / 解密旧令牌后自动退役。</p>
    </section>

    <section className="panel table-panel">
      <div className="section-heading">
        <div><p className="eyebrow">LET'S ENCRYPT</p><h2>ACME 托管站点</h2></div>
        {canCreate ? <button className="button button--dark" type="button" onClick={() => setSubscription('new')}><Icon name="plus" />新增托管</button> : null}
      </div>
      {subscriptions.isError ? <ErrorNotice error={subscriptions.error} onRetry={() => void subscriptions.refetch()} /> : null}
      {subscriptions.isPending ? <SkeletonRows rows={2} /> : subscriptions.data?.length ? <div className="data-table"><table>
        <thead><tr><th>站点</th><th>域名</th><th>验证</th><th>状态</th><th>证书到期</th><th>计划续订</th><th>操作</th></tr></thead>
        <tbody>{subscriptions.data.map((item) => <tr key={item.id}>
          <td><strong>{item.slot}</strong><small>{item.directoryUrl === catalog.data?.acmeStagingDirectory ? '测试环境（证书不受信任）' : item.directoryUrl === catalog.data?.acmeProductionDirectory ? "Let's Encrypt" : item.directoryUrl}</small></td>
          <td><div className="tag-list">{item.domains.map((domain) => <span key={domain}>{domain}</span>)}</div></td>
          <td>{item.challengeType === 'Dns01' ? 'DNS-01' : 'HTTP-01'}<small>{item.dnsProvider ? catalog.data?.dnsProviders.find((provider) => provider.name === item.dnsProvider)?.displayName ?? item.dnsProvider : ''}</small></td>
          <td>{!item.enabled ? <Badge tone="neutral">已停用</Badge> : item.running ? <Badge tone="warn">申请中</Badge> : item.status === 'Failed' ? <Badge tone="bad">失败 {item.failureCount} 次</Badge> : item.status === 'Succeeded' ? <Badge tone="good">正常</Badge> : <Badge tone="warn">等待申请</Badge>}
            {item.lastError ? <small className="error-cell" title={item.lastError}>{item.lastError}</small> : null}
            {item.nextAttemptAt && item.status === 'Failed' ? <small title={formatDate(item.nextAttemptAt)}>{formatRelative(item.nextAttemptAt)}重试</small> : null}</td>
          <td>{item.currentNotAfter ? <>{formatDate(item.currentNotAfter)}<small className={daysLeft(item.currentNotAfter) < 14 ? 'number-bad' : undefined}>剩 {daysLeft(item.currentNotAfter)} 天</small></> : '—'}</td>
          <td>{item.renewRequested ? '下一轮检查' : item.renewAt ? <span title={formatDate(item.renewAt)}>{formatRelative(item.renewAt)}</span> : '—'}</td>
          <td><div className="row-actions">
            {canEdit ? <button type="button" className="row-action-primary" disabled={run.isPending || item.running} onClick={() => renew(item)}>立即续订</button> : null}
            {canEdit ? <button type="button" onClick={() => setSubscription(item)}>编辑</button> : null}
            {canDelete ? <button type="button" onClick={() => void removeSubscription(item)}>删除</button> : null}
          </div></td>
        </tr>)}</tbody>
      </table></div> : <Empty title="还没有 ACME 托管" detail="添加站点域名后，定时任务会自动向 Let's Encrypt 申请证书并在到期前续订。通配符域名需要 DNS-01。" />}
    </section>

    <section className="panel table-panel">
      <div className="section-heading"><div><p className="eyebrow">AUDIT</p><h2>操作记录</h2></div></div>
      {audits.data?.length ? <div className="data-table"><table>
        <thead><tr><th>时间</th><th>操作人</th><th>动作</th><th>说明</th></tr></thead>
        <tbody>{audits.data.map((audit) => <tr key={audit.id}>
          <td title={formatDate(audit.at)}>{formatRelative(audit.at)}</td>
          <td>{audit.actor === 'system' ? '定时任务' : audit.actor}</td>
          <td>{auditLabels[audit.action] ?? audit.action}</td>
          <td><small className="error-cell" title={audit.detail ?? ''}>{audit.detail ?? '—'}</small></td>
        </tr>)}</tbody>
      </table></div> : <Empty title="暂无操作记录" detail="导入、生成、吊销、导出私钥等操作都会记录在这里。" />}
    </section>

    <Modal open={dialog === 'generate'} title="生成证书" description="为内部用途生成自签名证书；站点 HTTPS 证书请用 ACME 托管或导入。" onClose={() => setDialog(null)}>
      {catalog.data ? <GenerateForm catalog={catalog.data} onDone={() => { setDialog(null); void refresh(); }} onCancel={() => setDialog(null)} /> : null}
    </Modal>
    <Modal open={dialog === 'import'} wide title="导入证书" description="支持 PFX（含口令）或 PEM（证书链 + 私钥）。私钥用使用方的 KEK 加密后入库。" onClose={() => setDialog(null)}>
      {catalog.data ? <ImportForm catalog={catalog.data} onDone={() => { setDialog(null); void refresh(); }} onCancel={() => setDialog(null)} /> : null}
    </Modal>
    <Modal open={editing !== null} title="证书设置" description={editing?.name} onClose={() => setEditing(null)}>
      {editing ? <EditForm certificate={editing} onDone={() => { setEditing(null); void refresh(); }} onCancel={() => setEditing(null)} /> : null}
    </Modal>
    <Modal open={exporting !== null} title="导出私钥" description="导出会记录到操作记录。请妥善保管导出的文件，用完即删。" onClose={() => setExporting(null)}>
      {exporting ? <ExportForm certificate={exporting} onDone={() => { setExporting(null); void refresh(); }} onCancel={() => setExporting(null)} /> : null}
    </Modal>
    <Modal open={subscription !== null} wide title={subscription === 'new' ? '新增 ACME 托管' : `编辑 ${typeof subscription === 'object' && subscription ? subscription.slot : ''}`} description="证书用掉 2/3 寿命时自动续订，失败按 1、2、4…24 小时退避重试。" onClose={() => setSubscription(null)}>
      {subscription !== null && catalog.data ? <SubscriptionForm catalog={catalog.data} item={subscription === 'new' ? null : subscription} onDone={() => { setSubscription(null); void refresh(); }} onCancel={() => setSubscription(null)} /> : null}
    </Modal>
  </div>;
}

function RenewalStatus({ catalog }: { catalog: Catalog }) {
  const last = catalog.lastRenewalRun;
  return <>
    <div className="kpi-strip" style={{ gridTemplateColumns: 'repeat(3, minmax(0, 1fr))' }}>
      <div><span>下次自动检查</span><strong>{catalog.nextRenewalRunAt ? formatRelative(catalog.nextRenewalRunAt) : '—'}</strong><small>每 {Math.round(catalog.renewalIntervalMinutes)} 分钟一轮</small></div>
      <div><span>上次检查</span><strong>{last ? formatRelative(last.finishedAt) : '尚未运行'}</strong><small>{last ? `轮换 ${last.rotated} · 续订 ${last.acmeRenewed} · 失败 ${last.acmeFailed}` : '服务启动 30 秒后运行第一轮'}</small></div>
      <div><span>nginx 证书部署</span><strong>{catalog.tlsDeploymentConfigured ? '已配置' : '未配置'}</strong><small>{catalog.tlsDeploymentConfigured ? `上次写入 ${last?.tlsDeployed ?? 0} 个站点` : 'TLS 证书不会写到 nginx 目录'}</small></div>
    </div>
    {last?.errors.length ? <p className="notice notice--error" role="alert"><Icon name="alert" /><span>{last.errors.join('；')}</span></p> : null}
    {last?.warnings.length ? <p className="notice notice--error"><Icon name="alert" /><span>需要处理：{last.warnings.join('；')}</span></p> : null}
  </>;
}

type FormProps = { onDone: () => void; onCancel: () => void };

function useSubmit(onDone: () => void, success: string) {
  const toast = useToast();
  return useMutation({
    mutationFn: (action: () => Promise<unknown>) => action(),
    onSuccess: () => { toast.success(success); onDone(); },
    onError: (error) => toast.error(error),
  });
}

function ConsumerPurposeFields({ catalog, generatableOnly, onPurpose }: { catalog: Catalog; generatableOnly: boolean; onPurpose?: (purpose: string) => void }) {
  const consumers = catalog.consumers.filter((consumer) => !generatableOnly || catalog.purposes.some((purpose) => purpose.canGenerate && purpose.consumers.includes(consumer)));
  const [consumer, setConsumer] = useState(consumers[0] ?? '');
  const purposes = catalog.purposes.filter((purpose) => purpose.consumers.includes(consumer) && (!generatableOnly || purpose.canGenerate));
  return <div className="form-row">
    <Field label="使用方"><select name="consumer" value={consumer} onChange={(event) => { setConsumer(event.target.value); onPurpose?.(catalog.purposes.find((purpose) => purpose.consumers.includes(event.target.value))?.code ?? ''); }}>
      {consumers.map((value) => <option key={value} value={value}>{consumerLabels[value] ?? value}</option>)}
    </select></Field>
    <Field label="用途"><select name="purpose" key={consumer} defaultValue={purposes[0]?.code} onChange={(event) => onPurpose?.(event.target.value)}>
      {purposes.map((purpose) => <option key={purpose.code} value={purpose.code}>{purpose.displayName}</option>)}
    </select></Field>
  </div>;
}

function ActivationFields({ showAutoRotate }: { showAutoRotate: boolean }) {
  return <>
    <Field label="生效时间" hint="留空：这个用途还没有当前证书时立即生效，否则按提前量排期，让各服务先加载、签名公钥先发布"><input name="activateAt" type="datetime-local" /></Field>
    <label className="switch-row"><input name="exportable" type="checkbox" defaultChecked /><span />允许导出私钥（之后只能关闭，不能再打开）</label>
    {showAutoRotate ? <label className="switch-row"><input name="autoRotate" type="checkbox" defaultChecked /><span />到期前自动轮换</label> : null}
  </>;
}

function GenerateForm({ catalog, onDone, onCancel }: FormProps & { catalog: Catalog }) {
  const submit = useSubmit(onDone, '证书已生成');
  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    submit.mutate(() => api(`${base}/certificates/generate`, {
      method: 'POST',
      body: { consumer: form.get('consumer'), purpose: form.get('purpose'), name: form.get('name') || null, activateAt: toIso(form.get('activateAt')), exportable: form.has('exportable'), autoRotate: form.has('autoRotate') },
    }));
  };
  return <form className="modal-form" onSubmit={onSubmit}>
    <ConsumerPurposeFields catalog={catalog} generatableOnly />
    <Field label="名称" hint="留空自动命名"><input name="name" maxLength={120} /></Field>
    <ActivationFields showAutoRotate />
    <SubmitBar busy={submit.isPending} label="生成" onCancel={onCancel} />
  </form>;
}

function ImportForm({ catalog, onDone, onCancel }: FormProps & { catalog: Catalog }) {
  const submit = useSubmit(onDone, '证书已导入');
  const [format, setFormat] = useState<'pfx' | 'pem'>('pfx');
  const [purpose, setPurpose] = useState(catalog.purposes.find((item) => item.consumers.includes(catalog.consumers[0] ?? ''))?.code ?? '');
  const isTls = purpose === 'tls';
  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    submit.mutate(async () => {
      const pfx = form.get('pfx');
      const body = {
        consumer: form.get('consumer'), purpose: form.get('purpose'), slot: form.get('slot') || null, name: form.get('name') || null, format,
        pfxBase64: format === 'pfx' && pfx instanceof File && pfx.size ? await fileToBase64(pfx) : null,
        certificatePem: format === 'pem' ? form.get('certificatePem') : null,
        privateKeyPem: format === 'pem' ? form.get('privateKeyPem') : null,
        password: form.get('password') || null,
        activateAt: toIso(form.get('activateAt')), exportable: form.has('exportable'), autoRotate: form.has('autoRotate'),
      };
      await api(`${base}/certificates/import`, { method: 'POST', body });
    });
  };
  return <form className="modal-form" onSubmit={onSubmit}>
    <ConsumerPurposeFields catalog={catalog} generatableOnly={false} onPurpose={setPurpose} />
    <div className="form-row">
      {isTls ? <Field label="站点名" hint="也是 nginx 证书目录名，例如 msa.example.com"><input name="slot" required maxLength={64} pattern="[a-z0-9][a-z0-9.\-]*" /></Field> : null}
      <Field label="名称" hint="留空自动命名"><input name="name" maxLength={120} /></Field>
    </div>
    <Field label="格式"><select value={format} onChange={(event) => setFormat(event.target.value as 'pfx' | 'pem')}><option value="pfx">PFX / P12（含私钥）</option><option value="pem">PEM（证书链 + 私钥）</option></select></Field>
    {format === 'pfx'
      ? <Field label="PFX 文件" hint="不超过 64 KB"><input name="pfx" type="file" accept=".pfx,.p12,application/x-pkcs12" required /></Field>
      : <div className="form-row">
        <Field label="证书 PEM" hint="叶子证书在前，可带中间证书（fullchain.pem）"><textarea name="certificatePem" rows={8} required placeholder="-----BEGIN CERTIFICATE-----" /></Field>
        <Field label="私钥 PEM" hint="PKCS#8 / PKCS#1 / EC；加密的私钥请填口令"><textarea name="privateKeyPem" rows={8} required placeholder="-----BEGIN PRIVATE KEY-----" /></Field>
      </div>}
    <Field label="口令" hint={format === 'pfx' ? 'PFX 文件的口令' : '私钥加密时才需要'}><input name="password" type="password" autoComplete="off" /></Field>
    <ActivationFields showAutoRotate={!isTls} />
    <SubmitBar busy={submit.isPending} label="导入" onCancel={onCancel} />
  </form>;
}

function EditForm({ certificate, onDone, onCancel }: FormProps & { certificate: Certificate }) {
  const submit = useSubmit(onDone, '设置已保存');
  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    submit.mutate(() => api(`${base}/certificates/${certificate.id}`, {
      method: 'PUT',
      body: { name: form.get('name'), autoRotate: form.has('autoRotate'), exportable: certificate.exportable && form.has('exportable') },
    }));
  };
  return <form className="modal-form" onSubmit={onSubmit}>
    <Field label="名称"><input name="name" defaultValue={certificate.name} required maxLength={120} /></Field>
    {certificate.purpose !== 'tls' ? <label className="switch-row"><input name="autoRotate" type="checkbox" defaultChecked={certificate.autoRotate} /><span />到期前自动轮换（关闭后到期需要人工处理）</label> : null}
    {certificate.exportable ? <label className="switch-row"><input name="exportable" type="checkbox" defaultChecked /><span />允许导出私钥（关闭后不能再打开）</label> : <p className="capability-note"><Icon name="key" />这张证书的私钥不可导出。</p>}
    <SubmitBar busy={submit.isPending} label="保存" onCancel={onCancel} />
  </form>;
}

function ExportForm({ certificate, onDone, onCancel }: FormProps & { certificate: Certificate }) {
  const [format, setFormat] = useState<'pfx' | 'pem'>(certificate.purpose === 'tls' ? 'pem' : 'pfx');
  const submit = useSubmit(onDone, '私钥已导出');
  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const password = String(new FormData(event.currentTarget).get('password') ?? '');
    submit.mutate(async () => download(await api<ExportedFiles>(`${base}/certificates/${certificate.id}/export-private`, { method: 'POST', body: { format, password: password || null } })));
  };
  return <form className="modal-form" onSubmit={onSubmit}>
    <Field label="格式"><select value={format} onChange={(event) => setFormat(event.target.value as 'pfx' | 'pem')}><option value="pfx">PFX（证书链 + 私钥，需口令）</option><option value="pem">PEM（fullchain.pem + key.pem）</option></select></Field>
    <Field label="口令" hint={format === 'pfx' ? '至少 8 位，导入时需要' : '留空导出明文私钥（nginx 直接可用）；填写则导出加密的 PKCS#8'}><input name="password" type="password" autoComplete="new-password" minLength={format === 'pfx' ? 8 : undefined} required={format === 'pfx'} /></Field>
    <SubmitBar busy={submit.isPending} label="导出" onCancel={onCancel} />
  </form>;
}

function SubscriptionForm({ catalog, item, onDone, onCancel }: FormProps & { catalog: Catalog; item: Subscription | null }) {
  const submit = useSubmit(onDone, item ? 'ACME 托管已更新' : 'ACME 托管已创建，下一轮检查开始申请');
  const [domains, setDomains] = useState(item?.domains.join('\n') ?? '');
  const [challenge, setChallenge] = useState<'Dns01' | 'Http01'>(item?.challengeType ?? 'Dns01');
  const hasWildcard = domains.split(/\s+/).some((domain) => domain.startsWith('*.'));
  const providers = catalog.dnsProviders;
  const directory = !item || item.directoryUrl === catalog.acmeProductionDirectory ? 'production' : item.directoryUrl === catalog.acmeStagingDirectory ? 'staging' : item.directoryUrl;
  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = {
      slot: item?.slot ?? form.get('slot'),
      domains: domains.split(/[\s,]+/).filter(Boolean),
      challengeType: challenge,
      dnsProvider: challenge === 'Dns01' ? form.get('dnsProvider') : null,
      directory: form.get('directory'),
      contactEmail: form.get('contactEmail'),
      enabled: form.has('enabled'),
    };
    submit.mutate(() => item
      ? api(`${base}/acme/subscriptions/${item.id}`, { method: 'PUT', body })
      : api(`${base}/acme/subscriptions`, { method: 'POST', body }));
  };
  return <form className="modal-form" onSubmit={onSubmit}>
    <div className="form-row">
      <Field label="站点名" hint="TLS 证书槽位，也是 nginx 证书目录名"><input name="slot" defaultValue={item?.slot} disabled={item !== null} required maxLength={64} pattern="[a-z0-9][a-z0-9.\-]*" placeholder="msa.example.com" /></Field>
      <Field label="联系邮箱" hint="CA 账户联系人"><input name="contactEmail" type="email" defaultValue={item?.contactEmail} required maxLength={200} /></Field>
    </div>
    <Field label="域名" hint="每行一个；普通域名如 shop.example.com，通配符如 *.example.com（只能用 DNS-01）"><textarea rows={4} value={domains} onChange={(event) => setDomains(event.target.value)} required placeholder={'example.com\n*.example.com'} /></Field>
    <div className="form-row">
      <Field label="验证方式"><select value={challenge} onChange={(event) => setChallenge(event.target.value as 'Dns01' | 'Http01')}>
        <option value="Dns01">DNS-01（TXT 记录，支持通配符）</option>
        <option value="Http01" disabled={hasWildcard}>HTTP-01（80 端口，仅普通域名）</option>
      </select></Field>
      {challenge === 'Dns01'
        ? <Field label="DNS 服务商" hint="凭据在证书服务的环境变量里配置"><select name="dnsProvider" defaultValue={item?.dnsProvider ?? providers.find((provider) => provider.configured)?.name} required>
          {providers.map((provider) => <option key={provider.name} value={provider.name} disabled={!provider.configured}>{provider.displayName}{provider.configured ? '' : '（未配置凭据）'}</option>)}
        </select></Field>
        : <Field label="HTTP-01 前提"><p className="capability-note">nginx 的 80 端口要把 /.well-known/acme-challenge/ 转发到证书服务（见 docker/nginx/msa.conf）。</p></Field>}
    </div>
    <Field label="CA 环境" hint="建议先在测试环境跑通，再切到正式环境（正式环境有频率限制）"><select name="directory" defaultValue={directory}>
      <option value="production">Let&apos;s Encrypt 正式环境</option>
      <option value="staging">Let&apos;s Encrypt 测试环境（证书不受浏览器信任）</option>
      {directory !== 'production' && directory !== 'staging' ? <option value={directory}>{directory}</option> : null}
    </select></Field>
    <label className="switch-row"><input name="enabled" type="checkbox" defaultChecked={item?.enabled ?? true} /><span />启用自动申请与续订</label>
    <SubmitBar busy={submit.isPending} label={item ? '保存' : '创建'} onCancel={onCancel} />
  </form>;
}
