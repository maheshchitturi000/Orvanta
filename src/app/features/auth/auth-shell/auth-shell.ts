import { Component, input } from '@angular/core';

/** Two-panel layout shared by login, register and email verification. */
@Component({
  selector: 'app-auth-shell',
  templateUrl: './auth-shell.html',
  styleUrl: './auth-shell.scss'
})
export class AuthShell {
  readonly heading = input.required<string>();
  readonly subheading = input<string>('');
}
