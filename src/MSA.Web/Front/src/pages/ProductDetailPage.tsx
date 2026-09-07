import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { ProductDetailSections } from '../features/catalog/ProductDetailSections';
import { getProductDetail } from '../features/catalog/product-detail-data';
import type { ProductDetail } from '../features/catalog/product-detail-types';
import { ProductGallery } from '../features/catalog/ProductGallery';
import { ProductPurchasePanel } from '../features/catalog/ProductPurchasePanel';
import { getErrorMessage } from '../shared/api/api-error';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';

function ProductDetailView({ product }: { product: ProductDetail }) {
  return (
    <>
      <div className="product-detail-page">
        <nav className="product-breadcrumb" aria-label="面包屑">
          <Link to="/">首页</Link><Icon name="chevron-right" />
          <Link to="/#featured">{product.categoryLabel}</Link><Icon name="chevron-right" />
          <span aria-current="page">{product.name}</span>
        </nav>
        <div className="product-detail-layout">
          <ProductGallery images={product.images} productName={product.name} />
          <ProductPurchasePanel product={product} />
        </div>
        {product.fallbackContracts.length ? (
          <p className="product-contract-note">当前使用前端演示数据补齐：{product.fallbackContracts.join('、')}。接口地址已预留，后端接入后无需调整页面结构。</p>
        ) : null}
        <ProductDetailSections product={product} />
      </div>
      <ServicePromises />
    </>
  );
}

export default function ProductDetailPage() {
  const { productId = '' } = useParams();
  const productQuery = useQuery({
    queryKey: ['catalog', 'product-detail', productId],
    queryFn: () => getProductDetail(productId),
    enabled: Boolean(productId),
    staleTime: 60_000,
    retry: 1,
  });

  return (
    <CommerceShell>
      {productQuery.isPending ? (
        <div className="page-state" role="status"><span className="page-spinner" />正在加载商品…</div>
      ) : null}
      {productQuery.isError ? (
        <div className="page-state page-state--error">
          <Icon name="package" />
          <h1>商品暂时无法打开</h1>
          <p>{getErrorMessage(productQuery.error)}</p>
          <Button type="button" onClick={() => void productQuery.refetch()}>重新加载</Button>
          <Link className="text-link" to="/#featured">返回首页精选 <Icon name="arrow-right" /></Link>
        </div>
      ) : null}
      {productQuery.data ? <ProductDetailView key={productQuery.data.id} product={productQuery.data} /> : null}
    </CommerceShell>
  );
}
