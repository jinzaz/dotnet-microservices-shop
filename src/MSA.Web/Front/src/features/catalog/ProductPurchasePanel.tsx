import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../app/auth/AuthProvider';
import { MAX_LINE_QUANTITY, useBasketActions } from '../basket/basket-store';
import { getErrorMessage } from '../../shared/api/api-error';
import { isDemoMode } from '../../shared/config/api';
import { Button } from '../../shared/ui/Button';
import { Icon } from '../../shared/ui/Icon';
import { useToast } from '../../shared/ui/Toast';
import { formatMoney } from '../../shared/utils/format';
import { getInventoryAvailability } from './inventory-api';
import type { ProductDetail, ProductSku } from './product-detail-types';
import { RatingStars } from './RatingStars';
import { describePromotion } from '../promotions/describe';
import { formatDateTime } from '../../shared/utils/format';

function attributeGroups(skus: ProductSku[]) {
  const names = Array.from(new Set(skus.flatMap((sku) => Object.keys(sku.attributes))));
  return names.map((name) => ({
    name,
    values: Array.from(new Set(skus.map((sku) => sku.attributes[name]).filter((value): value is string => Boolean(value)))),
  }));
}

function stockLabel(available: number | undefined, isAuthenticated: boolean): string {
  if (available === 0) return '暂时缺货';
  if (available !== undefined) return available <= 10 ? `仅剩 ${available} 件，欲购从速` : '现货充足';
  return isAuthenticated ? '有货，库存以结算为准' : '登录后可查看实时库存';
}

export function ProductPurchasePanel({ product, onSkuChange }: { product: ProductDetail; onSkuChange?: (sku: ProductSku) => void }) {
  const initialSku = product.skus[0];
  const [selectedSkuCode, setSelectedSkuCode] = useState(initialSku?.sku ?? product.sku);
  const [quantity, setQuantity] = useState(1);
  const [pending, setPending] = useState<'add' | 'buy' | null>(null);
  const navigate = useNavigate();
  const location = useLocation();
  const toast = useToast();
  const { isAuthenticated } = useAuth();
  const basket = useBasketActions();
  const demoMode = isDemoMode(location.search);
  const demoSuffix = demoMode ? '?demo=1' : '';
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
  const maxQuantity = Math.max(1, Math.min(MAX_LINE_QUANTITY, available ?? MAX_LINE_QUANTITY));

  // Attribute values that cannot combine with the rest of the current selection are shown disabled.
  const isValueSelectable = (name: string, value: string) => {
    if (!selectedSku) return false;
    return product.skus.some((sku) => sku.attributes[name] === value
      && Object.entries(selectedSku.attributes).every(([attribute, selected]) => attribute === name || sku.attributes[attribute] === selected));
  };

  const selectSku = (sku: ProductSku) => {
    setSelectedSkuCode(sku.sku);
    setQuantity(1);
    onSkuChange?.(sku);
  };

  const chooseAttribute = (name: string, value: string) => {
    if (!selectedSku) return;
    const desired = { ...selectedSku.attributes, [name]: value };
    const exact = product.skus.find((sku) =>
      Object.entries(desired).every(([attribute, selectedValue]) => sku.attributes[attribute] === selectedValue));
    // Fall back to the first SKU carrying the clicked value so a click never feels ignored.
    const next = exact ?? product.skus.find((sku) => sku.attributes[name] === value);
    if (next) selectSku(next);
  };

  const addToBasket = async (buyNow: boolean) => {
    if (!selectedSku) return;
    setPending(buyNow ? 'buy' : 'add');
    try {
      await basket.add({
        productId: product.id,
        sku: selectedSku.sku,
        name: product.name,
        variantName: selectedSku.displayName,
        image: selectedSku.image ?? product.images[0] ?? product.image,
        unitPrice: selectedSku.price,
        currency: selectedSku.currency,
        quantity,
        attributes: selectedSku.attributes,
      });
      if (buyNow) {
        navigate(isAuthenticated || demoMode ? `/checkout${demoSuffix}` : `/login?redirect=${encodeURIComponent('/checkout')}`);
      } else {
        toast(`已加入购物袋 · ${product.name} × ${quantity}`, {
          action: { label: '去结算', onClick: () => navigate(`/cart${demoSuffix}`) },
        });
      }
    } catch (error) {
      toast(getErrorMessage(error, '加入购物袋失败，请稍后重试。'), { tone: 'error' });
    } finally {
      setPending(null);
    }
  };

  if (!selectedSku) return null;

  return (
    <section className="product-purchase" aria-label="商品购买选项">
      <h1>{product.name}</h1>
      <p className="product-purchase__description">{product.description}</p>
      {product.reviews.total > 0 ? (
        <a className="product-rating-line" href="#reviews">
          <RatingStars rating={product.reviews.averageRating} />
          <span>{product.reviews.averageRating.toFixed(1)} · {product.reviews.total} 条评价</span>
        </a>
      ) : null}
      <strong className="product-price">{formatMoney(selectedSku.price, selectedSku.currency)}</strong>

      {product.promotions.length ? (
        <ul className="product-promotions" aria-label="可享优惠">
          {product.promotions.slice(0, 3).map((promotion) => (
            <li key={promotion.id}>
              <em>促销</em>
              <span><strong>{describePromotion(promotion.rules, promotion.benefits, selectedSku.currency)}</strong><small>{promotion.name} · {formatDateTime(promotion.endsOnUtc)} 结束</small></span>
            </li>
          ))}
          <li className="product-promotions__coupon"><em>优惠码</em><span>有优惠码？<Link to={`/coupons${demoSuffix}`}>在我的优惠券中使用<Icon name="chevron-right" /></Link></span></li>
        </ul>
      ) : null}

      <div className="sku-selector">
        {groups.map((group) => (
          <fieldset key={group.name}>
            <legend>{group.name}</legend>
            <div>
              {group.values.map((value) => {
                const active = selectedSku.attributes[group.name] === value;
                return (
                  <button
                    key={value}
                    type="button"
                    className={`${active ? 'is-active' : ''} ${!active && !isValueSelectable(group.name, value) ? 'is-unavailable' : ''}`.trim() || undefined}
                    aria-pressed={active}
                    onClick={() => chooseAttribute(group.name, value)}
                  >
                    {value}
                  </button>
                );
              })}
            </div>
          </fieldset>
        ))}
      </div>

      <div className={`product-stock${available === 0 ? ' is-out' : ''}`}>
        <span>库存</span>
        <strong>{inventoryQuery.isFetching && available === undefined ? '正在查询库存…' : stockLabel(available, isAuthenticated || demoMode)}</strong>
      </div>
      <div className="quantity-row">
        <span>数量</span>
        <div className="quantity-stepper">
          <button type="button" onClick={() => setQuantity((current) => Math.max(1, current - 1))} aria-label="减少数量" disabled={quantity <= 1}><Icon name="minus" /></button>
          <output aria-live="polite">{quantity}</output>
          <button type="button" onClick={() => setQuantity((current) => Math.min(maxQuantity, current + 1))} aria-label="增加数量" disabled={quantity >= maxQuantity}><Icon name="plus" /></button>
        </div>
        {quantity >= maxQuantity && available ? <small className="quantity-hint">已达可购上限</small> : null}
      </div>
      <div className="product-purchase__actions">
        <Button type="button" loading={pending === 'add'} disabled={available === 0 || pending !== null} onClick={() => void addToBasket(false)}>
          <Icon name="bag" />加入购物袋
        </Button>
        <Button type="button" variant="secondary" loading={pending === 'buy'} disabled={available === 0 || pending !== null} onClick={() => void addToBasket(true)}>
          立即购买
        </Button>
      </div>

      <div className="product-assurances">
        <div><Icon name="truck" /><span><strong>多仓发货</strong>快速送达</span></div>
        <div><Icon name="shield" /><span><strong>7 天售后保障</strong>签收后可申请退换</span></div>
        <div><Icon name="headset" /><span><strong>在线客服</strong>9:00–21:00</span></div>
      </div>
    </section>
  );
}
