import { Icon, type IconName } from '../../shared/ui/Icon';
import { formatPoints } from '../../shared/utils/format';
import type { LoyaltyPointAccount } from './types';

export function PointsOverview({ points }: { points: LoyaltyPointAccount }) {
  const items: ReadonlyArray<{ label: string; value?: number; icon: IconName }> = [
    { label: '可用积分', value: points.balance, icon: 'coins' },
    { label: '累计获得', value: points.lifetimeEarned, icon: 'calendar' },
    { label: '累计使用', value: points.lifetimeSpent, icon: 'gift' },
  ];

  return (
    <section className="points-overview" aria-label="积分概览">
      {items.map((item) => (
        <div className="points-overview__item" key={item.label}>
          <Icon name={item.icon} />
          <div>
            <span>{item.label}</span>
            <strong>{formatPoints(item.value ?? 0)}</strong>
          </div>
        </div>
      ))}
      <a className="points-overview__link" href="#points">
        <Icon name="receipt" />
        <span>积分明细</span>
        <Icon name="chevron-right" />
      </a>
    </section>
  );
}
