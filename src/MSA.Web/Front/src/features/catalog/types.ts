export type CatalogProduct = {
  id: string;
  categoryId: string;
  sku: string;
  name: string;
  description: string;
  price: number;
  currency: string;
  isActive: boolean;
  updatedOnUtc: string;
  lifecycleStatus?: string;
};

export type DisplayProduct = CatalogProduct & {
  categoryLabel: string;
  image: string;
};
