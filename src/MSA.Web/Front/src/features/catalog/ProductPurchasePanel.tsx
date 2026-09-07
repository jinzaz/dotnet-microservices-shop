import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../app/auth/AuthProvider';
import { addBasketItem } from '../basket/basket-api';
import { addCartLine } from '../basket/basket-store';
import { claimCoupon } from '../coupons/coupons-api';
import { getErrorMessage } from '../../shared/api/api-error';
import { isDemoMode } from '../../shared/config/api';
import { Button } from '../../shared/ui/Button';
import { Icon } from '../../shared/ui/Icon';
import { formatMoney } from '../../shared/utils/format';
import { getInventoryAvailability } from './inventory-api';
import type { ProductDetail, ProductSku } from './product-detail-types';
import { RatingStars } from './RatingStars';

function attributeGroups(skus: ProductSku[]) {
  const names = Array.from(new Set(skus.flatMap((sku) => Object.keys(sku.attributes))));
  return names.map((name) => ({
    name,
    values: Array.from(new Set(skus.map((sku) => sku.attributes[name]).filter((value): value is string => Boolean(value)))),
  }));
}

export function ProductPurchasePanel({ product }: { product: ProductDetail }) {
  const initialSku = product.skus[0];
  const [selectedSkuCode, setSelectedSkuCode] = useState(initialSku?.sku ?? product.sku);
  const [quantity, setQuantity] = useState(1);
  const [adding, setAdding] = useState(false);
  const [claiming, setClaiming] = useState(false);
  const [couponClaimed, setCouponClaimed] = useState(false);
  const [notice, setNotice] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const { isAuthenticated } = useAuth();
  const demoMode = isDemoMode(location.search);
  const selectedSku = product.skus.find((sku) => sku.sku === selectedSkuCode) ?? initialSku;
  const groups = useMemo(() => attributeGroups(product.skus), [product.skus]);
  const inventoryQuery = useQuery({
    queryKey: ['inventory', selectedSku?.sku],
    queryFn: () => getInventoryAvailability(selectedSku?.sku ?? ''),
    enabled: Boolean(selectedSku?.sku && isAuthenticated && !demoMode),
    staleTime: 20_000,
    retry: 0,
  });
  const available = inventoryQuery.data?.available ?? selectedSku?.availableQuantity;
  const coupon = product.coupons[0];

  const chooseAttribute = (name: string, value: string) => {
    if (!selectedSku) return;
    const desired = { ...selectedSku.attributes, [name]: value };
    const nextSku = product.skus.find((sku) =>
      Object.entries(desired).every(([attribute, selectedValue]) => sku.attributes[attribute] === selectedValue));
    if (nextSku) setSelectedSkuCode(nextSku.sku);
  };

  const addToBasket = async (buyNow: boolean) => {
    if (!selectedSku) return;
    setAdding(true);
    setNotice('');
    addCartLine({
      productId: product.id,
      sku: selectedSku.sku,
      name: product.name,
      image: product.images[0] ?? product.image,
      unitPrice: selectedSku.price,
      currency: selectedSku.currency,
      quantity,
      attributes: selectedSku.attributes,
    });

    let synced = false;
    if (isAuthenticated && !demoMode) {
      try {
        await addBasketItem(selectedSku.sku, quantity);
        synced = true;
      } catch {
        // Keep the local draft usable while the Basket service is unavailable.
      }
    }
    setNotice(synced ? '已加入购物袋，并同步到购物车服务。' : '已加入购物袋，可继续前往确认订单。');
    setAdding(false);
    if (buyNow) navigate(`/checkout${demoMode ? '?demo=1' : ''}`);
  };

  const handleClaimCoupon = async () => {
    if (!coupon || couponClaimed) return;
    setClaiming(true);
    setNotice('');
    try {
      if (!demoMode && !product.fallbackContracts.includes('用户可领优惠券')) {
        await claimCoupon(coupon.id);
      }
      setCouponClaimed(true);
      setNotice('优惠券已领取，结算时可选择使用。');
    } catch (error) {
      setCouponClaimed(true);
      setNotice(`领券接口待接入；当前页面已模拟领取。${getErrorMessage(error)}`);
    } finally {
      setClaiming(false);
    }
  };

  if (!selectedSku) return null;

  return (
    <section className="product-purchase" aria-label="商品购买选项">
      <h1>{product.name}</h1>
      <p className="product-purchase__description">{product.description}</p>
      <div className="product-rating-line">
        <RatingStars rating={product.reviews.averageRating} />
        <span>{product.reviews.averageRating.toFixed(1)} · {product.reviews.total} 条评价</span>
      </div>
      <strong className="product-price">{formatMoney(selectedSku.price, selectedSku.currency)}</strong>

      {coupon ? (
        <div className="product-coupon">
          <strong>优惠券</strong>
          <span>{coupon.conditionLabel.replace('可用', '')}{coupon.valueLabel === '¥40' ? '减40' : coupon.valueLabel}</span>
          <button type="button" onClick={() => void handleClaimCoupon()} disabled={claiming || couponClaimed}>
            {claiming ? '领取中…' : couponClaimed ? '已领取' : '领取'}
            <Icon name="chevron-right" />
          </button>
        </div>
      ) : null}

      <div className="sku-selector">
        {groups.map((group) => (
          <fieldset key={group.name}>
            <legend>{group.name}</legend>
            <div>
              {group.values.map((value) => (
                <button
                  key={value}
                  type="button"
                  className={selectedSku.attributes[group.name] === value ? 'is-active' : undefined}
                  aria-pressed={selectedSku.attributes[group.name] === value}
                  onClick={() => chooseAttribute(group.name, value)}
                >
                  {value}
                </button>
              ))}
            </div>
          </fieldset>
        ))}
      </div>

      <div className="product-stock"><span>库存</span><strong>{available === 0 ? '暂时缺货' : available ? `现货 · 剩余 ${available} 件` : '有货，库存以结算为准'}</strong></div>
      <div className="quantity-row">
        <span>数量</span>
        <div className="quantity-stepper">
          <button type="button" onClick={() => setQuantity((current) => Math.max(1, current - 1))} aria-label="减少数量" disabled={quantity <= 1}><Icon name="minus" /></button>
          <output aria-live="polite">{quantity}</output>
          <button type="button" onClick={() => setQuantity((current) => Math.min(99, current + 1))} aria-label="增加数量"><Icon name="plus" /></button>
        </div>
      </div>
      <div className="product-purchase__actions">
        <Button type="button" loading={adding} disabled={available === 0} onClick={() => void addToBasket(false)}>加入购物袋</Button>
        <Button type="button" variant="secondary" disabled={adding || available === 0} onClick={() => void addToBasket(true)}>立即购买</Button>
      </div>
      {notice ? <p className="product-action-notice" role="status">{notice}</p> : null}

      <div className="product-assurances">
        <div><Icon name="truck" /><span><strong>多仓发货</strong>快速送达</span></div>
        <div><Icon name="shield" /><span><strong>7 天无理由退换</strong>安心选购</span></div>
        <div><Icon name="headset" /><span><strong>在线客服</strong>9:00–21:00</span></div>
      </div>
    </section>
  );
}
