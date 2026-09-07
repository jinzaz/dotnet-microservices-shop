import type { ProductReviewResult } from './types';

export const demoProductReviews: ProductReviewResult = {
  total: 128,
  averageRating: 4.8,
  ratingDistribution: { 5: 106, 4: 17, 3: 3, 2: 1, 1: 1 },
  items: [
    {
      id: 'review-1',
      userName: 'LemonTree',
      rating: 5,
      content: '套装质感很棒，玻璃壶通透，滤杯做工精细，使用体验非常好。每天一杯手冲，幸福感满满。',
      skuLabel: '600ml / 胡桃木',
      createdOnUtc: '2026-08-20T10:24:00+08:00',
      images: ['/images/product-pour-over.jpg', '/images/hero-lifestyle.jpg'],
    },
    {
      id: 'review-2',
      userName: 'Winnie',
      rating: 5,
      content: '包装很用心，配件齐全。新手也能轻松上手，萃取效果稳定，清洗也方便，推荐！',
      skuLabel: '600ml / 原木',
      createdOnUtc: '2026-08-18T14:10:00+08:00',
      images: [],
    },
    {
      id: 'review-3',
      userName: 'Zhou.',
      rating: 4,
      content: '整体不错，壶身厚实耐热，滤杯出水顺畅。如果能有收纳包就更好了。',
      skuLabel: '800ml / 胡桃木',
      createdOnUtc: '2026-08-15T09:08:00+08:00',
      images: [],
    },
  ],
};
