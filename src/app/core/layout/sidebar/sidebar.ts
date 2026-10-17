import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthService } from '../../auth/auth.service';
import { ConversationStore } from '../../../features/chat/conversation.store';
import { Icon, IconName } from '../../../shared/ui/icon/icon';
import { LayoutService } from '../layout.service';

interface NavItem {
  label: string;
  icon: IconName;
  link: string;
  /** Extra URL prefixes that should also mark this item active. */
  matches?: string[];
}

@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, Icon],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Sidebar implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly layout = inject(LayoutService);
  protected readonly store = inject(ConversationStore);

  protected readonly user = this.auth.user;
  protected readonly initials = computed(() =>
    (this.user()?.fullName ?? '?')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0]!.toUpperCase())
      .join('')
  );

  private readonly url = toSignal(
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      map(event => event.urlAfterRedirects.split('?')[0])
    ),
    { initialValue: this.router.url.split('?')[0] }
  );

  protected readonly nav: NavItem[] = [
    { label: 'AI Assistant', icon: 'sparkles', link: '/', matches: ['/c/'] },
    { label: 'Jobs', icon: 'briefcase', link: '/jobs', matches: ['/jobs/'] },
    { label: 'Integrations', icon: 'apps', link: '/integrations' }
  ];

  ngOnInit(): void {
    void this.store.refresh();
  }

  protected isActive(item: NavItem): boolean {
    const url = this.url();
    return url === item.link || (item.matches ?? []).some(prefix => url.startsWith(prefix));
  }

  protected newConversation(): void {
    this.store.startNew();
    void this.router.navigateByUrl('/');
    this.layout.closeSidebar();
  }

  protected openConversation(conversationId: string): void {
    void this.router.navigate(['/c', conversationId]);
    this.layout.closeSidebar();
  }

  protected close(): void {
    this.layout.closeSidebar();
  }

  protected signOut(): void {
    void this.auth.logout();
  }
}
