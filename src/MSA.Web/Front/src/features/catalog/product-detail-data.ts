import { listProductCoupons } from '../coupons/coupons-api';
import { demoCoupons } from '../coupons/demo-coupons';
import { demoProductReviews } from '../reviews/demo-reviews';
import { listProductReviews } from '../reviews/reviews-api';
import { getProduct, listProductSkus } from './catalog-api';
import { editorialProducts, productImages } from './editorial-products';
import type { ProductDetail, ProductSku } from './product-detail-types';
import type { CatalogProduct, DisplayProduct } from './types';

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

function getEditorialSkus(product: DisplayProduct): ProductSku[] {
  if (product.id === 'editorial-pour-over') {
    return [
      { sku: product.sku, price: 299, currency: product.currency, attributes: { 容量: '600ml', 滤杯: '胡桃木' }, availableQuantity: 18 },
      { sku: `${product.sku}-OAK`, price: 299, currency: product.currency, attributes: { 容量: '600ml', 滤杯: '原木' }, availableQuantity: 12 },
      { sku: `${product.sku}-800-W`, price: 339, currency: product.currency, attributes: { 容量: '800ml', 滤杯: '胡桃木' }, availableQuantity: 7 },
      { sku: `${product.sku}-800-O`, price: 329, currency: product.currency, attributes: { 容量: '800ml', 滤杯: '原木' }, availableQuantity: 9 },
    ];
  }
  if (product.id === 'editorial-charger') {
    return [
      { sku: product.sku, price: product.price, currency: product.currency, attributes: { 颜色: '深空灰' }, availableQuantity: 24 },
      { sku: `${product.sku}-S`, price: product.price, currency: product.currency, attributes: { 颜色: '银白' }, availableQuantity: 16 },
    ];
  }
  if (product.id === 'editorial-backpack') {
    return [
      { sku: product.sku, price: product.price, currency: product.currency, attributes: { 颜色: '沙岩白' }, availableQuantity: 11 },
      { sku: `${product.sku}-B`, price: product.price, currency: product.currency, attributes: { 颜色: '雾蓝' }, availableQuantity: 8 },
    ];
  }
  return [
    { sku: product.sku, price: product.price, currency: product.currency, attributes: { 规格: '2 支装' }, availableQuantity: 36 },
    { sku: `${product.sku}-4`, price: 69, currency: product.currency, attributes: { 规格: '4 支装' }, availableQuantity: 22 },
  ];
}

function fallbackSku(product: CatalogProduct): ProductSku[] {
  return [{
    sku: product.sku,
    price: product.price,
    currency: product.currency,
    attributes: { 规格: '标准款' },
  }];
}

function buildEditorialDetail(product: DisplayProduct): ProductDetail {
  const isPourOver = product.id === 'editorial-pour-over';
  return {
    ...product,
    images: detailImages[product.id] ?? [product.image],
    longDescription: isPourOver
      ? '套装包含玻璃分享壶、通透滤杯、木环、细口壶与量勺。高硼硅玻璃耐热通透，搭配温润木环与细致工艺，带来稳定顺畅的冲煮体验。'
      : `${product.description} 经过日常使用场景筛选，兼顾耐用、易维护与舒适体验。`,
    specifications: isPourOver
      ? [
          { label: '主要材质', value: '高硼硅玻璃、北美黑胡桃木、304 不锈钢' },
          { label: '容量选择', value: '600ml / 800ml' },
          { label: '适用范围', value: '1–4 人份手冲咖啡' },
          { label: '清洁方式', value: '玻璃件可水洗，木环请干布擦拭' },
        ]
      : [
          { label: '商品分类', value: product.categoryLabel },
          { label: '商品编号', value: product.sku },
          { label: '售后服务', value: '7 天无理由退换' },
        ],
    skus: getEditorialSkus(product),
    coupons: demoCoupons.filter((coupon) => coupon.status === 'available').slice(0, 1),
    reviews: demoProductReviews,
    fallbackContracts: ['商品 SKU 列表', '用户可领优惠券', '商品评价'],
  };
}

function productImageFor(id: string): string {
  let hash = 0;
  for (const character of id) hash = (hash + character.charCodeAt(0)) % productImages.length;
  return productImages[hash] ?? productImages[0];
}

export function findEditorialProduct(productId: string): DisplayProduct | undefined {
  return editorialProducts.find((product) => product.id === productId);
}

export async function getProductDetail(productId: string): Promise<ProductDetail> {
  const editorialProduct = findEditorialProduct(productId);
  if (editorialProduct) return buildEditorialDetail(editorialProduct);

  const product = await getProduct(productId);
  const [skusResult, couponsResult, reviewsResult] = await Promise.allSettled([
    listProductSkus(product.id),
    listProductCoupons(product.id),
    listProductReviews(product.id),
  ]);
  const image = productImageFor(product.id);
  const fallbackContracts: string[] = [];
  if (skusResult.status === 'rejected') fallbackContracts.push('商品 SKU 列表');
  if (couponsResult.status === 'rejected') fallbackContracts.push('用户可领优惠券');
  if (reviewsResult.status === 'rejected') fallbackContracts.push('商品评价');

  return {
    ...product,
    categoryLabel: '精选商品',
    image,
    images: [image],
    longDescription: product.description,
    specifications: [
      { label: '商品编号', value: product.sku },
      { label: '商品状态', value: product.lifecycleStatus ?? (product.isActive ? '在售' : '暂停售卖') },
      { label: '售后服务', value: '7 天无理由退换' },
    ],
    skus: skusResult.status === 'fulfilled' && skusResult.value.length
      ? skusResult.value
      : fallbackSku(product),
    coupons: couponsResult.status === 'fulfilled'
      ? couponsResult.value
      : demoCoupons.filter((coupon) => coupon.status === 'available').slice(0, 1),
    reviews: reviewsResult.status === 'fulfilled' ? reviewsResult.value : demoProductReviews,
    fallbackContracts,
  };
}
