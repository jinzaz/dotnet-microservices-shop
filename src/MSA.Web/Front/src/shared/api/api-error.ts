export type ApiProblem = {
  title?: string;
  detail?: string;
  error?: string;
  error_description?: string;
  errors?: Record<string, string[] | string>;
};

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ApiProblem;

  constructor(message: string, status: number, problem?: ApiProblem) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

function firstValidationMessage(errors?: ApiProblem['errors']): string | undefined {
  if (!errors) return undefined;

  for (const value of Object.values(errors)) {
    if (Array.isArray(value) && value[0]) return value[0];
    if (typeof value === 'string' && value) return value;
  }

  return undefined;
}

export async function toApiError(response: Response, fallbackMessage: string): Promise<ApiError> {
  let problem: ApiProblem | undefined;

  try {
    problem = (await response.json()) as ApiProblem;
  } catch {
    problem = undefined;
  }

  const unauthorizedMessage = response.status === 401
    || (response.status === 403 && problem?.error === 'invalid_grant')
    ? '邮箱或密码不正确，请重新输入。'
    : undefined;
  const message =
    firstValidationMessage(problem?.errors) ??
    unauthorizedMessage ??
    problem?.error_description ??
    problem?.detail ??
    problem?.title ??
    fallbackMessage;

  return new ApiError(message, response.status, problem);
}

export function getErrorMessage(error: unknown, fallback = '操作没有完成，请稍后重试。'): string {
  if (error instanceof Error && error.message) return error.message;
  return fallback;
}
