import { Link } from 'react-router-dom';
import { Icon } from '../../shared/ui/Icon';
import { formatMoney } from '../../shared/utils/format';
import { priceLabel } from './product-view';
import type { DisplayProduct } from './types';

type ProductCardProps = {
  product: DisplayProduct;
  priority?: boolean;
  onPreview: (product: DisplayProduct) => void;
  linkSuffix?: string;
};

export function ProductCard({ product, priority = false, onPreview, linkSuffix = '' }: ProductCardProps) {
  const price = priceLabel(product);
  const variantCount = product.variants?.filter((variant) => variant.isActive).length ?? 0;
  const href = `/products/${product.id}${linkSuffix}`;

  return (
    <article className="product-card">
      <div className="product-card__media">
        <Link to={href} aria-label={`查看${product.name}详情`}>
          <img
            src={product.image}
            alt={product.name}
            width="720"
            height="720"
            loading={priority ? 'eager' : 'lazy'}
          />
        </Link>
        {variantCount > 1 ? <span className="product-card__badge">{variantCount} 款可选</span> : null}
        <button className="product-card__preview" type="button" onClick={() => onPreview(product)}>
          <Icon name="eye" />
          快速查看
        </button>
      </div>
      <Link className="product-card__body" to={href}>
        <h3>{product.name}</h3>
        <span className="product-card__meta">
          <span>{product.categoryLabel}</span>
          <strong>{formatMoney(price.amount, product.currency)}{price.from ? <small> 起</small> : null}</strong>
        </span>
      </Link>
    </article>
  );
}
