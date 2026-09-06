import { formatDateTime, formatPoints } from '../../shared/utils/format';
import type { LoyaltyPointEntry } from './types';

export function PointsLedger({ entries }: { entries: LoyaltyPointEntry[] }) {
  return (
    <section className="account-section points-ledger" id="points">
      <div className="account-section__heading">
        <h2>积分明细 <span>最近 5 条</span></h2>
      </div>
      {entries.length ? (
        <div className="points-table-wrap">
          <table>
            <thead><tr><th>时间</th><th>来源</th><th>说明</th><th>变动</th><th>余额</th></tr></thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={entry.id}>
                  <td>{formatDateTime(entry.occurredOnUtc)}</td>
                  <td>{entry.source}</td>
                  <td>{entry.reason}</td>
                  <td className={entry.delta >= 0 ? 'points-positive' : 'points-negative'}>
                    {entry.delta >= 0 ? '+' : ''}{formatPoints(entry.delta)}
                  </td>
                  <td>{formatPoints(entry.balanceAfter)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : <p className="account-empty">还没有积分明细。</p>}
    </section>
  );
}
