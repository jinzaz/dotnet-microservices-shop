// 浏览器 ⇄ BFF 的请求/响应体加密：ECDH(P-256) + HKDF-SHA256 + AES-256-GCM，全部用浏览器原生
// WebCrypto，不引入额外的加密库。每次请求生成一个临时密钥对，跟 BFF 的静态公钥协商出共享密钥，
// 按方向派生出两个 AES 密钥（c2s 发请求、s2c 收响应），用完就扔——不是会话密钥，不用管过期/续期。
//
// 服务端实现见 src/BFFs/MSA.BFF.Common/BodyEncryption/BodyEncryptionMiddleware.cs，字节格式要跟这里完全对应：
//   - 公钥：未压缩点格式（0x04 || X(32) || Y(32)，65 字节），不是 SPKI/DER。
//   - 信封：nonce(12 字节) || ciphertext+tag（AES-GCM 的 tag 默认拼在密文末尾，不用分开传）。
//   - HKDF info：请求方向 "msa-body-c2s"，响应方向 "msa-body-s2c"，没有 salt（空）。
import { bffUrl } from '../config/api';

export const ENCRYPTED_CONTENT_TYPE = 'application/vnd.msa.enc';
export const EPHEMERAL_KEY_HEADER = 'X-Msa-Ekey';

const NONCE_LENGTH = 12;
const REQUEST_INFO = new TextEncoder().encode('msa-body-c2s');
const RESPONSE_INFO = new TextEncoder().encode('msa-body-s2c');

export type BodyEncryptionContext = {
  ephemeralPublicKeyRaw: ArrayBuffer;
  c2sKey: CryptoKey;
  s2cKey: CryptoKey;
};

let cachedServerPublicKey: Promise<CryptoKey> | undefined;

/** 懒加载并缓存 BFF 的 ECDH 公钥；并发请求共用同一次拉取，不会各发一遍。 */
function getServerPublicKey(): Promise<CryptoKey> {
  cachedServerPublicKey ??= (async () => {
    const response = await fetch(bffUrl('/bff/security/public-key'));
    if (!response.ok) throw new Error('无法获取服务端加密公钥。');
    const raw = await response.arrayBuffer();
    return crypto.subtle.importKey('raw', raw, { name: 'ECDH', namedCurve: 'P-256' }, false, []);
  })();
  return cachedServerPublicKey;
}

/** 服务端证书轮换或者公钥缓存坏了导致解密失败时，调用这个清掉缓存，下次请求重新拉一次。 */
export function resetServerPublicKeyCache(): void {
  cachedServerPublicKey = undefined;
}

async function deriveAesKey(sharedBits: ArrayBuffer, info: Uint8Array<ArrayBuffer>): Promise<CryptoKey> {
  const hkdfBaseKey = await crypto.subtle.importKey('raw', sharedBits, 'HKDF', false, ['deriveKey']);
  const salt: Uint8Array<ArrayBuffer> = new Uint8Array(0);
  return crypto.subtle.deriveKey(
    { name: 'HKDF', hash: 'SHA-256', salt, info },
    hkdfBaseKey,
    { name: 'AES-GCM', length: 256 },
    false,
    ['encrypt', 'decrypt'],
  );
}

/** 为一次请求生成临时密钥对并跟服务端协商出 c2s/s2c 两个方向各自的 AES 密钥。 */
export async function createBodyEncryptionContext(): Promise<BodyEncryptionContext> {
  const serverPublicKey = await getServerPublicKey();
  const ephemeral = await crypto.subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
  const ephemeralPublicKeyRaw = await crypto.subtle.exportKey('raw', ephemeral.publicKey);
  const sharedBits = await crypto.subtle.deriveBits({ name: 'ECDH', public: serverPublicKey }, ephemeral.privateKey, 256);

  const [c2sKey, s2cKey] = await Promise.all([
    deriveAesKey(sharedBits, REQUEST_INFO),
    deriveAesKey(sharedBits, RESPONSE_INFO),
  ]);

  return { ephemeralPublicKeyRaw, c2sKey, s2cKey };
}

function base64FromArrayBuffer(buffer: ArrayBuffer): string {
  return btoa(String.fromCharCode(...new Uint8Array(buffer)));
}

/** 把 X-Msa-Ekey 头的值算出来——GET 之类没有 body 的请求也要带这个头，响应才能被加密。 */
export function ephemeralKeyHeaderValue(context: BodyEncryptionContext): string {
  return base64FromArrayBuffer(context.ephemeralPublicKeyRaw);
}

export async function encryptRequestBody(context: BodyEncryptionContext, plaintext: Uint8Array<ArrayBuffer>): Promise<Uint8Array<ArrayBuffer>> {
  const nonce: Uint8Array<ArrayBuffer> = crypto.getRandomValues(new Uint8Array(NONCE_LENGTH));
  const ciphertextAndTag = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: nonce, tagLength: 128 }, context.c2sKey, plaintext);

  const envelope = new Uint8Array(NONCE_LENGTH + ciphertextAndTag.byteLength);
  envelope.set(nonce, 0);
  envelope.set(new Uint8Array(ciphertextAndTag), NONCE_LENGTH);
  return envelope;
}

export async function decryptResponseBody(context: BodyEncryptionContext, envelope: ArrayBuffer): Promise<ArrayBuffer> {
  const bytes = new Uint8Array(envelope);
  if (bytes.length < NONCE_LENGTH) throw new Error('加密响应体长度不对。');

  // slice（不是 subarray）：要一段独立的 ArrayBuffer-backed 视图传给 WebCrypto。
  const nonce = bytes.slice(0, NONCE_LENGTH);
  const ciphertextAndTag = bytes.slice(NONCE_LENGTH);
  return crypto.subtle.decrypt({ name: 'AES-GCM', iv: nonce, tagLength: 128 }, context.s2cKey, ciphertextAndTag);
}
