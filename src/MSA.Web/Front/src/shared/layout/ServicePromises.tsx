import { Icon, type IconName } from '../ui/Icon';

const promises: ReadonlyArray<{ icon: IconName; title: string; detail: string }> = [
  { icon: 'shield', title: '精选品质', detail: '严格筛选，长期主义' },
  { icon: 'truck', title: '便捷配送', detail: '多仓发货，快速送达' },
  { icon: 'refresh', title: '安心售后', detail: '7 天无理由退换' },
  { icon: 'headset', title: '贴心客服', detail: '在线解答，及时响应' },
];

export function ServicePromises() {
  return (
    <section className="service-promises" aria-label="服务承诺">
      <div className="service-promises__inner">
        {promises.map((item) => (
          <div className="service-promise" key={item.title}>
            <Icon name={item.icon} />
            <div>
              <strong>{item.title}</strong>
              <span>{item.detail}</span>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
