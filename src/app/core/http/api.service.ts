import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../config/api.config';
import { ApiError, ResponseCls } from '../models/response-cls.model';

type Params = Record<string, string | number | boolean>;

/**
 * Thin typed wrapper over HttpClient for ResponseCls endpoints.
 * Resolves with the full envelope; rejects with ApiError.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get<T>(path: string, params?: Params): Promise<ResponseCls<T>> {
    return this.send(this.http.get<ResponseCls<T>>(this.url(path), { params: this.params(params) }));
  }

  post<T>(path: string, body: unknown = {}): Promise<ResponseCls<T>> {
    return this.send(this.http.post<ResponseCls<T>>(this.url(path), body));
  }

  put<T>(path: string, body: unknown = {}): Promise<ResponseCls<T>> {
    return this.send(this.http.put<ResponseCls<T>>(this.url(path), body));
  }

  delete<T>(path: string): Promise<ResponseCls<T>> {
    return this.send(this.http.delete<ResponseCls<T>>(this.url(path)));
  }

  private async send<T>(request: Observable<ResponseCls<T>>): Promise<ResponseCls<T>> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw ApiError.from(error);
    }
  }

  private url(path: string): string {
    return `${this.baseUrl}${path.startsWith('/') ? path : `/${path}`}`;
  }

  private params(params?: Params): HttpParams | undefined {
    return params ? new HttpParams({ fromObject: params }) : undefined;
  }
}
