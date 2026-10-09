import { appliesToProduct } from '../promotions/describe';
import { listStorefrontPromotions } from '../promotions/promotions-api';
import type { StorefrontPromotion } from '../promotions/types';
import { demoProductReviews } from '../reviews/demo-reviews';
import type { ProductReviewResult } from '../reviews/types';
import { getProduct, listBrands, listCategories } from './catalog-api';
import { editorialProducts } from './editorial-products';
import type { ProductDetail, ProductSku } from './product-detail-types';
import { productImagesOf, productSkusOf, toDisplayProduct } from './product-view';
import type { DisplayProduct } from './types';

const detailImages: Record<string, string[]> = {
  'editorial-pour-over': [
    '/images/product-pour-over-bundle.png',
    '/images/product-pour-over.jpg',
    '/images/product-pour-over-detail.png',
    '/images/product-pour-over-package.png',
  ],
  'editorial-charger': ['/images/product-charger.jpg'],
  'editorial-backpack': ['/images/product-backpack.jpg'],
  'editorial-toothbrush': ['/images/product-toothbrush.jpg'],
};

// Editorial (demo) products preview how a storefront promotion is presented.
const demoPromotions: StorefrontPromotion[] = [{
  id: 'demo-promo-300',
  name: '秋季焕新 · 满 300 减 30',
  startsOnUtc: '2026-09-01T00:00:00Z',
  endsOnUtc: '2026-10-07T15:59:59Z',
  priority: 10,
  stackPolicy: 'Stackable',
  rules: [{ ruleType: 'MinimumOrderAmount', schemaVersion: 1, configurationJson: '{"amount":300}' }],
  benefits: [{ benefitType: 'FixedAmountOff', schemaVersion: 1, configurationJson: '{"amount":30}' }],
  targets: [{ targetType: 'All', targetKey: null, isExcluded: false }],
}];

// The review service is not deployed yet; products show an empty state until it is.
export const emptyReviews: ProductReviewResult = {
  items: [],
  total: 0,
  averageRating: 0,
  ratingDistribution: { 5: 0, 4: 0, 3: 0, 2: 0, 1: 0 },
};

function getEditorialSkus(product: DisplayProduct): ProductSku[] {
  if (product.id === 'editorial-pour-over') {
    return [
      { sku: product.sku, price: 299, currency: product.currency, attributes: { 容量: '600ml', 滤杯: '胡桃木' }, availableQuantity: 18 },
      { sku: `${product.sku}-OAK`, price: 299, currency: product.currency, attributes: { 容量: '600ml', 滤杯: '原木' }, availableQuantity: 12 },
      { sku: `${product.sku}-800-W`, price: 339, currency: product.currency, attributes: { 容量: '800ml', 滤杯: '胡桃木' }, availableQuantity: 7 },
      { sku: `${product.sku}-800-O`, price: 329, currency: product.currency, attributes: { 容量: '800ml', 滤杯: '原木' }, availableQuantity: 0 },
    ];
  }
  return [
    { sku: product.sku, price: product.price, currency: product.currency, attributes: { 规格: '标准款' }, availableQuantity: 24 },
    { sku: `${product.sku}-PLUS`, price: product.price + 30, currency: product.currency, attributes: { 规格: '升级款' }, availableQuantity: 9 },
  ];
}

function buildEditorialDetail(product: DisplayProduct): ProductDetail {
  return {
    ...product,
    images: detailImages[product.id] ?? [product.image],
    longDescription: `${product.description} 经过日常使用场景筛选，兼顾耐用、易维护与舒适体验。`,
    specifications: [
      { label: '商品分类', value: product.categoryLabel },
      { label: '商品编号', value: product.sku },
      { label: '售后服务', value: '签收后 7 天内可申请退换' },
    ],
    skus: getEditorialSkus(product),
    promotions: demoPromotions,
    reviews: demoProductReviews,
  };
}

export function findEditorialProduct(productId: string): DisplayProduct | undefined {
  return editorialProducts.find((product) => product.id === productId);
}

export async function getProductDetail(productId: string): Promise<ProductDetail> {
  const editorialProduct = findEditorialProduct(productId);
  if (editorialProduct) return buildEditorialDetail(editorialProduct);

  const [product, categories, brands, promotions] = await Promise.all([
    getProduct(productId),
    listCategories().catch(() => []),
    // 品牌只是展示信息，读不到时不显示这一行即可。
    listBrands().catch(() => []),
    // Promotions only decorate the page; a failure must not hide the product.
    listStorefrontPromotions().catch(() => [] as StorefrontPromotion[]),
  ]);
  const display = toDisplayProduct(product, categories);
  const brandName = product.brandId ? brands.find((brand) => brand.id === product.brandId)?.name : undefined;
  const skus = productSkusOf(product);
  const priceRange = skus.length > 1
    ? `${Math.min(...skus.map((sku) => sku.price))} – ${Math.max(...skus.map((sku) => sku.price))} ${product.currency}`
    : null;

  return {
    ...display,
    brandLabel: brandName,
    images: productImagesOf(product),
    longDescription: product.description || '商家暂未填写详细描述。',
    specifications: [
      { label: '商品分类', value: display.categoryLabel },
      ...(brandName ? [{ label: '品牌', value: brandName }] : []),
      { label: '可选规格', value: `${skus.length} 款` },
      ...(priceRange ? [{ label: '价格区间', value: priceRange }] : []),
      { label: '售后服务', value: '签收后 7 天内可申请退换' },
    ],
    skus,
    promotions: promotions.filter((promotion) => appliesToProduct(promotion.targets, {
      id: product.id,
      categoryId: product.categoryId,
      skus: skus.map((sku) => sku.sku),
    })),
    reviews: emptyReviews,
  };
}
