import { Icon } from '../../shared/ui/Icon';
import { formatMoney } from '../../shared/utils/format';
import type { DisplayProduct } from './types';

type ProductCardProps = {
  product: DisplayProduct;
  priority?: boolean;
  onPreview: (product: DisplayProduct) => void;
};

export function ProductCard({ product, priority = false, onPreview }: ProductCardProps) {
  return (
    <article className="product-card">
      <div className="product-card__media">
        <img
          src={product.image}
          alt={product.name}
          width="720"
          height="720"
          loading={priority ? 'eager' : 'lazy'}
        />
        <button className="icon-button product-card__favorite" type="button" aria-label={`收藏${product.name}`}>
          <Icon name="heart" />
        </button>
        <button className="product-card__preview" type="button" onClick={() => onPreview(product)}>
          <Icon name="eye" />
          快速查看
        </button>
      </div>
      <button className="product-card__body" type="button" onClick={() => onPreview(product)}>
        <h3>{product.name}</h3>
        <span className="product-card__meta">
          <span>{product.categoryLabel}</span>
          <strong>{formatMoney(product.price, product.currency)}</strong>
        </span>
      </button>
    </article>
  );
}
