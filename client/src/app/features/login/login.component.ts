import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="login">
      <form class="login-card" (ngSubmit)="submit()">
        <h1>Realtime chat</h1>
        <p class="muted">
          Pick a user name to sign in. This uses the API's development token endpoint, so there
          is no password.
        </p>
        <label for="userName">User name</label>
        <input
          id="userName"
          name="userName"
          autocomplete="username"
          [(ngModel)]="userName"
          required
          minlength="2"
          maxlength="32"
          pattern="[A-Za-z0-9][A-Za-z0-9._\\-]*"
          [disabled]="busy()"
          autofocus
        />
        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
        }
        <button type="submit" class="primary" [disabled]="busy() || userName.trim().length < 2">
          {{ busy() ? 'Signing in...' : 'Sign in' }}
        </button>
      </form>
    </main>
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected userName = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.signIn(this.userName.trim());
      await this.router.navigate(['/chat']);
    } catch (e) {
      this.error.set(describeError(e));
    } finally {
      this.busy.set(false);
    }
  }
}

function describeError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const title = (error.error as { title?: string } | null)?.title;
    return title ?? (error.status === 0 ? 'The server cannot be reached.' : `Sign-in failed (${error.status}).`);
  }
  return 'Sign-in failed.';
}
