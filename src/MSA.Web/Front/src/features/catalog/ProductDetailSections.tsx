import { useState } from 'react';
import { ProductReviews } from '../reviews/ProductReviews';
import type { ProductDetail } from './product-detail-types';

type DetailSection = 'description' | 'specifications' | 'reviews';

const sectionLabels: ReadonlyArray<{ id: DetailSection; label: string }> = [
  { id: 'description', label: '商品描述' },
  { id: 'specifications', label: '规格参数' },
  { id: 'reviews', label: '用户评价' },
];

export function ProductDetailSections({ product }: { product: ProductDetail }) {
  const [activeSection, setActiveSection] = useState<DetailSection>('description');

  const jumpTo = (section: DetailSection) => {
    setActiveSection(section);
    document.getElementById(section)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <div className="product-detail-sections">
      <nav className="product-section-nav" aria-label="商品详情导航">
        {sectionLabels.map((section) => (
          <button
            key={section.id}
            type="button"
            className={activeSection === section.id ? 'is-active' : undefined}
            onClick={() => jumpTo(section.id)}
          >
            {section.label}{section.id === 'reviews' ? `（${product.reviews.total}）` : ''}
          </button>
        ))}
      </nav>
      <section className="product-description-section" id="description">
        <div>
          <h2>商品描述</h2>
          <p>{product.longDescription}</p>
        </div>
        <div id="specifications">
          <h2>规格参数</h2>
          <dl>
            {product.specifications.map((specification) => (
              <div key={specification.label}><dt>{specification.label}</dt><dd>{specification.value}</dd></div>
            ))}
          </dl>
        </div>
      </section>
      <ProductReviews result={product.reviews} />
    </div>
  );
}
