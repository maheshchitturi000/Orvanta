import { isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, PLATFORM_ID, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map } from 'rxjs';
import { ConversationStore } from '../../../features/chat/conversation.store';
import { ThemeSwitcher } from '../../../shared/ui/theme-switcher/theme-switcher';
import { AuthService } from '../../auth/auth.service';
import { Icon, IconName } from '../../../shared/ui/icon/icon';
import { LayoutService } from '../layout.service';
import { HistoryGroup, groupConversations } from './history-groups';

interface NavItem {
  label: string;
  icon: IconName;
  link: string;
  /** Extra URL prefixes that should also mark this item active. */
  matches?: string[];
}

/** Remembers which history sections are open, per browser. */
const SECTIONS_KEY = 'orv_history_sections';
/** Chat history starts collapsed; once opened, recent groups are expanded and older months collapsed. */
const OPEN_BY_DEFAULT = new Set(['today', 'yesterday', 'week']);
const SEARCH_THRESHOLD = 8;

@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, Icon, ThemeSwitcher],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Sidebar implements OnInit {
  private readonly router = inject(Router);
  private readonly layout = inject(LayoutService);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  protected readonly store = inject(ConversationStore);
  protected readonly auth = inject(AuthService);
  protected readonly user = this.auth.user;

  /** Account card at the bottom: expands upwards with contact details and appearance. */
  protected readonly accountOpen = signal(false);
  protected readonly phone = computed(() => {
    const phone = this.user()?.phoneNumber ?? '';
    return /^\d{10}$/.test(phone) ? `+91 ${phone.slice(0, 5)} ${phone.slice(5)}` : phone;
  });

  private readonly url = toSignal(
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      map(event => event.urlAfterRedirects.split('?')[0])
    ),
    { initialValue: this.router.url.split('?')[0] }
  );

  protected readonly nav: NavItem[] = [
    { label: 'Dashboard', icon: 'dashboard', link: '/dashboard' },
    { label: 'AI Assistant', icon: 'sparkles', link: '/', matches: ['/c/'] },
    { label: 'Jobs', icon: 'briefcase', link: '/jobs', matches: ['/jobs/'] },
    { label: 'Integrations', icon: 'apps', link: '/integrations', matches: ['/integrations/'] }
  ];

  protected readonly query = signal('');
  protected readonly showSearch = computed(() => this.store.conversations().length > SEARCH_THRESHOLD);
  private readonly overrides = signal<Record<string, boolean>>(this.readOverrides());

  protected readonly groups = computed<HistoryGroup[]>(() => {
    const term = this.query().trim().toLowerCase();
    const conversations = this.store.conversations();
    return groupConversations(term ? conversations.filter(c => c.title.toLowerCase().includes(term)) : conversations);
  });

  constructor() {
    // Opening a conversation from elsewhere (e.g. a link) expands the group that contains it.
    // Tracks only the active id and the list, so the user can still collapse that group afterwards.
    effect(() => {
      const activeId = this.store.activeId();
      const group = this.groups().find(g => g.items.some(c => c.conversationId === activeId));
      untracked(() => {
        if (group && !this.isOpen(group.key)) {
          this.setOpen(group.key, true);
        }
      });
    });
  }

  ngOnInit(): void {
    void this.store.refresh();
  }

  protected isActive(item: NavItem): boolean {
    const url = this.url();
    return url === item.link || (item.matches ?? []).some(prefix => url.startsWith(prefix));
  }

  /** While searching, every group is open so matches are never hidden. */
  protected isOpen(key: string): boolean {
    if (key !== 'recent' && this.query().trim()) {
      return true;
    }
    return this.overrides()[key] ?? OPEN_BY_DEFAULT.has(key);
  }

  protected toggle(key: string): void {
    this.setOpen(key, !this.isOpen(key));
  }

  /** AI Assistant always opens a fresh conversation; past ones are in Chat history. */
  protected onNav(item: NavItem): void {
    if (item.link === '/') {
      this.store.startNew();
    }
    this.layout.closeSidebar();
  }

  protected openConversation(conversationId: string): void {
    void this.router.navigate(['/c', conversationId]);
    this.layout.closeSidebar();
  }

  protected signOut(): void {
    void this.auth.logout();
  }

  protected close(): void {
    this.layout.closeSidebar();
  }

  private setOpen(key: string, open: boolean): void {
    this.overrides.update(all => ({ ...all, [key]: open }));
    if (this.isBrowser) {
      try {
        localStorage.setItem(SECTIONS_KEY, JSON.stringify(this.overrides()));
      } catch {
        // Storage unavailable: state still works for this visit.
      }
    }
  }

  private readOverrides(): Record<string, boolean> {
    if (!this.isBrowser) {
      return {};
    }
    try {
      const parsed: unknown = JSON.parse(localStorage.getItem(SECTIONS_KEY) ?? '{}');
      return parsed && typeof parsed === 'object' ? (parsed as Record<string, boolean>) : {};
    } catch {
      return {};
    }
  }
}
