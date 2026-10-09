import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useLocation } from 'react-router-dom';
import { categoriesQuery } from '../features/catalog/catalog-api';
import { editorialProducts } from '../features/catalog/editorial-products';
import { ProductCard } from '../features/catalog/ProductCard';
import { ProductGridSkeleton } from '../features/catalog/ProductGridSkeleton';
import { ProductPreviewDialog } from '../features/catalog/ProductPreviewDialog';
import { toDisplayProduct } from '../features/catalog/product-view';
import { searchProducts } from '../features/catalog/search-api';
import type { DisplayProduct } from '../features/catalog/types';
import { describePromotion, describeTargets } from '../features/promotions/describe';
import { storefrontPromotionsQuery } from '../features/promotions/promotions-api';
import { HeroCarousel } from '../features/home/HeroCarousel';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';
import { Icon, type IconName } from '../shared/ui/Icon';
import { formatDateTime } from '../shared/utils/format';

const categoryIcons: IconName[] = ['sofa', 'phone', 'luggage', 'cup', 'gift', 'package'];
const demoCategories = [
  { id: 'home', name: '居家' },
  { id: 'digital', name: '数码' },
  { id: 'travel', name: '出行' },
  { id: 'life', name: '生活' },
];

export default function HomePage() {
  const location = useLocation();
  const demoMode = isDemoMode(location.search);
  const demoSuffix = demoMode ? '?demo=1' : '';
  const [previewProduct, setPreviewProduct] = useState<DisplayProduct | null>(null);
  const categories = useQuery({ ...categoriesQuery, enabled: !demoMode });
  const promotions = useQuery({ ...storefrontPromotionsQuery, enabled: !demoMode });
  const categoryNames = new Map((categories.data ?? []).map((category) => [category.id, category.name]));
  const catalogQuery = useQuery({
    // 走搜索服务：带品牌名、主图与价格区间；搜索不可用时 searchProducts 自己回退到目录列表。
    queryKey: ['search', 'featured-products'],
    queryFn: () => searchProducts({ sort: 'newest', take: 8 }),
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
    : (catalogQuery.data?.items ?? []).map((item) => {
      const display = toDisplayProduct(item, categories.data);
      return item.image ? { ...display, image: item.image } : display;
    });
  const categoryLinks = demoMode ? demoCategories : categories.data ?? [];

  return (
    <CommerceShell>
      <div className="home-stage">
        <HeroCarousel />

        {categoryLinks.length ? (
          <nav className="category-pills" id="new" aria-label="商品分类">
            {categoryLinks.slice(0, 8).map((category, index) => (
              <Link to={`/products?category=${encodeURIComponent(category.id)}${demoMode ? '&demo=1' : ''}`} key={category.id}>
                <span aria-hidden="true"><Icon name={categoryIcons[index % categoryIcons.length] ?? 'package'} /></span>
                {category.name}
              </Link>
            ))}
          </nav>
        ) : null}
      </div>

      {promotions.data?.length ? (
        <section className="promotion-strip" aria-label="正在进行的活动">
          {promotions.data.slice(0, 3).map((promotion) => (
            <Link key={promotion.id} to={`/products${demoSuffix}`} className="promotion-strip__card">
              <Icon name="gift" />
              <span>
                <strong>{describePromotion(promotion.rules, promotion.benefits)}</strong>
                <small>{promotion.name} · {describeTargets(promotion.targets, categoryNames)} · {formatDateTime(promotion.endsOnUtc)} 结束</small>
              </span>
              <Icon name="chevron-right" />
            </Link>
          ))}
        </section>
      ) : null}

      <section className="featured-products" id="featured">
        <div className="section-heading">
          <div>
            <h2><Link to={`/products?sort=newest${demoMode ? '&demo=1' : ''}`}>新品上架 <Icon name="chevron-right" /></Link></h2>
            {catalogQuery.data ? <p>共 {catalogQuery.data.total}{catalogQuery.data.totalIsLowerBound ? '+' : ''} 件在售商品</p> : null}
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
