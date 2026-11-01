/**
 * API client for Zharfa backend.
 * Uses native fetch with same-origin credentials (cookie-based session).
 * No tokens, no client storage, no secrets.
 */

export class ApiError extends Error {
  readonly status: number;
  readonly isUnauthenticated: boolean;
  readonly data?: unknown;

  constructor(status: number, message: string, data?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.isUnauthenticated = status === 401;
    this.data = data;
  }
}

export function isUnauthenticated(error: unknown): boolean {
  return error instanceof ApiError && error.isUnauthenticated;
}

async function handleResponse<T>(res: Response): Promise<T> {
  if (res.ok) {
    if (res.status === 204) {
      return {} as T;
    }
    const contentType = res.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
      return (await res.json()) as T;
    }
    return (await res.text()) as unknown as T;
  }

  let errorMessage =
    res.status === 401
      ? 'احراز هویت انجام نشده است یا نشست منقضی شده است.'
      : 'خطایی در پردازش درخواست رخ داد.';
  let responseData: unknown;

  try {
    const contentType = res.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
      responseData = await res.json();
      if (typeof responseData === 'object' && responseData !== null) {
        const record = responseData as Record<string, unknown>;
        if (typeof record.message === 'string' && record.message.trim().length > 0) {
          errorMessage = record.message;
        } else if (typeof record.error === 'string' && record.error.trim().length > 0) {
          errorMessage = record.error;
        }
      }
    } else {
      const text = await res.text();
      if (text && text.trim().length > 0) {
        errorMessage = text;
      }
    }
  } catch {
    // If body parsing fails, preserve the default Persian error message
  }

  throw new ApiError(res.status, errorMessage, responseData);
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(path, {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
    },
  });
  return handleResponse<T>(res);
}

export async function apiPost<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  return handleResponse<T>(res);
}
