import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Icon, IconName } from '../../../shared/ui/icon/icon';
import { JobsState } from '../jobs-state.service';

interface Tab {
  label: string;
  link: string;
  icon: IconName;
  badge?: () => number;
}

/** Frame for the Jobs pages: automation status, counters and tabs. */
@Component({
  selector: 'app-jobs-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe, Icon],
  templateUrl: './jobs-layout.html',
  styleUrl: './jobs-layout.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class JobsLayout implements OnInit {
  protected readonly state = inject(JobsState);

  protected readonly tabs: Tab[] = [
    { label: 'Matches', link: 'matches', icon: 'search', badge: () => this.state.summary()?.newMatches ?? 0 },
    { label: 'Applications', link: 'applications', icon: 'send', badge: () => this.state.summary()?.readyToSubmit ?? 0 },
    { label: 'CV & preferences', link: 'profile', icon: 'user' }
  ];

  ngOnInit(): void {
    void this.state.refresh();
  }
}
