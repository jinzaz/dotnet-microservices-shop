import { useEffect, useState } from 'react';
import { bffUrl } from '../../shared/config/api';
import { formatDateTime } from '../../shared/utils/format';

type FlashSaleOffer = {
  offerId: string;
  state: 'Scheduled' | 'Active' | 'SoldOut' | 'Expired' | 'Closed' | string;
  totalQuantity: number;
  availableQuantity: number;
  perUserLimit: number;
  startsOnUtc: string;
  endsOnUtc: string;
};

type FlashSaleSnapshot = { sku: string; offers: FlashSaleOffer[]; asOfUtc: string };

/**
 * Live flash-sale stock for one SKU over server-sent events. The BFF relays Inventory's gRPC stream;
 * EventSource reconnects on its own and the first message after a reconnect is a full snapshot.
 */
export function useFlashSaleOffers(sku: string | undefined) {
  const [offers, setOffers] = useState<FlashSaleOffer[]>([]);

  useEffect(() => {
    setOffers([]);
    if (!sku || typeof EventSource === 'undefined') return undefined;
    const source = new EventSource(bffUrl(`/bff/flash-sales/${encodeURIComponent(sku)}/stream`));
    source.addEventListener('offers', (event) => {
      const snapshot = JSON.parse((event as MessageEvent<string>).data) as FlashSaleSnapshot;
      setOffers(snapshot.offers);
    });
    return () => source.close();
  }, [sku]);

  return offers;
}

export function FlashSaleBadge({ sku }: { sku: string | undefined }) {
  const offers = useFlashSaleOffers(sku);
  // Show the offer that matters now: a running one first, otherwise the next one to start.
  const offer = offers.find((item) => item.state === 'Active' || item.state === 'SoldOut')
    ?? offers.find((item) => item.state === 'Scheduled');
  if (!offer) return null;

  const claimed = offer.totalQuantity - offer.availableQuantity;
  const progress = offer.totalQuantity > 0 ? Math.round((claimed / offer.totalQuantity) * 100) : 0;
  return (
    <div className={`flash-sale-badge is-${offer.state.toLowerCase()}`} role="status" aria-live="polite">
      <em>限时秒杀</em>
      {offer.state === 'Scheduled' ? (
        <span>{formatDateTime(offer.startsOnUtc)} 开抢 · 共 {offer.totalQuantity} 件 · 每人限 {offer.perUserLimit} 件</span>
      ) : offer.state === 'SoldOut' ? (
        <span>已抢光</span>
      ) : (
        <span>
          剩余 <strong>{offer.availableQuantity}</strong> / {offer.totalQuantity} 件 · 每人限 {offer.perUserLimit} 件
          <i className="flash-sale-badge__bar" style={{ ['--progress' as string]: `${progress}%` }} />
        </span>
      )}
    </div>
  );
}
