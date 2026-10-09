import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { brandsQuery, categoriesQuery } from '../features/catalog/catalog-api';
import { editorialProducts } from '../features/catalog/editorial-products';
import { ProductCard } from '../features/catalog/ProductCard';
import { ProductPreviewDialog } from '../features/catalog/ProductPreviewDialog';
import { toDisplayProduct } from '../features/catalog/product-view';
import { searchProducts, suggestProducts, type SearchSort } from '../features/catalog/search-api';
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

/** Waits until typing pauses so autocomplete does not fire a request per keystroke. */
function useDebounced<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delay);
    return () => window.clearTimeout(timer);
  }, [value, delay]);
  return debounced;
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
  const [draft, setDraft] = useState(search);
  const [priceDraft, setPriceDraft] = useState({ min: searchParams.get('min') ?? '', max: searchParams.get('max') ?? '' });
  const [previewProduct, setPreviewProduct] = useState<DisplayProduct | null>(null);
  const categories = useQuery({ ...categoriesQuery, enabled: !demoMode });
  const brands = useQuery({ ...brandsQuery, enabled: !demoMode });
  const prefix = useDebounced(draft.trim(), 200);
  const suggestions = useQuery({
    queryKey: ['search', 'suggestions', prefix],
    queryFn: () => suggestProducts(prefix),
    enabled: !demoMode && prefix.length > 0 && prefix !== search,
    staleTime: 60_000,
  });
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

  // Keep the inputs in sync when the header search or browser history changes the query.
  useEffect(() => setDraft(search), [search]);
  useEffect(() => setPriceDraft({ min: searchParams.get('min') ?? '', max: searchParams.get('max') ?? '' }), [searchParams]);

  const updateParams = (next: Record<string, string | undefined>) => {
    const params = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(next)) {
      if (value) params.set(key, value);
      else params.delete(key);
    }
    setSearchParams(params, { replace: true });
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    updateParams({ q: draft.trim() });
  };

  const submitPrice = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    updateParams({ min: priceDraft.min.trim(), max: priceDraft.max.trim() });
  };

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
  const hasFilters = Boolean(search || categoryId || brandId || minPrice !== undefined || maxPrice !== undefined);
  const showCounts = Boolean(firstPage?.facets && (search || minPrice !== undefined || maxPrice !== undefined));

  return (
    <CommerceShell>
      <section className="catalog-page">
        <header className="catalog-page__header">
          <div>
            <h1>{activeCategory?.name ?? (search ? '搜索结果' : '全部商品')}</h1>
            <p>{productsQuery.isFetching && !productsQuery.isFetchingNextPage ? '正在更新商品…' : `共 ${totalLabel} 件商品${search ? `，与“${search}”相关` : ''}`}</p>
          </div>
          <form className="catalog-search" role="search" onSubmit={submit}>
            <Icon name="search" />
            <label className="sr-only" htmlFor="catalog-search">搜索商品</label>
            <input
              id="catalog-search"
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              placeholder="商品名称、品类或 SKU"
              maxLength={80}
              list="catalog-search-suggestions"
              autoComplete="off"
            />
            <datalist id="catalog-search-suggestions">
              {(suggestions.data ?? []).map((suggestion) => <option key={suggestion} value={suggestion} />)}
            </datalist>
            {draft ? <button type="button" className="catalog-search__clear" aria-label="清空搜索" onClick={() => { setDraft(''); updateParams({ q: '' }); }}><Icon name="close" /></button> : null}
            <button type="submit">搜索</button>
          </form>
        </header>

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
                  onClick={() => updateParams({ category: category.id })}
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
            <button type="button" className={!brandId ? 'is-active' : undefined} aria-pressed={!brandId} onClick={() => updateParams({ brand: '' })}>全部品牌</button>
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
          <div className="catalog-toolbar">
            <label className="catalog-toolbar__sort">
              <span className="sr-only">排序</span>
              <select value={sort} onChange={(event) => updateParams({ sort: event.target.value === 'relevance' ? '' : event.target.value })}>
                {sortOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
            </label>
            <form className="catalog-toolbar__price" onSubmit={submitPrice} aria-label="价格区间">
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
          </div>
        ) : null}

        {!demoMode && productsQuery.isPending ? (
          <div className="orders-state" role="status"><span className="page-spinner" />正在加载商品…</div>
        ) : null}
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
            <h2>没有找到相关商品</h2>
            <p>{hasFilters ? '换个关键词、分类或价格区间试试。' : '商家还没有发布商品。'}</p>
            {hasFilters ? <Button type="button" variant="secondary" size="small" onClick={() => setSearchParams({}, { replace: true })}>清除筛选</Button> : null}
          </div>
        ) : null}

        {productsQuery.hasNextPage ? (
          <div className="load-more">
            <Button type="button" variant="secondary" loading={productsQuery.isFetchingNextPage} onClick={() => void productsQuery.fetchNextPage()}>
              加载更多（已显示 {products.length} / {totalLabel}）
            </Button>
          </div>
        ) : null}
      </section>
      <ProductPreviewDialog product={previewProduct} onClose={() => setPreviewProduct(null)} />
    </CommerceShell>
  );
}
