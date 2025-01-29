import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { IssuedToken } from './models';

const STORAGE_KEY = 'realtime-chat.session';

/**
 * Holds the signed-in user's token. Tokens come from the API's development token
 * endpoint, which issues one for any user name; there is no password.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly session = signal<IssuedToken | null>(readStoredSession());

  readonly userName = computed(() => this.session()?.userName ?? null);
  readonly isSignedIn = computed(() => this.session() !== null);

  get accessToken(): string | null {
    const current = this.session();
    if (current && new Date(current.expiresAt).getTime() <= Date.now()) {
      this.signOut();
      return null;
    }
    return current?.accessToken ?? null;
  }

  async signIn(userName: string): Promise<void> {
    const token = await firstValueFrom(this.http.post<IssuedToken>('/api/dev/token', { userName }));
    this.session.set(token);
    writeStoredSession(token);
  }

  signOut(): void {
    this.session.set(null);
    writeStoredSession(null);
  }
}

function readStoredSession(): IssuedToken | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const token = JSON.parse(raw) as IssuedToken;
    return new Date(token.expiresAt).getTime() > Date.now() ? token : null;
  } catch {
    return null;
  }
}

function writeStoredSession(token: IssuedToken | null): void {
  try {
    if (token) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(token));
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
  } catch {
    // Storage can be unavailable (private mode, blocked site data); the session then
    // simply lasts until the page is closed.
  }
}
