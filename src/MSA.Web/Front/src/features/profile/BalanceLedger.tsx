import { Link } from 'react-router-dom';
import { formatDateTime, formatMoney } from '../../shared/utils/format';
import type { BalanceAccount, BalanceEntry, BalanceEntryType } from './types';

const typeLabels: Record<BalanceEntryType, string> = {
  Adjustment: '后台调整',
  Payment: '订单支付',
  Refund: '退款退回',
};

function EntryDetail({ entry }: { entry: BalanceEntry }) {
  if (entry.afterSalesRequestNumber) return <>售后单 {entry.afterSalesRequestNumber}</>;
  if (entry.orderId) return <Link to={`/orders/${entry.orderId}`}>查看订单</Link>;
  return <>{entry.reason}</>;
}

export function BalanceLedger({ balance, entries }: { balance: BalanceAccount; entries: BalanceEntry[] }) {
  return (
    <section className="account-section points-ledger" id="balance">
      <div className="account-section__heading">
        <h2>账户余额 <span>最近 10 条流水</span></h2>
        <strong className="balance-amount">{formatMoney(balance.balance, balance.currency)}</strong>
      </div>
      {entries.length ? (
        <div className="points-table-wrap">
          <table>
            <thead><tr><th>时间</th><th>类型</th><th>说明</th><th>变动</th><th>余额</th></tr></thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={entry.id}>
                  <td>{formatDateTime(entry.occurredOnUtc)}</td>
                  <td>{typeLabels[entry.type] ?? entry.type}</td>
                  <td><EntryDetail entry={entry} /></td>
                  <td className={entry.amount >= 0 ? 'points-positive' : 'points-negative'}>
                    {entry.amount >= 0 ? '+' : ''}{formatMoney(entry.amount, balance.currency)}
                  </td>
                  <td>{formatMoney(entry.balanceAfter, balance.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : <p className="account-empty">还没有余额流水。</p>}
    </section>
  );
}
