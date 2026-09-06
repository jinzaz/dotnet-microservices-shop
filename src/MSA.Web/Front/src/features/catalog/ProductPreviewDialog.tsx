import { useEffect, useRef } from 'react';
import { Button } from '../../shared/ui/Button';
import { Icon } from '../../shared/ui/Icon';
import { formatMoney } from '../../shared/utils/format';
import type { DisplayProduct } from './types';

type ProductPreviewDialogProps = {
  product: DisplayProduct | null;
  onClose: () => void;
};

export function ProductPreviewDialog({ product, onClose }: ProductPreviewDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog || !product) return;
    if (!dialog.open) dialog.showModal();
    return () => {
      if (dialog.open) dialog.close();
    };
  }, [product]);

  if (!product) return null;

  return (
    <dialog
      className="product-preview"
      ref={dialogRef}
      onCancel={onClose}
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <button className="dialog-close" type="button" onClick={onClose} aria-label="关闭商品预览">
        <Icon name="close" />
      </button>
      <img src={product.image} alt="" width="720" height="720" />
      <div className="product-preview__content">
        <span>{product.categoryLabel}</span>
        <h2>{product.name}</h2>
        <strong>{formatMoney(product.price, product.currency)}</strong>
        <p>{product.description}</p>
        <Button type="button" onClick={onClose}>继续浏览</Button>
        <small>商品详情与加入购物车将在下一阶段接入。</small>
      </div>
    </dialog>
  );
}
