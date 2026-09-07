export type ProductReview = {
  id: string;
  userName: string;
  rating: number;
  content: string;
  skuLabel: string;
  createdOnUtc: string;
  images: string[];
};

export type ProductReviewResult = {
  items: ProductReview[];
  total: number;
  averageRating: number;
  ratingDistribution: Record<1 | 2 | 3 | 4 | 5, number>;
};
