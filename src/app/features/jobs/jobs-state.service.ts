import { Injectable, inject, signal } from '@angular/core';
import { JobsSummary } from './jobs.models';
import { JobsService } from './jobs.service';

/** Summary counters shared by the Jobs tabs (badges, empty states, "next step" hints). */
@Injectable({ providedIn: 'root' })
export class JobsState {
  private readonly api = inject(JobsService);
  private readonly _summary = signal<JobsSummary | null>(null);

  readonly summary = this._summary.asReadonly();

  async refresh(): Promise<void> {
    try {
      this._summary.set((await this.api.summary()).statusObject);
    } catch {
      // Summary is decorative; pages show their own errors.
    }
  }
}
