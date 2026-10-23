import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Icon } from '../../../shared/ui/icon/icon';
import { CATEGORIES, IntegrationCategory } from '../integration-catalog';
import { IntegrationLogo } from '../integration-logo/integration-logo';
import { IntegrationsStore } from '../integrations.store';

/**
 * Integrations area: a second-level navigation (All · Food · Jobs · Travel, with each app under its
 * category) next to the routed list or detail page. The main sidebar stays as it is.
 */
@Component({
  selector: 'app-integrations-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, IntegrationLogo],
  templateUrl: './integrations-layout.html',
  styleUrl: './integrations-layout.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class IntegrationsLayout implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly store = inject(IntegrationsStore);

  protected readonly categories = computed(() =>
    CATEGORIES.map(category => {
      const items = this.store.byCategory(category.key);
      return { ...category, items, connected: items.filter(i => i.state === 'connected').length };
    })
  );

  ngOnInit(): void {
    if (!this.handleOAuthReturn()) {
      void this.store.load();
    }
  }

  protected categoryLink(key: IntegrationCategory): string[] {
    return ['/integrations', key];
  }

  /**
   * FoodService redirects to /integrations?provider=swiggy&status=connected|error&message=… after the
   * provider's consent screen. Show the outcome on that provider's page.
   */
  private handleOAuthReturn(): boolean {
    const params = this.route.snapshot.queryParamMap;
    const provider = params.get('provider');
    const status = params.get('status');
    if (!provider || !status || !/^[a-z0-9-]+$/.test(provider)) {
      return false;
    }

    const name = provider.charAt(0).toUpperCase() + provider.slice(1);
    this.store.setFlash(
      status === 'connected'
        ? { kind: 'success', text: `${name} connected. You can now order from the AI Assistant.` }
        : { kind: 'error', text: params.get('message') ?? `${name} authorization was unsuccessful. Please try again.` }
    );
    void this.store.load(true);
    void this.router.navigate(['/integrations', 'food', provider], { replaceUrl: true });
    return true;
  }
}
