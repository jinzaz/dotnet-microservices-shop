export type BasketMoneyTotal = {
  currency: string;
  amount: number;
};

/** Mirrors BasketService BasketItemDto. */
export type BasketItem = {
  productId: string;
  variantId: string;
  sku: string;
  productName: string;
  variantName: string;
  attributesJson: string;
  quantity: number;
  unitPrice: number;
  currency: string;
  lineAmount: number;
  isAvailable: boolean;
  unitPriceAtSelection: number;
  priceChangedSinceSelection: boolean;
  addedOnUtc: string;
  updatedOnUtc: string;
};

export type Basket = {
  basketId: string | null;
  version: number;
  items: BasketItem[];
  couponCodes: string[];
  estimatedTotals: BasketMoneyTotal[];
  hasUnavailableItems: boolean;
  hasPriceChanges: boolean;
  updatedOnUtc: string | null;
  pricingNotice: string;
};

export type CartLine = {
  productId: string;
  sku: string;
  name: string;
  variantName?: string;
  image: string;
  unitPrice: number;
  currency: string;
  quantity: number;
  attributes: Record<string, string>;
  isAvailable?: boolean;
  priceChanged?: boolean;
  previousUnitPrice?: number;
};
