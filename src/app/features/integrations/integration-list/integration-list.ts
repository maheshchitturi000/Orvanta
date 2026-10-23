import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '../../../shared/ui/icon/icon';
import { CATEGORIES, IntegrationCategory, categoryInfo } from '../integration-catalog';
import { IntegrationLogo } from '../integration-logo/integration-logo';
import { IntegrationItem, IntegrationsStore } from '../integrations.store';

interface Section {
  key: IntegrationCategory;
  label: string;
  items: IntegrationItem[];
}

/** /integrations/all and /integrations/{food|jobs|travel}: cards that open each integration's page. */
@Component({
  selector: 'app-integration-list',
  imports: [RouterLink, Icon, IntegrationLogo],
  templateUrl: './integration-list.html',
  styleUrl: './integration-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class IntegrationList {
  protected readonly store = inject(IntegrationsStore);

  /** From the route's data. */
  readonly category = input<IntegrationCategory | 'all'>('all');

  protected readonly heading = computed(() => {
    const category = this.category();
    return category === 'all' ? 'All integrations' : categoryInfo(category).label;
  });

  protected readonly sections = computed<Section[]>(() => {
    const category = this.category();
    return CATEGORIES.filter(c => category === 'all' || c.key === category).map(c => ({
      key: c.key,
      label: c.label,
      items: this.store.byCategory(c.key)
    }));
  });

  protected readonly stats = computed(() => {
    const items = this.store.byCategory(this.category()).filter(i => i.state !== 'soon');
    return { connected: items.filter(i => i.state === 'connected').length, total: items.length };
  });

  protected refresh(): void {
    void this.store.load(true);
  }
}
