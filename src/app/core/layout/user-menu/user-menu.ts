import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { Icon } from '../../../shared/ui/icon/icon';
import { ThemeSwitcher } from '../../../shared/ui/theme-switcher/theme-switcher';
import { AuthService } from '../../auth/auth.service';

/** Avatar button in the top navbar that opens the signed-in user's profile panel. */
@Component({
  selector: 'app-user-menu',
  imports: [RouterLink, Icon, ThemeSwitcher],
  templateUrl: './user-menu.html',
  styleUrl: './user-menu.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'close()'
  }
})
export class UserMenu {
  private readonly auth = inject(AuthService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly user = this.auth.user;
  protected readonly open = signal(false);

  protected readonly initials = this.auth.initials;

  protected readonly phone = computed(() => {
    const phone = this.user()?.phoneNumber ?? '';
    return /^\d{10}$/.test(phone) ? `+91 ${phone.slice(0, 5)} ${phone.slice(5)}` : phone;
  });

  constructor() {
    inject(Router)
      .events.pipe(filter(event => event instanceof NavigationStart), takeUntilDestroyed())
      .subscribe(() => this.close());
  }

  protected toggle(): void {
    this.open.update(open => !open);
  }

  protected close(): void {
    this.open.set(false);
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }

  protected signOut(): void {
    this.close();
    void this.auth.logout();
  }
}
