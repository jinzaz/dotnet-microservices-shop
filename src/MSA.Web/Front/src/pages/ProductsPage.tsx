import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { brandsQuery, categoriesQuery } from '../features/catalog/catalog-api';
import { editorialProducts } from '../features/catalog/editorial-products';
import { ProductCard } from '../features/catalog/ProductCard';
import { ProductGridSkeleton } from '../features/catalog/ProductGridSkeleton';
import { ProductPreviewDialog } from '../features/catalog/ProductPreviewDialog';
import { toDisplayProduct } from '../features/catalog/product-view';
import { searchProducts, type SearchSort } from '../features/catalog/search-api';
import type { DisplayProduct } from '../features/catalog/types';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';

const PAGE_SIZE = 12;

const sortOptions: { value: SearchSort; label: string }[] = [
  { value: 'relevance', label: '综合排序' },
  { value: 'newest', label: '新品优先' },
  { value: 'price_asc', label: '价格从低到高' },
  { value: 'price_desc', label: '价格从高到低' },
];

function readSort(value: string | null): SearchSort {
  return sortOptions.some((option) => option.value === value) ? value as SearchSort : 'relevance';
}

function readPrice(value: string | null): number | undefined {
  if (!value) return undefined;
  const price = Number(value);
  return Number.isFinite(price) && price >= 0 ? price : undefined;
}

function priceRangeLabel(min?: number, max?: number): string {
  if (min !== undefined && max !== undefined) return `¥${min} – ¥${max}`;
  return min !== undefined ? `¥${min} 以上` : `¥${max} 以下`;
}

export default function ProductsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const demoMode = isDemoMode(`?${searchParams}`);
  const search = searchParams.get('q')?.trim() ?? '';
  const categoryId = searchParams.get('category') ?? '';
  const brandId = searchParams.get('brand') ?? '';
  const sort = readSort(searchParams.get('sort'));
  const minPrice = readPrice(searchParams.get('min'));
  const maxPrice = readPrice(searchParams.get('max'));
  const [priceDraft, setPriceDraft] = useState({ min: searchParams.get('min') ?? '', max: searchParams.get('max') ?? '' });
  const [previewProduct, setPreviewProduct] = useState<DisplayProduct | null>(null);
  const loadMoreRef = useRef<HTMLDivElement>(null);
  const categories = useQuery({ ...categoriesQuery, enabled: !demoMode });
  const brands = useQuery({ ...brandsQuery, enabled: !demoMode });
  const productsQuery = useInfiniteQuery({
    queryKey: ['search', 'products', { search, categoryId, brandId, sort, minPrice, maxPrice }],
    queryFn: ({ pageParam }) => searchProducts({
      q: search,
      categoryId: categoryId || undefined,
      brandId: brandId || undefined,
      sort,
      minPrice,
      maxPrice,
      skip: pageParam,
      take: PAGE_SIZE,
    }),
    initialPageParam: 0,
    getNextPageParam: (lastPage) => {
      const next = lastPage.skip + lastPage.items.length;
      return lastPage.items.length > 0 && next < lastPage.total ? next : undefined;
    },
    enabled: !demoMode,
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  });
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = productsQuery;

  // Keep the price inputs in sync when browser history or "clear" changes the URL.
  useEffect(() => setPriceDraft({ min: searchParams.get('min') ?? '', max: searchParams.get('max') ?? '' }), [searchParams]);

  // Load the next page as the shopper nears the end of the grid; the button stays as a fallback.
  useEffect(() => {
    const sentinel = loadMoreRef.current;
    if (!sentinel || !hasNextPage) return undefined;
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting) && !isFetchingNextPage) void fetchNextPage();
    }, { rootMargin: '600px 0px' });
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [hasNextPage, isFetchingNextPage, fetchNextPage]);

  const updateParams = (next: Record<string, string | undefined>) => {
    const params = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(next)) {
      if (value) params.set(key, value);
      else params.delete(key);
    }
    setSearchParams(params, { replace: true });
  };

  const submitPrice = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    updateParams({ min: priceDraft.min.trim(), max: priceDraft.max.trim() });
  };

  const clearAll = () => setSearchParams(demoMode ? { demo: '1' } : {}, { replace: true });

  const firstPage = productsQuery.data?.pages[0];
  // The search engine fell back to the plain catalog listing: no facets, sorting or price filters.
  const advanced = firstPage?.engine !== 'database';
  const facetCounts = new Map((firstPage?.facets?.categories ?? []).map((bucket) => [bucket.id, bucket.count]));
  // 品牌很多（几十上百个），只列当前结果里命中最多的那些；选中的品牌即使不在其中也要显示，方便取消。
  const brandNames = new Map((brands.data ?? []).map((brand) => [brand.id, brand.name]));
  const brandFacets = (firstPage?.facets?.brands ?? []).filter((bucket) => brandNames.has(bucket.id));
  const selectedBrandMissing = brandId && !brandFacets.some((bucket) => bucket.id === brandId);
  const products: DisplayProduct[] = demoMode
    ? editorialProducts.filter((product) => !search || product.name.includes(search))
    : (productsQuery.data?.pages.flatMap((page) => page.items) ?? []).map((item) => {
      const display = toDisplayProduct(item, categories.data);
      return item.image ? { ...display, image: item.image } : display;
    });
  const total = demoMode ? products.length : firstPage?.total ?? 0;
  const totalLabel = `${total}${firstPage?.totalIsLowerBound ? '+' : ''}`;
  const activeCategory = categories.data?.find((category) => category.id === categoryId);
  const hasPrice = minPrice !== undefined || maxPrice !== undefined;
  const hasFilters = Boolean(search || categoryId || brandId || hasPrice);
  // 选了品牌后分类计数已按品牌收窄（见 SearchService 的 post_filter），同样值得显示。
  const showCounts = Boolean(firstPage?.facets && (search || brandId || hasPrice));

  const activeFilters = [
    search ? { key: 'q', label: `“${search}”` } : null,
    activeCategory ? { key: 'category', label: activeCategory.name } : null,
    brandId ? { key: 'brand', label: brandNames.get(brandId) ?? '已选品牌' } : null,
    hasPrice ? { key: 'price', label: priceRangeLabel(minPrice, maxPrice) } : null,
  ].filter((filter): filter is { key: string; label: string } => filter !== null);

  const removeFilter = (key: string) => updateParams(key === 'price' ? { min: '', max: '' } : { [key]: '' });

  return (
    <CommerceShell>
      <section className="catalog-page">
        <header className="catalog-page__header">
          <div>
            <h1>{search ? `搜索：${search}` : activeCategory?.name ?? '全部商品'}</h1>
            <p aria-live="polite">
              {productsQuery.isFetching && !productsQuery.isFetchingNextPage
                ? '正在更新商品…'
                : `共 ${totalLabel} 件商品${search && activeCategory ? `，在${activeCategory.name}中` : ''}`}
            </p>
          </div>
          {!demoMode && advanced ? (
            <label className="catalog-sort">
              <span className="sr-only">排序</span>
              <select value={sort} onChange={(event) => updateParams({ sort: event.target.value === 'relevance' ? '' : event.target.value })}>
                {sortOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
              <Icon name="chevron-right" />
            </label>
          ) : null}
        </header>

        <div className="catalog-filters">
          {categories.data?.length ? (
            <nav className="chip-row" aria-label="商品分类筛选">
              <button type="button" className={!categoryId ? 'is-active' : undefined} aria-pressed={!categoryId} onClick={() => updateParams({ category: '' })}>全部</button>
              {categories.data.map((category) => {
                const count = facetCounts.get(category.id) ?? 0;
                return (
                  <button
                    key={category.id}
                    type="button"
                    className={[categoryId === category.id ? 'is-active' : '', showCounts && !count ? 'is-empty' : ''].filter(Boolean).join(' ') || undefined}
                    aria-pressed={categoryId === category.id}
                    onClick={() => updateParams({ category: categoryId === category.id ? '' : category.id })}
                  >
                    {category.name}
                    {showCounts ? <small className="chip-row__count">{count}</small> : null}
                  </button>
                );
              })}
            </nav>
          ) : null}

          {!demoMode && advanced && (brandFacets.length || brandId) ? (
            <nav className="chip-row chip-row--compact" aria-label="品牌筛选">
              <span className="chip-row__label">品牌</span>
              {selectedBrandMissing ? <button type="button" className="is-active" aria-pressed onClick={() => updateParams({ brand: '' })}>{brandNames.get(brandId) ?? '已选品牌'}</button> : null}
              {brandFacets.map((bucket) => (
                <button key={bucket.id} type="button" className={brandId === bucket.id ? 'is-active' : undefined} aria-pressed={brandId === bucket.id} onClick={() => updateParams({ brand: brandId === bucket.id ? '' : bucket.id })}>
                  {brandNames.get(bucket.id)}
                  <small className="chip-row__count">{bucket.count}</small>
                </button>
              ))}
            </nav>
          ) : null}

          {!demoMode && advanced ? (
            <form className="catalog-price" onSubmit={submitPrice} aria-label="价格区间">
              <span className="chip-row__label">价格</span>
              <input
                type="number"
                inputMode="decimal"
                min={0}
                step="any"
                aria-label="最低价"
                placeholder={firstPage?.facets?.minPrice != null ? `¥${Math.floor(firstPage.facets.minPrice)}` : '最低价'}
                value={priceDraft.min}
                onChange={(event) => setPriceDraft((value) => ({ ...value, min: event.target.value }))}
              />
              <span aria-hidden="true">–</span>
              <input
                type="number"
                inputMode="decimal"
                min={0}
                step="any"
                aria-label="最高价"
                placeholder={firstPage?.facets?.maxPrice != null ? `¥${Math.ceil(firstPage.facets.maxPrice)}` : '最高价'}
                value={priceDraft.max}
                onChange={(event) => setPriceDraft((value) => ({ ...value, max: event.target.value }))}
              />
              <Button type="submit" size="small" variant="secondary">确定</Button>
            </form>
          ) : null}
        </div>

        {activeFilters.length ? (
          <div className="active-filters" aria-label="已选条件">
            {activeFilters.map((filter) => (
              <button key={filter.key} type="button" onClick={() => removeFilter(filter.key)} aria-label={`移除筛选：${filter.label}`}>
                {filter.label}
                <Icon name="close" />
              </button>
            ))}
            {activeFilters.length > 1 ? <button type="button" className="active-filters__clear" onClick={clearAll}>清除全部</button> : null}
          </div>
        ) : null}

        {!demoMode && productsQuery.isPending ? <ProductGridSkeleton count={8} /> : null}
        {!demoMode && productsQuery.isError ? (
          <div className="orders-state orders-state--error" role="alert">
            <Icon name="alert-circle" />
            <h2>商品加载失败</h2>
            <p>{getErrorMessage(productsQuery.error)}</p>
            <Button type="button" size="small" onClick={() => void productsQuery.refetch()}>重新加载</Button>
          </div>
        ) : null}

        {products.length ? (
          <div className={`product-grid${productsQuery.isPlaceholderData ? ' is-stale' : ''}`}>
            {products.map((product, index) => (
              <ProductCard key={product.id} product={product} priority={index < 4} onPreview={setPreviewProduct} linkSuffix={demoMode ? '?demo=1' : ''} />
            ))}
          </div>
        ) : null}
        {(demoMode || productsQuery.isSuccess) && !products.length ? (
          <div className="orders-empty">
            <Icon name="search" />
            <h2>{search ? `没有找到与“${search}”相关的商品` : '没有找到相关商品'}</h2>
            <p>{hasFilters ? '试试更短的关键词，或者放宽分类、品牌和价格条件。' : '商家还没有发布商品。'}</p>
            {hasFilters ? <Button type="button" variant="secondary" size="small" onClick={clearAll}>清除筛选</Button> : null}
          </div>
        ) : null}

        {hasNextPage ? (
          <div className="load-more" ref={loadMoreRef}>
            <Button type="button" variant="secondary" loading={isFetchingNextPage} onClick={() => void fetchNextPage()}>
              加载更多（已显示 {products.length} / {totalLabel}）
            </Button>
          </div>
        ) : products.length > PAGE_SIZE ? (
          <p className="load-more load-more--end">已经到底了 · 共 {products.length} 件</p>
        ) : null}
      </section>
      <ProductPreviewDialog product={previewProduct} onClose={() => setPreviewProduct(null)} />
    </CommerceShell>
  );
}
