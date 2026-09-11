export type LoginCredentials = {
  email: string;
  password: string;
  rememberMe: boolean;
};

export type RegistrationDetails = {
  displayName: string;
  email: string;
  password: string;
};

type ApiProblem = {
  title?: string;
  detail?: string;
  errors?: Record<string, string[] | string>;
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

async function post(path: string, body: unknown, fallbackMessage: string): Promise<void> {
  let response: Response;
  try {
    response = await fetch(path, {
      method: 'POST',
      credentials: 'include',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(body),
    });
  } catch {
    throw new Error('暂时无法连接身份服务，请稍后重试。');
  }

  if (response.ok) return;

  const problem = await readProblem(response);
  const message = response.status === 401
    ? '邮箱或密码不正确，或账户暂时不可用。'
    : firstValidationMessage(problem?.errors) ?? problem?.detail ?? problem?.title ?? fallbackMessage;

  throw new ApiError(message, response.status, problem);
}

export function login(credentials: LoginCredentials): Promise<void> {
  return post('/api/auth/login', credentials, '登录没有完成，请稍后重试。');
}

export async function registerAndLogin(details: RegistrationDetails): Promise<void> {
  await post('/api/auth/register', details, '注册没有完成，请稍后重试。');

  try {
    await login({ email: details.email, password: details.password, rememberMe: false });
  } catch {
    throw new Error('账户已创建，但自动登录失败，请返回登录页重试。');
  }
}
