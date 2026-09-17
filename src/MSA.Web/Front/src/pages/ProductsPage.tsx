import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { categoriesQuery, listProducts } from '../features/catalog/catalog-api';
import { editorialProducts } from '../features/catalog/editorial-products';
import { ProductCard } from '../features/catalog/ProductCard';
import { ProductPreviewDialog } from '../features/catalog/ProductPreviewDialog';
import { toDisplayProduct } from '../features/catalog/product-view';
import type { DisplayProduct } from '../features/catalog/types';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';

const PAGE_SIZE = 12;

export default function ProductsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const demoMode = isDemoMode(`?${searchParams}`);
  const search = searchParams.get('q')?.trim() ?? '';
  const categoryId = searchParams.get('category') ?? '';
  const [draft, setDraft] = useState(search);
  const [previewProduct, setPreviewProduct] = useState<DisplayProduct | null>(null);
  const categories = useQuery({ ...categoriesQuery, enabled: !demoMode });
  const productsQuery = useInfiniteQuery({
    queryKey: ['catalog', 'products', { search, categoryId }],
    queryFn: ({ pageParam }) => listProducts({ skip: pageParam, take: PAGE_SIZE, search, categoryId: categoryId || undefined }),
    initialPageParam: 0,
    getNextPageParam: (lastPage) => lastPage.skip + lastPage.items.length < lastPage.total ? lastPage.skip + lastPage.items.length : undefined,
    enabled: !demoMode,
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  });

  // Keep the input in sync when the header search or browser history changes the query.
  useEffect(() => setDraft(search), [search]);

  const updateParams = (next: { q?: string; category?: string }) => {
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

  const products = demoMode
    ? editorialProducts.filter((product) => !search || product.name.includes(search))
    : (productsQuery.data?.pages.flatMap((page) => page.items) ?? []).map((product) => toDisplayProduct(product, categories.data));
  const total = demoMode ? products.length : productsQuery.data?.pages[0]?.total ?? 0;
  const activeCategory = categories.data?.find((category) => category.id === categoryId);
  const hasFilters = Boolean(search || categoryId);

  return (
    <CommerceShell>
      <section className="catalog-page">
        <header className="catalog-page__header">
          <div>
            <h1>{activeCategory?.name ?? (search ? '搜索结果' : '全部商品')}</h1>
            <p>{productsQuery.isFetching && !productsQuery.isFetchingNextPage ? '正在更新商品…' : `共 ${total} 件商品${search ? `，与“${search}”相关` : ''}`}</p>
          </div>
          <form className="catalog-search" role="search" onSubmit={submit}>
            <Icon name="search" />
            <label className="sr-only" htmlFor="catalog-search">搜索商品</label>
            <input id="catalog-search" value={draft} onChange={(event) => setDraft(event.target.value)} placeholder="商品名称或 SKU" maxLength={80} />
            {draft ? <button type="button" className="catalog-search__clear" aria-label="清空搜索" onClick={() => { setDraft(''); updateParams({ q: '' }); }}><Icon name="close" /></button> : null}
            <button type="submit">搜索</button>
          </form>
        </header>

        {categories.data?.length ? (
          <nav className="chip-row" aria-label="商品分类筛选">
            <button type="button" className={!categoryId ? 'is-active' : undefined} aria-pressed={!categoryId} onClick={() => updateParams({ category: '' })}>全部</button>
            {categories.data.map((category) => (
              <button
                key={category.id}
                type="button"
                className={categoryId === category.id ? 'is-active' : undefined}
                aria-pressed={categoryId === category.id}
                onClick={() => updateParams({ category: category.id })}
              >
                {category.name}
              </button>
            ))}
          </nav>
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
            <p>{hasFilters ? '换个关键词或分类试试。' : '商家还没有发布商品。'}</p>
            {hasFilters ? <Button type="button" variant="secondary" size="small" onClick={() => setSearchParams({}, { replace: true })}>清除筛选</Button> : null}
          </div>
        ) : null}

        {productsQuery.hasNextPage ? (
          <div className="load-more">
            <Button type="button" variant="secondary" loading={productsQuery.isFetchingNextPage} onClick={() => void productsQuery.fetchNextPage()}>
              加载更多（已显示 {products.length} / {total}）
            </Button>
          </div>
        ) : null}
      </section>
      <ProductPreviewDialog product={previewProduct} onClose={() => setPreviewProduct(null)} />
    </CommerceShell>
  );
}
