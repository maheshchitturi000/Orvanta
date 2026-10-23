import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { ApiError } from '../../../core/models/response-cls.model';
import { Icon } from '../../../shared/ui/icon/icon';
import { ApplyLauncher } from '../apply-launcher/apply-launcher';
import { JobsState } from '../jobs-state.service';
import { ApplicationStatus, JobApplication } from '../jobs.models';
import { JobsService } from '../jobs.service';

type Filter = 'all' | 'active' | 'ReadyToSubmit' | 'Submitted' | 'Failed';

const STATUS_LABEL: Record<ApplicationStatus, string> = {
  Queued: 'Queued',
  Preparing: 'Preparing',
  ReadyToSubmit: 'Ready to submit',
  Submitted: 'Applied',
  Failed: 'Failed'
};

@Component({
  selector: 'app-job-applications',
  imports: [DatePipe, Icon, ApplyLauncher],
  templateUrl: './applications.html',
  styleUrl: './applications.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Applications implements OnInit {
  private readonly api = inject(JobsService);
  private readonly state = inject(JobsState);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly items = signal<JobApplication[]>([]);
  protected readonly filter = signal<Filter>('all');
  protected readonly open = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly copied = signal<string | null>(null);

  protected readonly filters: { key: Filter; label: string }[] = [
    { key: 'all', label: 'All' },
    { key: 'active', label: 'In progress' },
    { key: 'ReadyToSubmit', label: 'Ready' },
    { key: 'Submitted', label: 'Applied' },
    { key: 'Failed', label: 'Failed' }
  ];

  protected readonly visible = computed(() => {
    const f = this.filter();
    return this.items().filter(a =>
      f === 'all' ? true : f === 'active' ? a.status === 'Queued' || a.status === 'Preparing' : a.status === f
    );
  });

  protected readonly readyCount = computed(() => this.items().filter(a => a.status === 'ReadyToSubmit').length);
  protected readonly launching = signal(false);

  private readonly hasActive = computed(() => this.items().some(a => a.status === 'Queued' || a.status === 'Preparing'));

  ngOnInit(): void {
    void this.load();
    // While the background worker is preparing applications, refresh so statuses update live.
    const timer = setInterval(() => {
      if (this.hasActive()) {
        void this.load(true);
        void this.state.refresh();
      }
    }, 5000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  async load(silent = false): Promise<void> {
    if (!silent) this.loading.set(true);
    try {
      this.items.set((await this.api.applications()).statusObject ?? []);
      this.error.set('');
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  protected label(status: ApplicationStatus): string {
    return STATUS_LABEL[status];
  }

  protected toggle(id: string): void {
    this.open.update(current => (current === id ? null : id));
  }

  /** Fallback when the extension isn't installed: open the form and mark it applied manually. */
  protected async markSubmitted(application: JobApplication): Promise<void> {
    try {
      await this.api.markSubmitted(application.id);
      await Promise.all([this.load(true), this.state.refresh()]);
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    }
  }

  protected async copy(application: JobApplication): Promise<void> {
    try {
      await navigator.clipboard.writeText(application.coverLetter);
      this.copied.set(application.id);
      setTimeout(() => this.copied.set(null), 2000);
    } catch {
      this.error.set('Copy failed. Select the text and copy it manually.');
    }
  }
}
