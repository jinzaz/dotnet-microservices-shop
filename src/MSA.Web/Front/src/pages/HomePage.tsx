import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { listProducts } from '../features/catalog/catalog-api';
import { editorialProducts, productImages } from '../features/catalog/editorial-products';
import { ProductCard } from '../features/catalog/ProductCard';
import { ProductPreviewDialog } from '../features/catalog/ProductPreviewDialog';
import type { DisplayProduct } from '../features/catalog/types';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Icon, type IconName } from '../shared/ui/Icon';

const categories: ReadonlyArray<{ label: string; icon: IconName; anchor: string }> = [
  { label: '居家', icon: 'sofa', anchor: 'featured' },
  { label: '数码', icon: 'phone', anchor: 'featured' },
  { label: '出行', icon: 'luggage', anchor: 'featured' },
  { label: '生活', icon: 'cup', anchor: 'featured' },
];

function enrichProducts(products: Awaited<ReturnType<typeof listProducts>>): DisplayProduct[] {
  return products.slice(0, 4).map((product, index) => ({
    ...product,
    categoryLabel: editorialProducts[index]?.categoryLabel ?? '精选',
    image: productImages[index % productImages.length] ?? productImages[0],
  }));
}

export default function HomePage() {
  const { isAuthenticated } = useAuth();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const [previewProduct, setPreviewProduct] = useState<DisplayProduct | null>(null);
  const catalogQuery = useQuery({
    queryKey: ['catalog', 'featured-products'],
    queryFn: () => listProducts(4),
    enabled: isAuthenticated,
    staleTime: 60_000,
    retry: 1,
  });

  useEffect(() => {
    if (!location.hash) return;
    requestAnimationFrame(() => {
      document.querySelector(location.hash)?.scrollIntoView({ behavior: 'smooth' });
    });
  }, [location.hash]);

  const sourceProducts = catalogQuery.data?.length
    ? enrichProducts(catalogQuery.data)
    : editorialProducts;
  const query = searchParams.get('q')?.trim().toLocaleLowerCase('zh-CN') ?? '';
  const visibleProducts = query
    ? sourceProducts.filter((product) =>
        `${product.name} ${product.description} ${product.categoryLabel}`
          .toLocaleLowerCase('zh-CN')
          .includes(query),
      )
    : sourceProducts;

  return (
    <CommerceShell>
      <section className="home-hero">
        <div className="home-hero__copy">
          <h1>把日常，选得更好</h1>
          <p>从质感家居到随身好物，认真挑选每一件值得长期使用的东西。</p>
          <div className="home-hero__actions">
            <a className="button button--primary button--regular" href="#featured">浏览精选</a>
            <a className="text-link" href="#new">查看新品 <Icon name="arrow-right" /></a>
          </div>
        </div>
        <div className="home-hero__media">
          <img
            src="/images/hero-lifestyle.jpg"
            alt="黑色手冲壶、陶瓷杯与绿植组成的安静居家一角"
            width="1672"
            height="941"
            fetchPriority="high"
          />
        </div>
      </section>

      <nav className="category-rail" id="new" aria-label="商品分类">
        <div className="category-rail__inner">
          {categories.map((category) => (
            <a href={`#${category.anchor}`} key={category.label}>
              <Icon name={category.icon} />
              <span>{category.label}</span>
            </a>
          ))}
        </div>
      </nav>

      <section className="featured-products" id="featured">
        <div className="section-heading">
          <div>
            <h2>{query ? `“${searchParams.get('q')}” 的搜索结果` : '本周精选'}</h2>
            {catalogQuery.isError ? (
              <p role="status">商品服务暂时不可用，当前展示编辑精选内容。</p>
            ) : null}
          </div>
          <Link className="text-link" to={isAuthenticated ? '/products' : '/login?redirect=%2Fproducts'}>
            查看全部 <Icon name="arrow-right" />
          </Link>
        </div>

        {visibleProducts.length ? (
          <div className="product-grid">
            {visibleProducts.map((product, index) => (
              <ProductCard
                key={product.id}
                product={product}
                priority={index < 2}
                onPreview={setPreviewProduct}
              />
            ))}
          </div>
        ) : (
          <div className="search-empty">
            <Icon name="search" />
            <h3>没有找到相关商品</h3>
            <p>换一个关键词，或者浏览本周精选。</p>
            <Link className="button button--secondary button--regular" to="/#featured">清除搜索</Link>
          </div>
        )}
      </section>

      <ServicePromises />
      <ProductPreviewDialog product={previewProduct} onClose={() => setPreviewProduct(null)} />
    </CommerceShell>
  );
}
