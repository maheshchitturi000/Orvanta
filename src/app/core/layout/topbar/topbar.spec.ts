import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Topbar } from './topbar';

describe('Topbar', () => {
  it('renders the theme switcher and the profile button', async () => {
    TestBed.configureTestingModule({
      imports: [Topbar],
      providers: [provideRouter([]), provideHttpClient()]
    });

    const fixture = TestBed.createComponent(Topbar);
    fixture.detectChanges();
    await fixture.whenStable();

    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('app-theme-switcher button')).not.toBeNull();
    expect(host.querySelector('app-user-menu .trigger')).not.toBeNull();
  });
});
