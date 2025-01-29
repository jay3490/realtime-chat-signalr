import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChatHubService } from '../../core/chat-hub.service';
import { ChatStore } from '../../core/chat-store.service';
import { PresenceDotComponent } from '../../shared/presence-dot.component';

/** Client-side spacing for typing notifications; the server throttles as well. */
const TYPING_PING_MS = 1000;

@Component({
  selector: 'app-room-view',
  imports: [FormsModule, DatePipe, PresenceDotComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.activeRoom(); as room) {
      <header class="room-header">
        @if (room.kind === 'Direct') {
          <app-presence-dot [online]="store.isOnline(room.name)" />
          <h2>{{ room.name }}</h2>
        } @else {
          <h2># {{ room.name }}</h2>
        }
      </header>

      <section class="messages" #scroller (scroll)="onScroll()" aria-live="polite">
        @if (store.activeHasMore()) {
          <button class="link load-older" (click)="loadOlder()" [disabled]="store.loadingOlder()">
            {{ store.loadingOlder() ? 'Loading...' : 'Load older messages' }}
          </button>
        }
        @for (message of store.activeMessages(); track message.id) {
          <article class="message" [class.mine]="message.sender === me()">
            <div class="meta">
              <span class="sender">{{ message.sender }}</span>
              <time [attr.datetime]="message.sentAt">{{ message.sentAt | date: 'MMM d, HH:mm' }}</time>
            </div>
            <p class="body">{{ message.body }}</p>
          </article>
        } @empty {
          <p class="muted empty">No messages yet. Say hello.</p>
        }
        @if (seenBy()) {
          <p class="seen">{{ seenBy() }}</p>
        }
      </section>

      <p class="typing" aria-live="polite">{{ typingText() }}</p>

      <form class="composer" (ngSubmit)="send()">
        <textarea
          name="draft"
          rows="2"
          [(ngModel)]="draft"
          (ngModelChange)="onDraftChanged()"
          (keydown.enter)="onEnter($event)"
          [placeholder]="'Message ' + (room.kind === 'Direct' ? room.name : '#' + room.name)"
          [disabled]="hub.state() !== 'connected'"
          maxlength="4000"
        ></textarea>
        <button type="submit" class="primary" [disabled]="!canSend()">Send</button>
      </form>
      @if (sendError()) {
        <p class="error" role="alert">{{ sendError() }}</p>
      }
    } @else {
      <div class="placeholder muted">Choose a conversation, join a channel or message someone.</div>
    }
  `,
})
export class RoomViewComponent {
  protected readonly store = inject(ChatStore);
  protected readonly hub = inject(ChatHubService);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  protected draft = '';
  private readonly draftLength = signal(0);
  private lastTypingPing = 0;
  private stickToBottom = true;
  private previousFirstId: number | undefined;
  private previousHeight = 0;

  protected readonly sendError = signal<string | null>(null);
  protected readonly me = computed(() => this.store.me());
  protected readonly canSend = computed(() => this.draftLength() > 0 && this.hub.state() === 'connected');

  protected readonly typingText = computed(() => {
    const users = this.store.activeTyping();
    if (users.length === 0) {
      return '';
    }
    if (users.length === 1) {
      return `${users[0]} is typing...`;
    }
    return `${users.slice(0, -1).join(', ')} and ${users.at(-1)} are typing...`;
  });

  protected readonly seenBy = computed(() => {
    const users = this.store.activeSeenBy();
    if (users.length === 0) {
      return '';
    }
    return this.store.activeRoom()?.kind === 'Direct' ? 'Seen' : `Seen by ${users.join(', ')}`;
  });

  constructor() {
    afterRenderEffect(() => {
      const messages = this.store.activeMessages();
      const element = this.scroller()?.nativeElement;
      if (!element) {
        return;
      }
      const firstId = messages[0]?.id;
      if (this.previousFirstId !== undefined && firstId !== undefined && firstId < this.previousFirstId) {
        // Older messages were prepended: keep the reader's place instead of jumping.
        element.scrollTop += element.scrollHeight - this.previousHeight;
      } else if (this.stickToBottom) {
        element.scrollTop = element.scrollHeight;
      }
      this.previousFirstId = firstId;
      this.previousHeight = element.scrollHeight;
    });
  }

  protected onScroll(): void {
    const element = this.scroller()?.nativeElement;
    if (element) {
      this.stickToBottom = element.scrollHeight - element.scrollTop - element.clientHeight < 40;
    }
  }

  protected loadOlder(): void {
    this.previousHeight = this.scroller()?.nativeElement.scrollHeight ?? 0;
    void this.store.loadOlder();
  }

  protected onDraftChanged(): void {
    const roomId = this.store.activeRoomId();
    this.draftLength.set(this.draft.trim().length);
    if (!roomId || this.hub.state() !== 'connected') {
      return;
    }
    if (this.draft.trim().length === 0) {
      this.lastTypingPing = 0;
      void this.hub.stopTyping(roomId).catch(() => undefined);
      return;
    }
    const now = Date.now();
    if (now - this.lastTypingPing >= TYPING_PING_MS) {
      this.lastTypingPing = now;
      void this.hub.startTyping(roomId).catch(() => undefined);
    }
  }

  protected onEnter(event: Event): void {
    if (!(event as KeyboardEvent).shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }

  protected async send(): Promise<void> {
    const body = this.draft.trim();
    if (!body || !this.canSend()) {
      return;
    }
    this.sendError.set(null);
    try {
      await this.store.send(body);
      this.draft = '';
      this.draftLength.set(0);
      this.lastTypingPing = 0;
      this.stickToBottom = true;
    } catch (error) {
      this.sendError.set(error instanceof Error ? error.message : 'The message could not be sent.');
    }
  }
}
