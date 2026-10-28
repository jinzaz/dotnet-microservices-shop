// 压测准备：批量注册用户，再按浏览器的方式走一遍 BFF 登录，把会话 Cookie 和防伪 token 写进 sessions.json。
// 注册接口对已存在的邮箱也返回 204，所以重跑是安全的（已注册的直接登录）。
//
//   SHOP_URL=https://shop.example.com IDENTITY_URL=https://identity.example.com LT_PASSWORD=... node prepare.mjs
//
// 可选：LT_USERS（默认 1000）、LT_CONCURRENCY（默认 8）、LT_PREFIX（默认 lt）、LT_SKIP_REGISTER=1。
// 注册和登录都要算一次密码哈希（PBKDF2，故意做得很慢），认证中心 CPU 会被打满；并发别超过服务器核数，
// 否则请求排队超时。有失败就直接重跑：已注册的账号注册接口照样返回成功，接着登录。
// 自签名证书：加 NODE_TLS_REJECT_UNAUTHORIZED=0。
import { writeFileSync } from 'node:fs';

const env = process.env;
const shop = required('SHOP_URL').replace(/\/$/, '');
const identity = required('IDENTITY_URL').replace(/\/$/, '');
const password = required('LT_PASSWORD');
const users = Number(env.LT_USERS ?? 1000);
const concurrency = Number(env.LT_CONCURRENCY ?? 12);
const prefix = env.LT_PREFIX ?? 'lt';
const skipRegister = env.LT_SKIP_REGISTER === '1';

function required(name) {
  if (!env[name]) throw new Error(`缺少环境变量 ${name}`);
  return env[name];
}

/** 按主机分开存 Cookie。服务端的 Cookie 都是 host-only、Path=/，不需要处理 Domain / Path。 */
class Jar {
  #hosts = new Map();

  store(url, response) {
    const host = new URL(url).host;
    const cookies = this.#hosts.get(host) ?? new Map();
    for (const line of response.headers.getSetCookie()) {
      const [pair, ...attributes] = line.split(';');
      const index = pair.indexOf('=');
      const name = pair.slice(0, index).trim();
      const value = pair.slice(index + 1).trim();
      const expired = attributes.some(a => /^\s*expires=/i.test(a) && Date.parse(a.split('=')[1]) < Date.now());
      if (!value || expired) cookies.delete(name);
      else cookies.set(name, value);
    }
    this.#hosts.set(host, cookies);
  }

  header(url) {
    const cookies = this.#hosts.get(new URL(url).host) ?? new Map();
    return [...cookies].map(([name, value]) => `${name}=${value}`).join('; ');
  }

  object(url) {
    return Object.fromEntries(this.#hosts.get(new URL(url).host) ?? []);
  }
}

async function send(jar, url, { method = 'GET', headers = {}, body } = {}) {
  let response;
  try {
    response = await fetch(url, {
      method,
      redirect: 'manual',
      headers: { ...headers, Cookie: jar.header(url) },
      body,
    });
  } catch (error) {
    // fetch 只报 "fetch failed"，真正原因（连接超时、被重置、DNS…）在 cause 里。
    const cause = error.cause ? `${error.cause.code ?? ''} ${error.cause.message ?? ''}`.trim() : error.message;
    throw Object.assign(new Error(`${method} ${new URL(url).pathname} 网络错误：${cause}`), { network: true });
  }
  jar.store(url, response);
  return response;
}

async function expectStatus(response, expected, step) {
  if (response.status === expected) return response;
  const text = await response.text().catch(() => '');
  throw new Error(`${step}: HTTP ${response.status} ${text.slice(0, 200)}`);
}

async function identityPost(jar, path, payload, step) {
  const csrf = await expectStatus(await send(jar, `${identity}/api/Auth/csrf`), 200, `${step} csrf`);
  const { token } = await csrf.json();
  const response = await send(jar, `${identity}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
    body: JSON.stringify(payload),
  });
  return expectStatus(response, 204, step);
}

/** 认证中心回给 BFF 的授权码可能是 302 跳转（query），也可能是自动提交的表单（form_post），两种都处理。 */
async function deliverCallback(jar, authorizeResponse) {
  if (authorizeResponse.status === 302) {
    const location = new URL(authorizeResponse.headers.get('location'), identity).href;
    if (!location.startsWith(`${shop}/bff/callback/login`))
      throw new Error(`authorize 没有回到 BFF，跳到了 ${location}（登录没生效？）`);
    return send(jar, location);
  }

  await expectStatus(authorizeResponse, 200, 'authorize');
  const html = await authorizeResponse.text();
  const action = html.match(/<form[^>]*action="([^"]+)"/i)?.[1];
  if (!action) throw new Error('authorize 返回 200 但不是 form_post 表单');
  const fields = new URLSearchParams();
  for (const [, name, value] of html.matchAll(/<input[^>]*name="([^"]+)"[^>]*value="([^"]*)"/gi))
    fields.set(name, decodeHtml(value));
  return send(jar, decodeHtml(action), {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: fields.toString(),
  });
}

function decodeHtml(value) {
  return value.replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#x2B;/gi, '+').replace(/&#x3D;/gi, '=')
    .replace(/&#x2F;/gi, '/').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
}

async function prepareUser(index) {
  const email = `${prefix}${String(index).padStart(6, '0')}@loadtest.local`;
  const jar = new Jar();

  if (!skipRegister)
    await identityPost(jar, '/api/Auth/register', { email, password, displayName: `压测${index}` }, 'register');
  await identityPost(jar, '/api/Auth/login/password', { email, password, rememberMe: false }, 'login');

  // BFF 生成 PKCE 和 state，302 到认证中心；认证中心已登录，再 302（或 form_post）回 BFF 的回调。
  const login = await expectStatus(await send(jar, `${shop}/bff/login`), 302, 'bff/login');
  const authorize = await send(jar, new URL(login.headers.get('location'), shop).href);
  await expectStatus(await deliverCallback(jar, authorize), 302, 'bff/callback/login');

  const csrf = await expectStatus(await send(jar, `${shop}/bff/csrf`), 200, 'bff/csrf');
  const { token } = await csrf.json();
  await expectStatus(await send(jar, `${shop}/bff/user`), 200, 'bff/user');

  return { email, csrf: token, cookies: jar.object(shop) };
}

const sessions = [];
const failures = [];
let next = 1;
const started = Date.now();

async function worker() {
  while (next <= users) {
    const index = next++;
    // 网络错误（比如 nginx 用满 keepalive_requests 后发 GOAWAY 轮换 HTTP/2 连接）整个用户从头重来：
    // 注册对已存在的账号照样返回成功，授权码是新换的，重来是安全的。HTTP 状态码错误不重试。
    for (let attempt = 1; ; attempt++) {
      try {
        sessions.push(await prepareUser(index));
        break;
      } catch (error) {
        if (error.network && attempt < 3) continue;
        failures.push(index);
        console.error(`#${index} ${error.message}`);
        break;
      }
    }
    const done = sessions.length + failures.length;
    if (done % 100 === 0) console.log(`${done}/${users}  ${((Date.now() - started) / 1000).toFixed(0)}s`);
  }
}

await Promise.all(Array.from({ length: concurrency }, worker));
sessions.sort((a, b) => a.email.localeCompare(b.email));
writeFileSync(new URL('./sessions.json', import.meta.url), JSON.stringify(sessions));
console.log(`完成：成功 ${sessions.length}，失败 ${failures.length}，耗时 ${((Date.now() - started) / 1000).toFixed(0)}s → sessions.json`);
if (failures.length) process.exitCode = 1;
