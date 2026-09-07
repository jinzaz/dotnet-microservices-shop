import { Link } from 'react-router-dom';
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
        <Link to={`/products/${product.id}`} aria-label={`查看${product.name}详情`}>
          <img
            src={product.image}
            alt={product.name}
            width="720"
            height="720"
            loading={priority ? 'eager' : 'lazy'}
          />
        </Link>
        <button className="icon-button product-card__favorite" type="button" aria-label={`收藏${product.name}`}>
          <Icon name="heart" />
        </button>
        <button className="product-card__preview" type="button" onClick={() => onPreview(product)}>
          <Icon name="eye" />
          快速查看
        </button>
      </div>
      <Link className="product-card__body" to={`/products/${product.id}`}>
        <h3>{product.name}</h3>
        <span className="product-card__meta">
          <span>{product.categoryLabel}</span>
          <strong>{formatMoney(product.price, product.currency)}</strong>
        </span>
      </Link>
    </article>
  );
}
