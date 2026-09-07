import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Icon } from '../../shared/ui/Icon';

type HeroSlide = {
  id: string;
  title: string;
  description: string;
  image: string;
  imageAlt: string;
  primaryLabel: string;
  primaryTo: string;
  secondaryLabel: string;
  secondaryTo: string;
  imagePosition?: string;
};

const slides: readonly HeroSlide[] = [
  {
    id: 'everyday',
    title: '把日常，选得更好',
    description: '从质感家居到随身好物，认真挑选每一件值得长期使用的东西。',
    image: '/images/hero-lifestyle.jpg',
    imageAlt: '黑色手冲壶、陶瓷杯与绿植组成的安静居家一角',
    primaryLabel: '浏览精选',
    primaryTo: '#featured',
    secondaryLabel: '查看新品',
    secondaryTo: '#new',
    imagePosition: '57% center',
  },
  {
    id: 'pour-over',
    title: '从容冲煮，一杯刚好',
    description: '通透玻璃、温润木环与顺手器具，让每天的咖啡时刻简单而笃定。',
    image: '/images/product-pour-over-bundle.png',
    imageAlt: '玻璃手冲咖啡套装、黑色细口壶与陶瓷杯',
    primaryLabel: '查看手冲套装',
    primaryTo: '/products/editorial-pour-over?demo=1',
    secondaryLabel: '浏览居家好物',
    secondaryTo: '#featured',
    imagePosition: 'center 59%',
  },
  {
    id: 'commute',
    title: '轻装出发，自在有序',
    description: '轻盈材质与清晰分区，把通勤和短途出行都收拾得恰到好处。',
    image: '/images/product-backpack.jpg',
    imageAlt: '浅色轻量通勤双肩包置于安静卧室',
    primaryLabel: '查看通勤双肩包',
    primaryTo: '/products/editorial-backpack?demo=1',
    secondaryLabel: '发现更多',
    secondaryTo: '#featured',
    imagePosition: 'center 48%',
  },
];

export function HeroCarousel() {
  const [activeIndex, setActiveIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const activeSlide = slides[activeIndex] ?? slides[0]!;

  useEffect(() => {
    if (paused) return undefined;
    const timer = window.setInterval(
      () => setActiveIndex((current) => (current + 1) % slides.length),
      6_000,
    );
    return () => window.clearInterval(timer);
  }, [paused]);

  const move = (offset: number) => {
    setActiveIndex((current) => (current + offset + slides.length) % slides.length);
  };

  return (
    <section
      className="home-hero home-carousel"
      aria-roledescription="轮播图"
      aria-label="首页精选推荐"
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocusCapture={() => setPaused(true)}
      onBlurCapture={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) setPaused(false);
      }}
    >
      <div className="home-hero__copy" key={`${activeSlide.id}-copy`}>
        <h1>{activeSlide.title}</h1>
        <p>{activeSlide.description}</p>
        <div className="home-hero__actions">
          <Link className="button button--primary button--regular" to={activeSlide.primaryTo}>
            {activeSlide.primaryLabel}
          </Link>
          <Link className="text-link" to={activeSlide.secondaryTo}>
            {activeSlide.secondaryLabel} <Icon name="arrow-right" />
          </Link>
        </div>
      </div>
      <div className="home-hero__media" key={`${activeSlide.id}-media`}>
        <img
          src={activeSlide.image}
          alt={activeSlide.imageAlt}
          width="1672"
          height="941"
          fetchPriority={activeIndex === 0 ? 'high' : 'auto'}
          style={{ objectPosition: activeSlide.imagePosition }}
        />
      </div>
      <button className="carousel-arrow carousel-arrow--previous" type="button" onClick={() => move(-1)} aria-label="上一张推荐">
        <Icon name="chevron-left" />
      </button>
      <button className="carousel-arrow carousel-arrow--next" type="button" onClick={() => move(1)} aria-label="下一张推荐">
        <Icon name="chevron-right" />
      </button>
      <div className="carousel-pagination" role="group" aria-label="选择推荐内容">
        {slides.map((slide, index) => (
          <button
            key={slide.id}
            type="button"
            className={index === activeIndex ? 'is-active' : undefined}
            aria-label={`显示第 ${index + 1} 张：${slide.title}`}
            aria-current={index === activeIndex ? 'true' : undefined}
            onClick={() => setActiveIndex(index)}
          />
        ))}
      </div>
    </section>
  );
}
