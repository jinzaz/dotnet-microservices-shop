import { useState } from 'react';

type ProductGalleryProps = {
  images: string[];
  productName: string;
};

export function ProductGallery({ images, productName }: ProductGalleryProps) {
  const [activeIndex, setActiveIndex] = useState(0);
  const activeImage = images[activeIndex] ?? images[0];

  return (
    <div className="product-gallery">
      <div className="product-gallery__rail" aria-label="商品图片列表">
        {images.map((image, index) => (
          <button
            key={image}
            type="button"
            className={index === activeIndex ? 'is-active' : undefined}
            aria-label={`查看${productName}第 ${index + 1} 张图片`}
            aria-pressed={index === activeIndex}
            onClick={() => setActiveIndex(index)}
          >
            <img src={image} alt="" width="120" height="120" loading={index === 0 ? 'eager' : 'lazy'} />
          </button>
        ))}
      </div>
      <div className="product-gallery__main">
        <img
          key={activeImage}
          src={activeImage}
          alt={`${productName}商品展示图 ${activeIndex + 1}`}
          width="1024"
          height="1024"
          fetchPriority={activeIndex === 0 ? 'high' : 'auto'}
        />
      </div>
    </div>
  );
}
