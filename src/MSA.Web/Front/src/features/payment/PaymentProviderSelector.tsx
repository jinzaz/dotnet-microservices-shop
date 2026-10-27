import type { PaymentChannel } from './types';

const allProviders: ReadonlyArray<{ channel: PaymentChannel; label: string; shortLabel: string }> = [
  { channel: 'Alipay', label: '支付宝', shortLabel: '支' },
  { channel: 'WeChatPay', label: '微信支付', shortLabel: '微' },
  { channel: 'Balance', label: '余额支付', shortLabel: '余' },
];
// 余额支付下单后直接扣款，没有扫码环节，不出现在支付页的渠道选择里。
const providers = allProviders.filter((item) => item.channel !== 'Balance');

export function PaymentProviderMark({ channel }: { channel: PaymentChannel }) {
  const provider = allProviders.find((item) => item.channel === channel) ?? allProviders[0]!;
  return <span className={`payment-provider-mark payment-provider-mark--${channel.toLowerCase()}`}>{provider.shortLabel}</span>;
}

export function paymentChannelLabel(channel: PaymentChannel): string {
  return allProviders.find((item) => item.channel === channel)?.label ?? '在线支付';
}

export function PaymentProviderSelector({
  selected,
  onSelect,
}: {
  selected: PaymentChannel;
  onSelect: (channel: PaymentChannel) => void;
}) {
  return (
    <div className="payment-method-selector" role="radiogroup" aria-label="选择支付方式">
      {providers.map((provider) => (
        <button
          key={provider.channel}
          type="button"
          className={selected === provider.channel ? 'is-selected' : undefined}
          role="radio"
          aria-checked={selected === provider.channel}
          onClick={() => onSelect(provider.channel)}
        >
          <span className="selection-dot" />
          <PaymentProviderMark channel={provider.channel} />
          <strong>{provider.label}</strong>
        </button>
      ))}
    </div>
  );
}
