import { HttpErrorResponse } from '@angular/common/http';

/** Common envelope returned by every Orvanta microservice. */
export interface ResponseCls<T> {
  statusCode: number;
  statusMessage: string;
  statusObject: T | null;
}

export type FieldErrors = Record<string, string[]>;

/** Error thrown by ApiService. Carries the server's StatusMessage so the UI can show it as-is. */
export class ApiError extends Error {
  constructor(
    readonly statusCode: number,
    message: string,
    readonly statusObject: unknown = null,
    readonly fieldErrors: FieldErrors | null = null
  ) {
    super(message);
  }

  static from(error: unknown): ApiError {
    if (error instanceof ApiError) {
      return error;
    }

    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return new ApiError(0, "Can't reach Orvanta right now. Check your connection and that the Gateway is running.");
      }

      const body = error.error as Partial<ResponseCls<unknown>> | null;
      if (body && typeof body.statusMessage === 'string') {
        const fieldErrors = error.status === 400 && isFieldErrors(body.statusObject) ? body.statusObject : null;
        return new ApiError(error.status, body.statusMessage, body.statusObject ?? null, fieldErrors);
      }

      return new ApiError(error.status, `Request failed (HTTP ${error.status}).`);
    }

    return new ApiError(-1, 'Something went wrong. Please try again.');
  }
}

function isFieldErrors(value: unknown): value is FieldErrors {
  return (
    !!value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    Object.values(value as object).every(v => Array.isArray(v))
  );
}
