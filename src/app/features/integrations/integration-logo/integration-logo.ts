import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Icon } from '../../../shared/ui/icon/icon';
import { CatalogEntry } from '../integration-catalog';

/** Brand mark of an integration (letter tiles, or an icon for entries without one). */
@Component({
  selector: 'app-integration-logo',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-id]': 'entry().id', '[style.--size.px]': 'size()', 'aria-hidden': 'true' },
  template: `
    @if (entry().icon; as icon) {
      <span class="tile icon"><app-icon [name]="icon" [size]="size() * 0.42" /></span>
    } @else {
      @for (letter of entry().logo; track $index) {
        <span class="tile" [attr.data-index]="$index">{{ letter }}</span>
      }
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      flex-shrink: 0;
    }

    .tile {
      display: grid;
      place-items: center;
      width: var(--size);
      height: var(--size);
      border-radius: calc(var(--size) * 0.28);
      background: var(--lavender-100);
      color: var(--lavender-700);
      font-size: calc(var(--size) * 0.42);
      font-weight: 800;
      line-height: 1;
    }

    :host([data-id='swiggy']) .tile {
      background: linear-gradient(135deg, #ff9a3c, #fc8019);
      color: #fff;
    }

    :host([data-id='zomato']) .tile {
      background: linear-gradient(135deg, #ef4f5f, #cb202d);
      color: #fff;
    }

    :host([data-id='ondc']) .tile {
      background: linear-gradient(135deg, #1d8bd1, #17a673);
      color: #fff;
    }

    /* Three overlapping tiles: LinkedIn, Indeed, Glassdoor. */
    :host([data-id='jsearch']) .tile {
      width: calc(var(--size) * 0.74);
      height: calc(var(--size) * 0.74);
      border: 2px solid var(--surface);
      color: #fff;
      font-size: calc(var(--size) * 0.3);

      & + .tile {
        margin-left: calc(var(--size) * -0.2);
      }

      &[data-index='0'] { background: #0a66c2; }
      &[data-index='1'] { background: #2557a7; }
      &[data-index='2'] { background: #0caa41; }
    }

    :host([data-id='adzuna']) .tile {
      background: linear-gradient(135deg, #3f8fd8, #2b6cb0);
      color: #fff;
    }

    :host([data-id='jsearch']) {
      align-items: center;
    }
  `
})
export class IntegrationLogo {
  readonly entry = input.required<Pick<CatalogEntry, 'id' | 'logo' | 'icon'>>();
  readonly size = input(48);
}
