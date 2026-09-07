import { useMemo, useState } from 'react';
import { Icon } from '../../shared/ui/Icon';
import { RatingStars } from '../catalog/RatingStars';
import type { ProductReviewResult } from './types';

type ReviewFilter = 'all' | 'with-images';
type ReviewSort = 'latest' | 'highest';

function formatReviewDate(value: string): string {
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date(value));
}

function initial(name: string): string {
  return name.trim().slice(0, 1).toLocaleUpperCase('zh-CN') || 'M';
}

export function ProductReviews({ result }: { result: ProductReviewResult }) {
  const [filter, setFilter] = useState<ReviewFilter>('all');
  const [sort, setSort] = useState<ReviewSort>('latest');
  const reviews = useMemo(() => {
    const filtered = filter === 'with-images'
      ? result.items.filter((review) => review.images.length > 0)
      : result.items;
    return [...filtered].sort((left, right) => sort === 'highest'
      ? right.rating - left.rating
      : Date.parse(right.createdOnUtc) - Date.parse(left.createdOnUtc));
  }, [filter, result.items, sort]);

  return (
    <section className="product-reviews" id="reviews">
      <div className="review-overview">
        <div className="review-score">
          <span>用户评价</span>
          <strong>{result.averageRating.toFixed(1)}</strong>
          <RatingStars rating={result.averageRating} />
          <small>{result.total} 条评价</small>
        </div>
        <div className="rating-distribution" aria-label="评分分布">
          {([5, 4, 3, 2, 1] as const).map((rating) => {
            const count = result.ratingDistribution[rating];
            const percentage = result.total ? Math.round((count / result.total) * 100) : 0;
            return (
              <div key={rating}>
                <span>{rating}星</span>
                <i><b style={{ width: `${percentage}%` }} /></i>
                <strong>{count}</strong>
              </div>
            );
          })}
        </div>
        <div className="review-controls">
          <label>
            <span className="sr-only">评价范围</span>
            <select value={filter} onChange={(event) => setFilter(event.target.value as ReviewFilter)}>
              <option value="all">全部评价</option>
              <option value="with-images">有图评价</option>
            </select>
          </label>
          <button type="button" className={filter === 'with-images' ? 'is-active' : undefined} onClick={() => setFilter((current) => current === 'with-images' ? 'all' : 'with-images')}>有图</button>
          <label>
            <span className="sr-only">评价排序</span>
            <select value={sort} onChange={(event) => setSort(event.target.value as ReviewSort)}>
              <option value="latest">最新</option>
              <option value="highest">评分最高</option>
            </select>
          </label>
        </div>
      </div>

      {reviews.length ? (
        <div className="review-list">
          {reviews.map((review) => (
            <article className="review-row" key={review.id}>
              <div className="review-author">
                <span>{initial(review.userName)}</span>
                <strong>{review.userName}</strong>
                <time dateTime={review.createdOnUtc}>{formatReviewDate(review.createdOnUtc)}</time>
              </div>
              <div className="review-body">
                <div><RatingStars rating={review.rating} /><span>已选：{review.skuLabel}</span></div>
                <p>{review.content}</p>
                {review.images.length ? (
                  <div className="review-images">
                    {review.images.map((image) => <img key={image} src={image} alt="用户晒单" width="150" height="110" loading="lazy" />)}
                  </div>
                ) : null}
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className="review-empty"><Icon name="clipboard" /><p>还没有符合筛选条件的评价。</p></div>
      )}
      <nav className="review-pagination" aria-label="评价分页"><button type="button" className="is-active" aria-current="page">1</button></nav>
    </section>
  );
}
