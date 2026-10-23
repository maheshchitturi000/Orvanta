import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map } from 'rxjs';
import { Icon, IconName } from '../../../shared/ui/icon/icon';
import { ThemeSwitcher } from '../../../shared/ui/theme-switcher/theme-switcher';
import { LayoutService } from '../layout.service';
import { UserMenu } from '../user-menu/user-menu';

/** Route `data` read by the top navbar. */
export interface PageMeta {
  title: string;
  subtitle?: string;
  icon?: IconName;
  /** Hide the "Connect apps" shortcut (e.g. on the integrations page itself). */
  hideConnect?: boolean;
}

@Component({
  selector: 'app-topbar',
  imports: [RouterLink, Icon, ThemeSwitcher, UserMenu],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Topbar {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly layout = inject(LayoutService);

  /** Metadata of the deepest active route, updated on every navigation. */
  protected readonly page = toSignal(
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      map(() => this.currentMeta())
    ),
    { initialValue: this.currentMeta() }
  );

  protected openMenu(): void {
    this.layout.openSidebar();
  }

  private currentMeta(): PageMeta {
    let route = this.route.snapshot;
    while (route.firstChild) {
      route = route.firstChild;
    }
    return (route.data as Partial<PageMeta>).title
      ? (route.data as PageMeta)
      : { title: 'Orvanta AI' };
  }
}
