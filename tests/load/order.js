// 下单压测：走 BFF（会话 Cookie + X-XSRF-TOKEN，明文 JSON、不带 X-Msa-Ekey，所以不含请求体加密的开销）。
// 用 SCENARIO 选场景：order（默认，阶梯加压）、flash（秒杀，每个用户同时抢一次）、idempotency（同一个 Idempotency-Key 并发提交）。
//
//   k6 run -e SHOP_URL=https://shop.example.com -e SKUS=SKU-A,SKU-B order.js
//   k6 run -e SHOP_URL=... -e SCENARIO=flash -e FLASH_SKU=SKU-FLASH order.js
//   k6 run -e SHOP_URL=... -e SCENARIO=idempotency -e SKUS=SKU-A order.js
import http from 'k6/http';
import exec from 'k6/execution';
import { check } from 'k6';
import { SharedArray } from 'k6/data';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const shop = (__ENV.SHOP_URL ?? '').replace(/\/$/, '');
const scenario = __ENV.SCENARIO ?? 'order';
const skus = (__ENV.SKUS ?? '').split(',').map(s => s.trim()).filter(Boolean);
const sessions = new SharedArray('sessions', () => JSON.parse(open('./sessions.json')));

if (!shop) throw new Error('缺少 -e SHOP_URL=...');
if (scenario === 'flash' ? !__ENV.FLASH_SKU : skus.length === 0) throw new Error('缺少 -e SKUS=... 或 -e FLASH_SKU=...');

// 下单接口按用户限流：突发 5 次，之后每秒 2 次。order 场景轮流使用用户，速率超过「用户数 × 2」就会出现 429。
const scenarios = {
  order: {
    executor: 'ramping-arrival-rate',
    startRate: 10,
    timeUnit: '1s',
    preAllocatedVUs: Number(__ENV.VUS ?? 200),
    maxVUs: Number(__ENV.MAX_VUS ?? 1000),
    stages: (__ENV.STAGES ?? '1m:50,2m:100,2m:200,1m:0').split(',').map(stage => {
      const [duration, target] = stage.split(':');
      return { duration, target: Number(target) };
    }),
  },
  // 每个 VU 用不同的用户、只跑一次：VU 在开始前全部初始化好，第一轮请求几乎同时发出。
  flash: {
    executor: 'per-vu-iterations',
    vus: Math.min(Number(__ENV.FLASH_VUS ?? 2000), sessions.length),
    iterations: 1,
    maxDuration: '2m',
  },
  // 同一个用户：突发上限是 5，再多就会被 429 挡掉，测不到幂等逻辑。
  idempotency: {
    executor: 'per-vu-iterations',
    vus: Number(__ENV.IDEM_VUS ?? 5),
    iterations: 1,
    maxDuration: '1m',
  },
};

export const options = {
  scenarios: { [scenario]: scenarios[scenario] },
  insecureSkipTLSVerify: __ENV.INSECURE === '1',
  thresholds: {
    checks: ['rate>0.99'],
    'http_req_duration{expected_response:true}': ['p(95)<500', 'p(99)<1000'],
  },
};

// 秒杀时库存预检挡下来的 409 是预期结果，不算错误。
http.setResponseCallback(scenario === 'flash' ? http.expectedStatuses(202, 409) : http.expectedStatuses(202));

const address = {
  addressId: null,
  recipientName: '压测用户',
  phoneNumber: '13800000000',
  countryCode: 'CN',
  province: '广东省',
  city: '深圳市',
  district: '南山区',
  streetLine1: '压测路 1 号',
  streetLine2: null,
  postalCode: '518000',
};

// 幂等场景所有 VU 共用一个 key，必须在 setup 里生成一次再分发，否则每个 VU 初始化时各算各的。
export function setup() {
  return { idempotencyKey: uuidv4() };
}

function placeOrder(session, items, idempotencyKey) {
  // replace: true 覆盖 VU 自带 Cookie 容器里的同名 Cookie：BFF 续期会话时会回写 Cookie，
  // 而 order 场景里同一个 VU 会轮流扮演不同用户。
  const cookies = Object.fromEntries(
    Object.entries(session.cookies).map(([name, value]) => [name, { value, replace: true }]));

  return http.post(`${shop}/api/order/orders`, JSON.stringify({ items, paymentMethod: 'Balance', shippingAddress: address }), {
    cookies,
    headers: {
      'Content-Type': 'application/json',
      'X-XSRF-TOKEN': session.csrf,
      'Idempotency-Key': idempotencyKey,
    },
    tags: { name: 'POST /api/order/orders' },
  });
}

export default function (data) {
  if (scenario === 'flash') {
    const item = { sku: __ENV.FLASH_SKU, quantity: 1 };
    if (__ENV.OFFER_ID) item.offerId = __ENV.OFFER_ID;
    const res = placeOrder(sessions[exec.vu.idInTest - 1], [item], uuidv4());
    check(res, { '202 或 409': r => r.status === 202 || r.status === 409 });
    return;
  }

  if (scenario === 'idempotency') {
    const res = placeOrder(sessions[0], [{ sku: skus[0], quantity: 1 }], data.idempotencyKey);
    check(res, { '202': r => r.status === 202 });
    console.log(`VU ${exec.vu.idInTest}: ${res.status} ${res.body}`);
    return;
  }

  const session = sessions[exec.scenario.iterationInTest % sessions.length];
  const sku = skus[Math.floor(Math.random() * skus.length)];
  const res = placeOrder(session, [{ sku, quantity: 1 }], uuidv4());
  check(res, {
    '202': r => r.status === 202,
    '没被限流': r => r.status !== 429,
  });
}
