export type PasswordLoginCredentials = {
  email: string;
  password: string;
  rememberMe: boolean;
};

export type SmsLoginCredentials = {
  phoneNumber: string;
  code: string;
  rememberMe: boolean;
};

export type RegistrationDetails = {
  displayName: string;
  email: string;
  password: string;
};

export type SmsLoginCodeRequested = {
  expiresInSeconds: number;
  retryAfterSeconds: number;
};

type ApiProblem = {
  title?: string;
  detail?: string;
  errors?: Record<string, string[] | string>;
};

type AntiforgeryToken = {
  token: string;
};

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ApiProblem;

  constructor(message: string, status: number, problem?: ApiProblem) {
    super(message);
    this.status = status;
    this.problem = problem;
  }
}

let antiforgeryTokenPromise: Promise<string> | undefined;

function firstValidationMessage(errors?: ApiProblem['errors']): string | undefined {
  if (!errors) return undefined;

  for (const value of Object.values(errors)) {
    const message = Array.isArray(value) ? value[0] : value;
    if (message) return message;
  }

  return undefined;
}

async function readProblem(response: Response): Promise<ApiProblem | undefined> {
  if (!response.headers.get('content-type')?.includes('json')) return undefined;

  try {
    return await response.json() as ApiProblem;
  } catch {
    return undefined;
  }
}

function getAntiforgeryToken(): Promise<string> {
  antiforgeryTokenPromise ??= fetch('api/Auth/csrf', {
    credentials: 'include',
    headers: { Accept: 'application/json' },
    cache: 'no-store',
  })
    .then(async (response) => {
      console.log(response.url);
      if (!response.ok) throw new Error('无法建立安全会话，请刷新页面后重试。');
      const result = await response.json() as AntiforgeryToken;
      if (!result.token) throw new Error('无法建立安全会话，请刷新页面后重试。');
      return result.token;
    })
    .catch((error: unknown) => {
      antiforgeryTokenPromise = undefined;
      throw error;
    });

  return antiforgeryTokenPromise;
}

async function post<T = void>(
  path: string,
  body: unknown,
  fallbackMessage: string,
  unauthorizedMessage: string,
): Promise<T> {
  let response: Response;
  try {
    const antiforgeryToken = await getAntiforgeryToken();
    response = await fetch(path, {
      method: 'POST',
      credentials: 'include',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
        'X-XSRF-TOKEN': antiforgeryToken,
      },
      body: JSON.stringify(body),
    });
  } catch (error) {
    if (error instanceof Error && error.message) throw error;
    throw new Error('暂时无法连接身份服务，请稍后重试。');
  }

  if (response.ok) {
    if (response.status === 204) return undefined as T;
    return await response.json() as T;
  }

  const problem = await readProblem(response);
  const message = response.status === 401
    ? unauthorizedMessage
    : response.status === 429
      ? '请求过于频繁，请稍后再试。'
      : firstValidationMessage(problem?.errors) ?? problem?.detail ?? problem?.title ?? fallbackMessage;

  throw new ApiError(message, response.status, problem);
}

export function loginWithPassword(credentials: PasswordLoginCredentials): Promise<void> {
  return post(
    'api/Auth/login/password',
    credentials,
    '登录没有完成，请稍后重试。',
    '邮箱或密码不正确，或账户暂时不可用。',
  );
}

export function requestSmsLoginCode(phoneNumber: string): Promise<SmsLoginCodeRequested> {
  return post(
    'api/Auth/login/sms/code',
    { phoneNumber },
    '验证码发送请求没有完成，请稍后重试。',
    '验证码发送请求没有完成，请稍后重试。',
  );
}

export function loginWithSms(credentials: SmsLoginCredentials): Promise<void> {
  return post(
    'api/Auth/login/sms',
    credentials,
    '登录没有完成，请稍后重试。',
    '手机号或验证码不正确，或账户暂时不可用。',
  );
}

export async function registerAndLogin(details: RegistrationDetails): Promise<void> {
  await post(
    'api/Auth/register',
    details,
    '注册没有完成，请稍后重试。',
    '注册没有完成，请稍后重试。',
  );

  try {
    await loginWithPassword({
      email: details.email,
      password: details.password,
      rememberMe: false,
    });
  } catch {
    throw new Error('账户已创建，但自动登录失败，请返回登录页重试。');
  }
}
