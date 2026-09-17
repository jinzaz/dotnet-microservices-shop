import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useLocation } from 'react-router-dom';
import { categoriesQuery, listProducts } from '../features/catalog/catalog-api';
import { editorialProducts } from '../features/catalog/editorial-products';
import { ProductCard } from '../features/catalog/ProductCard';
import { ProductPreviewDialog } from '../features/catalog/ProductPreviewDialog';
import { toDisplayProduct } from '../features/catalog/product-view';
import type { DisplayProduct } from '../features/catalog/types';
import { HeroCarousel } from '../features/home/HeroCarousel';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';
import { Icon, type IconName } from '../shared/ui/Icon';

const categoryIcons: IconName[] = ['sofa', 'phone', 'luggage', 'cup', 'gift', 'package'];
const demoCategories = [
  { id: 'home', name: '居家' },
  { id: 'digital', name: '数码' },
  { id: 'travel', name: '出行' },
  { id: 'life', name: '生活' },
];

function ProductGridSkeleton({ count }: { count: number }) {
  return (
    <div className="product-grid" aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <div className="product-card product-card--skeleton" key={index}>
          <div className="skeleton skeleton--media" />
          <div className="skeleton skeleton--line" />
          <div className="skeleton skeleton--line skeleton--short" />
        </div>
      ))}
    </div>
  );
}

export default function HomePage() {
  const location = useLocation();
  const demoMode = isDemoMode(location.search);
  const demoSuffix = demoMode ? '?demo=1' : '';
  const [previewProduct, setPreviewProduct] = useState<DisplayProduct | null>(null);
  const categories = useQuery({ ...categoriesQuery, enabled: !demoMode });
  const catalogQuery = useQuery({
    queryKey: ['catalog', 'featured-products'],
    queryFn: () => listProducts({ take: 8 }),
    enabled: !demoMode,
    staleTime: 60_000,
  });

  useEffect(() => {
    if (!location.hash) return;
    requestAnimationFrame(() => {
      document.querySelector(location.hash)?.scrollIntoView({ behavior: 'smooth' });
    });
  }, [location.hash]);

  const products = demoMode
    ? editorialProducts
    : (catalogQuery.data?.items ?? []).map((product) => toDisplayProduct(product, categories.data));
  const categoryLinks = demoMode ? demoCategories : categories.data ?? [];

  return (
    <CommerceShell>
      <HeroCarousel />

      {categoryLinks.length ? (
        <nav className="category-rail" id="new" aria-label="商品分类">
          <div className="category-rail__inner">
            {categoryLinks.slice(0, 6).map((category, index) => (
              <Link to={`/products?category=${encodeURIComponent(category.id)}${demoMode ? '&demo=1' : ''}`} key={category.id}>
                <Icon name={categoryIcons[index % categoryIcons.length] ?? 'package'} />
                <span>{category.name}</span>
              </Link>
            ))}
          </div>
        </nav>
      ) : null}

      <section className="featured-products" id="featured">
        <div className="section-heading">
          <div>
            <h2>本周精选</h2>
            {catalogQuery.data ? <p>共 {catalogQuery.data.total} 件在售商品</p> : null}
          </div>
          <Link className="text-link" to={`/products${demoSuffix}`}>
            查看全部 <Icon name="arrow-right" />
          </Link>
        </div>

        {!demoMode && catalogQuery.isPending ? <ProductGridSkeleton count={4} /> : null}
        {!demoMode && catalogQuery.isError ? (
          <div className="search-empty" role="alert">
            <Icon name="alert-circle" />
            <h3>商品暂时无法加载</h3>
            <p>{getErrorMessage(catalogQuery.error)}</p>
            <Button type="button" variant="secondary" loading={catalogQuery.isFetching} onClick={() => void catalogQuery.refetch()}>重新加载</Button>
          </div>
        ) : null}
        {products.length ? (
          <div className="product-grid">
            {products.map((product, index) => (
              <ProductCard key={product.id} product={product} priority={index < 2} onPreview={setPreviewProduct} linkSuffix={demoSuffix} />
            ))}
          </div>
        ) : null}
        {!demoMode && catalogQuery.isSuccess && !products.length ? (
          <div className="search-empty">
            <Icon name="package" />
            <h3>新品正在上架</h3>
            <p>商家还没有发布商品，稍后再来看看吧。</p>
          </div>
        ) : null}
      </section>

      <ServicePromises />
      <ProductPreviewDialog product={previewProduct} onClose={() => setPreviewProduct(null)} />
    </CommerceShell>
  );
}
