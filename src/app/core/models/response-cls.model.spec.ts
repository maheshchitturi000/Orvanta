import { HttpErrorResponse } from '@angular/common/http';
import { ApiError } from './response-cls.model';

describe('ApiError.from', () => {
  it('uses the ResponseCls StatusMessage and field errors', () => {
    const error = ApiError.from(
      new HttpErrorResponse({
        status: 400,
        error: { statusCode: 400, statusMessage: 'Please correct the highlighted fields.', statusObject: { email: ['Invalid'] } }
      })
    );

    expect(error.statusCode).toBe(400);
    expect(error.message).toBe('Please correct the highlighted fields.');
    expect(error.fieldErrors).toEqual({ email: ['Invalid'] });
  });

  it('keeps the payload for non-validation errors', () => {
    const error = ApiError.from(
      new HttpErrorResponse({
        status: 403,
        error: { statusCode: 403, statusMessage: 'Verify first', statusObject: { requiresVerification: true, email: 'a@b.c' } }
      })
    );

    expect(error.fieldErrors).toBeNull();
    expect(error.statusObject).toEqual({ requiresVerification: true, email: 'a@b.c' });
  });

  it('explains network failures', () => {
    const error = ApiError.from(new HttpErrorResponse({ status: 0 }));
    expect(error.statusCode).toBe(0);
    expect(error.message).toContain("Can't reach Orvanta");
  });
});
