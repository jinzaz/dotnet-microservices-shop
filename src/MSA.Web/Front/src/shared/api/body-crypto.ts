// 浏览器 ⇄ BFF 的请求/响应体加密：ECDH(P-256) + HKDF-SHA256 + AES-256-GCM，全部用浏览器原生
// WebCrypto，不引入额外的加密库。每次请求生成一个临时密钥对，跟 BFF 的静态公钥协商出共享密钥，
// 按方向派生出两个 AES 密钥（c2s 发请求、s2c 收响应），用完就扔——不是会话密钥，不用管过期/续期。
//
// 服务端实现见 src/BFFs/MSA.BFF.Common/BodyEncryption/BodyEncryptionMiddleware.cs，字节格式要跟这里完全对应：
//   - 公钥：未压缩点格式（0x04 || X(32) || Y(32)，65 字节），不是 SPKI/DER。
//   - 信封：nonce(12 字节) || ciphertext+tag（AES-GCM 的 tag 默认拼在密文末尾，不用分开传）。
//   - HKDF info：请求方向 "msa-body-c2s"，响应方向 "msa-body-s2c"，没有 salt（空）。
//
// 服务端密钥会轮换：公钥接口的 X-Msa-Kid 响应头随请求带回；服务端回 X-Msa-Kid-Unknown 时重拉公钥再发一次。
import { bffUrl } from '../config/api';

export const ENCRYPTED_CONTENT_TYPE = 'application/vnd.msa.enc';
export const EPHEMERAL_KEY_HEADER = 'X-Msa-Ekey';
/** 服务端公钥标识：公钥接口在响应头里给出，加密请求原样带回，服务端轮换密钥期间据此选对密钥。 */
export const KEY_ID_HEADER = 'X-Msa-Kid';
/** 服务端认不出这个公钥标识（旧密钥已退役）：重拉公钥后重发一次即可。 */
const UNKNOWN_KEY_HEADER = 'X-Msa-Kid-Unknown';

const NONCE_LENGTH = 12;
const REQUEST_INFO = new TextEncoder().encode('msa-body-c2s');
const RESPONSE_INFO = new TextEncoder().encode('msa-body-s2c');

export type BodyEncryptionContext = {
  keyId: string | null;
  ephemeralPublicKeyRaw: ArrayBuffer;
  c2sKey: CryptoKey;
  s2cKey: CryptoKey;
};

type ServerPublicKey = { keyId: string | null; key: CryptoKey };

let cachedServerPublicKey: Promise<ServerPublicKey> | undefined;

/** 懒加载并缓存 BFF 的 ECDH 公钥；并发请求共用同一次拉取，不会各发一遍。拉取失败不缓存，下次请求重试。 */
function getServerPublicKey(): Promise<ServerPublicKey> {
  cachedServerPublicKey ??= (async () => {
    const response = await fetch(bffUrl('/bff/security/public-key'), { cache: 'no-store' });
    if (!response.ok) throw new Error('无法获取服务端加密公钥。');
    const raw = await response.arrayBuffer();
    const key = await crypto.subtle.importKey('raw', raw, { name: 'ECDH', namedCurve: 'P-256' }, false, []);
    return { keyId: response.headers.get(KEY_ID_HEADER), key };
  })().catch((error: unknown) => {
    cachedServerPublicKey = undefined;
    throw error;
  });
  return cachedServerPublicKey;
}

/** 服务端轮换了密钥（响应带 X-Msa-Kid-Unknown）时清掉缓存，下次请求重新拉一次。 */
export function resetServerPublicKeyCache(): void {
  cachedServerPublicKey = undefined;
}

/** 服务端已经不认浏览器缓存的公钥了：调用方应 resetServerPublicKeyCache() 后用新密钥重发。 */
export function isUnknownKeyResponse(response: Response): boolean {
  return response.headers.has(UNKNOWN_KEY_HEADER);
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
  const { keyId, key: serverPublicKey } = await getServerPublicKey();
  const ephemeral = await crypto.subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
  const ephemeralPublicKeyRaw = await crypto.subtle.exportKey('raw', ephemeral.publicKey);
  const sharedBits = await crypto.subtle.deriveBits({ name: 'ECDH', public: serverPublicKey }, ephemeral.privateKey, 256);

  const [c2sKey, s2cKey] = await Promise.all([
    deriveAesKey(sharedBits, REQUEST_INFO),
    deriveAesKey(sharedBits, RESPONSE_INFO),
  ]);

  return { keyId, ephemeralPublicKeyRaw, c2sKey, s2cKey };
}

function base64FromArrayBuffer(buffer: ArrayBuffer): string {
  return btoa(String.fromCharCode(...new Uint8Array(buffer)));
}

/** 写上加密请求要带的头：X-Msa-Ekey（临时公钥）——GET 之类没有 body 的请求也要带，响应才能被加密；X-Msa-Kid（服务端公钥标识）。 */
export function applyBodyEncryptionHeaders(headers: Headers, context: BodyEncryptionContext): void {
  headers.set(EPHEMERAL_KEY_HEADER, base64FromArrayBuffer(context.ephemeralPublicKeyRaw));
  if (context.keyId) headers.set(KEY_ID_HEADER, context.keyId);
  else headers.delete(KEY_ID_HEADER);
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
