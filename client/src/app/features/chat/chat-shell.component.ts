import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ChatApiService } from '../../core/chat-api.service';
import { ChatHubService } from '../../core/chat-hub.service';
import { ChatStore } from '../../core/chat-store.service';
import { Channel, RoomSummary } from '../../core/models';
import { PresenceDotComponent } from '../../shared/presence-dot.component';
import { RoomViewComponent } from './room-view.component';

type Panel = 'rooms' | 'channels' | 'people';

@Component({
  selector: 'app-chat-shell',
  imports: [FormsModule, RouterLink, PresenceDotComponent, RoomViewComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <aside class="sidebar">
        <div class="me">
          <div>
            <strong>{{ store.me() }}</strong>
            <span class="status" [attr.data-state]="hub.state()">{{ hub.state() }}</span>
          </div>
          <button class="link" (click)="signOut()">Sign out</button>
        </div>

        <nav class="tabs" aria-label="Sidebar">
          <button [class.active]="panel() === 'rooms'" (click)="panel.set('rooms')">
            Rooms
            @if (store.totalUnread() > 0) {
              <span class="badge">{{ store.totalUnread() }}</span>
            }
          </button>
          <button [class.active]="panel() === 'channels'" (click)="showChannels()">Channels</button>
          <button [class.active]="panel() === 'people'" (click)="showPeople()">People</button>
        </nav>

        @switch (panel()) {
          @case ('rooms') {
            <ul class="room-list">
              @for (room of store.rooms(); track room.id) {
                <li>
                  <a [routerLink]="['/chat', room.id]" [class.active]="room.id === roomId()">
                    @if (room.kind === 'Direct') {
                      <app-presence-dot [online]="store.isOnline(room.name)" />
                      <span class="name">{{ room.name }}</span>
                    } @else {
                      <span class="hash">#</span>
                      <span class="name">{{ room.name }}</span>
                    }
                    @if (room.unreadCount > 0) {
                      <span class="badge" [attr.aria-label]="room.unreadCount + ' unread'">{{ room.unreadCount }}</span>
                    }
                  </a>
                </li>
              } @empty {
                <li class="muted hint">You are not in any rooms yet. Join a channel or message someone.</li>
              }
            </ul>
          }
          @case ('channels') {
            <ul class="room-list">
              @for (channel of channels(); track channel.id) {
                <li class="row">
                  <span class="name"># {{ channel.name }}</span>
                  <span class="muted small">{{ channel.memberCount }}</span>
                  @if (channel.isMember) {
                    <a class="link" [routerLink]="['/chat', channel.id]">Open</a>
                  } @else {
                    <button class="link" (click)="join(channel)">Join</button>
                  }
                </li>
              }
            </ul>
            <form class="inline-form" (ngSubmit)="createChannel()">
              <input name="channelName" [(ngModel)]="newChannel" placeholder="new-channel" maxlength="32" />
              <button type="submit" [disabled]="newChannel.trim().length < 2">Create</button>
            </form>
          }
          @case ('people') {
            <ul class="room-list">
              @for (user of otherUsers(); track user) {
                <li class="row">
                  <app-presence-dot [online]="store.isOnline(user)" />
                  <span class="name">{{ user }}</span>
                  <button class="link" (click)="message(user)">Message</button>
                </li>
              } @empty {
                <li class="muted hint">Nobody else has signed in yet.</li>
              }
            </ul>
          }
        }

        @if (sidebarError()) {
          <p class="error" role="alert">{{ sidebarError() }}</p>
        }
        @if (hub.serverInstance(); as instance) {
          <p class="muted small instance">Connected via {{ instance }}</p>
        }
      </aside>

      <main class="room">
        <app-room-view />
      </main>
    </div>
  `,
})
export class ChatShellComponent implements OnInit {
  protected readonly store = inject(ChatStore);
  protected readonly hub = inject(ChatHubService);
  private readonly api = inject(ChatApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Bound from the route (`/chat/:roomId`). */
  readonly roomId = input<string>();

  protected readonly panel = signal<Panel>('rooms');
  protected readonly channels = signal<Channel[]>([]);
  private readonly users = signal<string[]>([]);
  protected readonly sidebarError = signal<string | null>(null);
  protected newChannel = '';

  protected readonly otherUsers = computed(() => this.users().filter((u) => u !== this.store.me()));

  constructor() {
    effect(() => {
      const id = this.roomId();
      if (id) {
        void this.store.open(id).catch((e: unknown) => this.showError(e));
      } else {
        this.store.close();
      }
    });

    const onVisible = () => {
      if (document.visibilityState === 'visible') {
        void this.store.markActiveRead().catch(() => undefined);
      }
    };
    document.addEventListener('visibilitychange', onVisible);
    this.destroyRef.onDestroy(() => document.removeEventListener('visibilitychange', onVisible));
  }

  async ngOnInit(): Promise<void> {
    try {
      await this.hub.start();
      await this.store.load();
    } catch (e) {
      this.showError(e);
    }
  }

  protected async showChannels(): Promise<void> {
    this.panel.set('channels');
    await this.run(async () => this.channels.set(await this.api.channels()));
  }

  protected async showPeople(): Promise<void> {
    this.panel.set('people');
    await this.run(async () => this.users.set(await this.api.users()));
  }

  protected async join(channel: Channel): Promise<void> {
    await this.run(async () => {
      const room = await this.store.joinChannel(channel.id);
      this.channels.update((all) => all.map((c) => (c.id === channel.id ? { ...c, isMember: true, memberCount: c.memberCount + 1 } : c)));
      await this.openRoom(room);
    });
  }

  protected async createChannel(): Promise<void> {
    await this.run(async () => {
      const room = await this.store.createChannel(this.newChannel);
      this.newChannel = '';
      await this.openRoom(room);
    });
  }

  protected async message(userName: string): Promise<void> {
    await this.run(async () => this.openRoom(await this.store.openDirect(userName)));
  }

  protected async signOut(): Promise<void> {
    await this.hub.stop();
    this.store.reset();
    this.auth.signOut();
    await this.router.navigate(['/login']);
  }

  private async openRoom(room: RoomSummary): Promise<void> {
    this.panel.set('rooms');
    await this.router.navigate(['/chat', room.id]);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.sidebarError.set(null);
    try {
      await action();
    } catch (e) {
      this.showError(e);
    }
  }

  private showError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      const title = (error.error as { title?: string } | null)?.title;
      this.sidebarError.set(title ?? `Request failed (${error.status}).`);
    } else {
      this.sidebarError.set(error instanceof Error ? error.message : 'Something went wrong.');
    }
  }
}
