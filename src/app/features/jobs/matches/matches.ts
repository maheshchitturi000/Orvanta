import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../../core/models/response-cls.model';
import { Icon } from '../../../shared/ui/icon/icon';
import { JobsState } from '../jobs-state.service';
import { JobMatch } from '../jobs.models';
import { JobsService } from '../jobs.service';

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

  protected readonly allSelected = computed(() => this.matches().length > 0 && this.selected().size === this.matches().length);
  protected readonly needsCv = computed(() => this.state.summary()?.hasCv === false);

  ngOnInit(): void {
    void this.load();
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
      const failedBoards = response.statusObject?.errors.length ?? 0;
      this.notice.set(response.statusMessage + (failedBoards ? ` (${failedBoards} board(s) were unavailable.)` : ''));
      await Promise.all([this.load(), this.state.refresh()]);
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.searching.set(false);
    }
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
