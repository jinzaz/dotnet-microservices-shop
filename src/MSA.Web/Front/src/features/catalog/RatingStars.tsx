import { Icon } from '../../shared/ui/Icon';

const STAR_POSITIONS = [1, 2, 3, 4, 5] as const;

export function RatingStars({ rating, label }: { rating: number; label?: string }) {
  return (
    <span className="rating-stars" aria-label={label ?? `评分 ${rating.toFixed(1)} 分，满分 5 分`}>
      {STAR_POSITIONS.map((position) => (
        <Icon key={position} name="star" className={position <= Math.round(rating) ? 'is-filled' : undefined} />
      ))}
    </span>
  );
}
