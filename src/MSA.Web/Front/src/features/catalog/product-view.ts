import { productImages } from './editorial-products';
import type { ProductSku } from './product-detail-types';
import type { CatalogCategory, CatalogProduct, DisplayProduct } from './types';

const imageCacheKey = 'msa-store:product-images:v1';

/** Deterministic placeholder so products without media keep a stable look between visits. */
export function placeholderImageFor(id: string): string {
  let hash = 0;
  for (const character of id) hash = (hash * 31 + character.charCodeAt(0)) % 9973;
  return productImages[hash % productImages.length] ?? productImages[0];
}

export function productImagesOf(product: CatalogProduct): string[] {
  const images = (product.media ?? [])
    .filter((media) => media.mediaType.toLowerCase() === 'image')
    .sort((left, right) => left.sortOrder - right.sortOrder)
    .map((media) => media.url);
  return images.length ? images : [placeholderImageFor(product.id)];
}

export function parseAttributes(json: string | null | undefined): Record<string, string> {
  if (!json) return {};
  try {
    const parsed: unknown = JSON.parse(json);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return {};
    return Object.fromEntries(Object.entries(parsed).map(([key, value]) => [key, String(value)]));
  } catch {
    return {};
  }
}

export function productSkusOf(product: CatalogProduct): ProductSku[] {
  const variants = (product.variants ?? []).filter((variant) => variant.isActive);
  if (!variants.length) {
    return [{ sku: product.sku, price: product.price, currency: product.currency, attributes: {} }];
  }
  return variants.map((variant) => {
    const attributes = parseAttributes(variant.attributesJson);
    return {
      sku: variant.sku,
      variantId: variant.id,
      displayName: variant.displayName,
      price: variant.price,
      currency: variant.currency,
      // A variant without attributes still needs a selectable chip.
      attributes: Object.keys(attributes).length ? attributes : { 规格: variant.displayName },
      image: product.media?.find((media) => media.variantId === variant.id)?.url,
    };
  });
}

export function toDisplayProduct(product: CatalogProduct, categories: CatalogCategory[] = []): DisplayProduct {
  return {
    ...product,
    categoryLabel: categories.find((category) => category.id === product.categoryId)?.name ?? '精选商品',
    image: productImagesOf(product)[0] ?? placeholderImageFor(product.id),
  };
}

export function priceLabel(product: CatalogProduct): { amount: number; from: boolean } {
  const min = product.minPrice ?? product.price;
  const max = product.maxPrice ?? product.price;
  return { amount: min, from: max > min };
}

function readImageCache(): Record<string, string> {
  try {
    const raw = window.localStorage.getItem(imageCacheKey);
    const parsed: unknown = raw ? JSON.parse(raw) : {};
    return parsed && typeof parsed === 'object' ? parsed as Record<string, string> : {};
  } catch {
    return {};
  }
}

/** Basket and order DTOs do not carry media, so remember the image a shopper saw for each SKU. */
export function rememberSkuImage(sku: string, image: string): void {
  try {
    const cache = readImageCache();
    cache[sku.toUpperCase()] = image;
    const entries = Object.entries(cache).slice(-200);
    window.localStorage.setItem(imageCacheKey, JSON.stringify(Object.fromEntries(entries)));
  } catch {
    // Images fall back to placeholders when storage is unavailable.
  }
}

export function imageForSku(sku: string, fallbackKey = sku): string {
  return readImageCache()[sku.toUpperCase()] ?? placeholderImageFor(fallbackKey);
}
