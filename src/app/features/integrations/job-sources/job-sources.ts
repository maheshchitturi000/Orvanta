import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '../../../core/models/response-cls.model';
import { Icon } from '../../../shared/ui/icon/icon';
import { JobSourceStatus } from '../../jobs/jobs.models';
import { JobsService } from '../../jobs/jobs.service';

/** Integrations card: connect job search sites (LinkedIn · Naukri · Indeed via JSearch). */
@Component({
  selector: 'app-job-sources',
  imports: [DatePipe, FormsModule, Icon],
  templateUrl: './job-sources.html',
  styleUrl: './job-sources.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class JobSources implements OnInit {
  private readonly api = inject(JobsService);

  protected readonly sources = signal<JobSourceStatus[]>([]);
  protected readonly loading = signal(true);
  protected readonly connecting = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly apiKey = signal('');
  protected readonly showKey = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.sources.set((await this.api.sources()).statusObject ?? []);
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  protected readonly signUpUrl = 'https://rapidapi.com/letscrape-6bRBa3QguO5/api/jsearch';

  /** Opens the provider's sign-up page in a new tab and shows the paste-key form here. */
  protected startConnect(source: JobSourceStatus): void {
    window.open(this.signUpUrl, '_blank', 'noopener');
    this.connecting.set(source.name);
    this.apiKey.set('');
    this.error.set('');
    this.success.set('');
    setTimeout(() => {
      const input = document.getElementById('apiKey');
      input?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      input?.focus();
    });
  }

  protected cancel(): void {
    this.connecting.set(null);
    this.error.set('');
  }

  protected async connect(source: JobSourceStatus): Promise<void> {
    const key = this.apiKey().trim();
    if (key.length < 10) {
      this.error.set('Paste the full API key.');
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      const response = await this.api.connectSource(source.name, key);
      this.success.set(response.statusMessage);
      this.connecting.set(null);
      this.apiKey.set('');
      await this.load();
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async disconnect(source: JobSourceStatus): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.disconnectSource(source.name);
      this.success.set(`${source.displayName} disconnected.`);
      await this.load();
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
