import { Link } from 'react-router-dom';
import { Icon } from '../../shared/ui/Icon';
import { formatMoney } from '../../shared/utils/format';
import { HighlightedText } from './HighlightedText';
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
  const variantCount = product.variantCount ?? product.variants?.filter((variant) => variant.isActive).length ?? 0;
  const href = `/products/${product.id}${linkSuffix}`;

  return (
    <article className="product-card">
      <div className="product-card__media">
        <Link to={href} aria-label={`查看${product.name}详情`} tabIndex={-1}>
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
        <span className="product-card__label">{product.brandName ?? product.categoryLabel}</span>
        <h3><HighlightedText text={product.name} highlight={product.nameHighlight} /></h3>
        <span className="product-card__meta">
          <strong>{formatMoney(price.amount, product.currency)}{price.from ? <small> 起</small> : null}</strong>
          {product.brandName ? <span>{product.categoryLabel}</span> : null}
        </span>
      </Link>
    </article>
  );
}
