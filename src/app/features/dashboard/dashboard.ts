import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ApiError } from '../../core/models/response-cls.model';
import { Icon } from '../../shared/ui/icon/icon';
import { ConversationStore } from '../chat/conversation.store';
import { IntegrationsStore } from '../integrations/integrations.store';
import { ActivityService, ActivitySummaryRow, niceMax, toAreas, toDays } from './activity.service';

const DAYS = 14;

/** Signed-in home: totals plus the user's activity as charts (per day, and per area). */
@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, RouterLink, Icon],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Dashboard implements OnInit {
  private readonly activityApi = inject(ActivityService);
  protected readonly auth = inject(AuthService);
  protected readonly conversations = inject(ConversationStore);
  protected readonly integrations = inject(IntegrationsStore);

  private readonly rows = signal<ActivitySummaryRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  /** Bars start at zero and grow once data is in (set a frame after render so the transition runs). */
  protected readonly grown = signal(false);
  protected readonly hovered = signal<string | null>(null);

  protected readonly days = computed(() => toDays(this.rows(), DAYS));
  protected readonly areas = computed(() => toAreas(this.rows()));
  protected readonly dayMax = computed(() => niceMax(Math.max(0, ...this.days().map(d => d.total))));
  protected readonly areaMax = computed(() => Math.max(1, ...this.areas().map(a => a.total)));
  protected readonly total = computed(() => this.days().reduce((sum, d) => sum + d.total, 0));
  protected readonly thisWeek = computed(() => this.days().slice(-7).reduce((sum, d) => sum + d.total, 0));
  protected readonly availableApps = computed(() => this.integrations.items().filter(i => i.state !== 'soon').length);

  ngOnInit(): void {
    void this.load();
    void this.integrations.load();
    if (!this.conversations.conversations().length) {
      void this.conversations.refresh();
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    this.grown.set(false);
    try {
      this.rows.set(await this.activityApi.summary(DAYS));
      requestAnimationFrame(() => requestAnimationFrame(() => this.grown.set(true)));
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  protected barHeight(total: number): string {
    return this.grown() ? `${(total / this.dayMax()) * 100}%` : '0%';
  }

  /** Floats just above the bar; capped so a tall bar's tooltip stays inside the plot. */
  protected tooltipBottom(total: number): string {
    return `calc(${Math.min((total / this.dayMax()) * 100, 45)}% + 8px)`;
  }

  protected barWidth(total: number): string {
    return this.grown() ? `${(total / this.areaMax()) * 100}%` : '0%';
  }
}
