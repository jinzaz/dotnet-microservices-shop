export type BasketMoneyTotal = {
  currency: string;
  amount: number;
};

export type BasketItem = {
  productId: string;
  sku: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  currency: string;
  lineAmount: number;
  isAvailable: boolean;
  priceChangedSinceSelection: boolean;
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
  image: string;
  unitPrice: number;
  currency: string;
  quantity: number;
  attributes: Record<string, string>;
};
