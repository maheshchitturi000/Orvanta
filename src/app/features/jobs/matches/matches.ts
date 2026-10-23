import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../../core/models/response-cls.model';
import { Icon } from '../../../shared/ui/icon/icon';
import { JobsState } from '../jobs-state.service';
import { DiscoveryResult, JobMatch } from '../jobs.models';
import { JobsService } from '../jobs.service';

const SOURCE_LABELS: Record<string, string> = {
  linkedin: 'LinkedIn',
  naukri: 'Naukri',
  indeed: 'Indeed',
  glassdoor: 'Glassdoor',
  adzuna: 'Adzuna',
  foundit: 'Foundit',
  apnajobs: 'Apna',
  jobrapido: 'Jobrapido',
  bebee: 'BeBee',
  jobleads: 'JobLeads',
  company: 'company site',
  greenhouse: 'company site',
  lever: 'company site',
  ashby: 'company site',
  web: 'web'
};

/** Today's matches with one-click approval for the background apply workflow. */
@Component({
  selector: 'app-job-matches',
  imports: [DatePipe, RouterLink, Icon],
  templateUrl: './matches.html',
  styleUrl: './matches.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Matches implements OnInit {
  private readonly api = inject(JobsService);
  protected readonly state = inject(JobsState);

  protected readonly matches = signal<JobMatch[]>([]);
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly expanded = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly searching = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  /** Outcome of the last "Search now": hints and closest jobs when matches are few. */
  protected readonly lastSearch = signal<DiscoveryResult | null>(null);
  /** null = unknown yet; false shows the "Connect job sites" banner. */
  protected readonly jobSitesConnected = signal<boolean | null>(null);
  /** A Naukri search for the user's main role. Naukri offers no API, so its jobs are only linked to, never read. */
  protected readonly naukriUrl = signal<string | null>(null);

  protected readonly allSelected = computed(() => this.matches().length > 0 && this.selected().size === this.matches().length);
  protected readonly needsCv = computed(() => this.state.summary()?.hasCv === false);

  ngOnInit(): void {
    void this.load();
    this.api.sources()
      .then(r => this.jobSitesConnected.set((r.statusObject ?? []).some(s => s.connected)))
      .catch(() => this.jobSitesConnected.set(null));
    void this.loadNaukriLink();
  }

  private async loadNaukriLink(): Promise<void> {
    try {
      const [profile, preferences] = await Promise.all([this.api.profile(), this.api.preferences()]);
      const role = preferences.statusObject?.desiredTitles[0] || profile.statusObject?.currentTitle || '';
      if (!role.trim()) return;
      const slug = role.toLowerCase().replace(/\./g, 'dot ').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
      const years = Math.floor(profile.statusObject?.totalYearsExperience ?? 0);
      this.naukriUrl.set(`https://www.naukri.com/${slug}-jobs?k=${encodeURIComponent(role.trim())}&experience=${years}`);
    } catch {
      // Optional shortcut; the page works without it.
    }
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      this.matches.set((await this.api.matches('New')).statusObject ?? []);
      this.selected.set(new Set());
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  protected toggle(id: string): void {
    this.selected.update(current => {
      const next = new Set(current);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  protected toggleAll(): void {
    this.selected.set(this.allSelected() ? new Set() : new Set(this.matches().map(m => m.id)));
  }

  protected toggleDetails(id: string): void {
    this.expanded.update(current => (current === id ? null : id));
  }

  /** The single approval: everything (or the selection) goes to the background apply workflow. */
  protected async approve(all: boolean): Promise<void> {
    const ids = all ? [] : [...this.selected()];
    if (!all && ids.length === 0) return;

    await this.run(async () => {
      const response = await this.api.approve(ids, all);
      this.notice.set(response.statusMessage);
      await Promise.all([this.load(), this.state.refresh()]);
    });
  }

  protected async skip(match: JobMatch): Promise<void> {
    await this.run(async () => {
      await this.api.skip(match.id);
      this.matches.update(list => list.filter(m => m.id !== match.id));
      this.selected.update(set => {
        const next = new Set(set);
        next.delete(match.id);
        return next;
      });
      void this.state.refresh();
    });
  }

  /** Immediate search: the API scans the boards now and answers when done (~15 s). */
  protected async searchNow(): Promise<void> {
    this.searching.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      const response = await this.api.runDiscovery();
      this.lastSearch.set(response.statusObject);
      const failedBoards = response.statusObject?.errors.length ?? 0;
      this.notice.set(response.statusMessage + (failedBoards ? ` (${failedBoards} board(s) were unavailable.)` : ''));
      await Promise.all([this.load(), this.state.refresh()]);
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.searching.set(false);
    }
  }

  protected sourceLabel(source: string): string {
    return SOURCE_LABELS[source] ?? source.charAt(0).toUpperCase() + source.slice(1);
  }

  protected scoreClass(score: number): string {
    return score >= 90 ? 'great' : score >= 80 ? 'good' : 'ok';
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
