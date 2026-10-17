import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config/api.config';
import { ApiService } from '../../core/http/api.service';
import { ApiError, ResponseCls } from '../../core/models/response-cls.model';
import {
  ApplicationStatus,
  ApproveResult,
  CandidateProfile,
  DiscoveryResult,
  JobApplication,
  JobMatch,
  JobPreferences,
  JobsSummary,
  MatchStatus
} from './jobs.models';

/** Client for the Jobs microservice (/api/jobs). */
@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  summary(): Promise<ResponseCls<JobsSummary>> {
    return this.api.get<JobsSummary>('/jobs/summary');
  }

  profile(): Promise<ResponseCls<CandidateProfile>> {
    return this.api.get<CandidateProfile>('/jobs/profile');
  }

  updateProfile(profile: Partial<CandidateProfile>): Promise<ResponseCls<CandidateProfile>> {
    return this.api.put<CandidateProfile>('/jobs/profile', profile);
  }

  /** Multipart upload; goes through HttpClient directly so the browser sets the boundary. */
  async uploadCv(file: File): Promise<ResponseCls<CandidateProfile>> {
    const body = new FormData();
    body.append('file', file, file.name);
    try {
      return await firstValueFrom(this.http.post<ResponseCls<CandidateProfile>>(`${this.baseUrl}/jobs/profile/cv`, body));
    } catch (error) {
      throw ApiError.from(error);
    }
  }

  preferences(): Promise<ResponseCls<JobPreferences>> {
    return this.api.get<JobPreferences>('/jobs/preferences');
  }

  savePreferences(preferences: JobPreferences): Promise<ResponseCls<JobPreferences>> {
    return this.api.put<JobPreferences>('/jobs/preferences', preferences);
  }

  suggestedBoards(): Promise<ResponseCls<string[]>> {
    return this.api.get<string[]>('/jobs/boards/suggested');
  }

  /** Scans the career boards now and returns the outcome (not queued). */
  runDiscovery(): Promise<ResponseCls<DiscoveryResult>> {
    return this.api.post<DiscoveryResult>('/jobs/discovery/run');
  }

  matches(status: MatchStatus = 'New'): Promise<ResponseCls<JobMatch[]>> {
    return this.api.get<JobMatch[]>('/jobs/matches', { status });
  }

  approve(matchIds: string[], all = false): Promise<ResponseCls<ApproveResult>> {
    return this.api.post<ApproveResult>('/jobs/matches/approve', { matchIds, all });
  }

  skip(matchId: string): Promise<ResponseCls<unknown>> {
    return this.api.post(`/jobs/matches/${matchId}/skip`);
  }

  applications(status?: ApplicationStatus): Promise<ResponseCls<JobApplication[]>> {
    return this.api.get<JobApplication[]>('/jobs/applications', status ? { status } : undefined);
  }

  markSubmitted(applicationId: string): Promise<ResponseCls<unknown>> {
    return this.api.post(`/jobs/applications/${applicationId}/submitted`);
  }
}
