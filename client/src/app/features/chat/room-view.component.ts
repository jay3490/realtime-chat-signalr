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
import { mentions, splitMentions } from '../../core/mentions';
import { Message } from '../../core/models';
import { PresenceDotComponent } from '../../shared/presence-dot.component';

/** Client-side spacing for typing notifications; the server throttles as well. */
const TYPING_PING_MS = 1000;

/** Matches the server's edit window; the server is what enforces it. */
const EDIT_WINDOW_MS = 24 * 60 * 60 * 1000;

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
          <article
            class="message"
            [class.mine]="message.sender === me()"
            [class.deleted]="message.isDeleted"
            [class.mentions-me]="mentionsMe(message)"
          >
            <div class="meta">
              <span class="sender">{{ message.sender }}</span>
              <time [attr.datetime]="message.sentAt">{{ message.sentAt | date: 'MMM d, HH:mm' }}</time>
              @if (message.editedAt && !message.isDeleted) {
                <span class="edited" [attr.title]="'Edited ' + (message.editedAt | date: 'MMM d, HH:mm')">(edited)</span>
              }
              @if (message.sender === me() && !message.isDeleted && editingId() !== message.id) {
                <span class="actions">
                  @if (canEdit(message)) {
                    <button class="link" (click)="startEdit(message)">Edit</button>
                  }
                  <button class="link danger" (click)="remove(message)">Delete</button>
                </span>
              }
            </div>
            @if (message.isDeleted) {
              <p class="body muted">This message was deleted.</p>
            } @else if (editingId() === message.id) {
              <form class="edit-form" (ngSubmit)="saveEdit(message)">
                <textarea
                  name="editDraft"
                  rows="2"
                  [(ngModel)]="editDraft"
                  (keydown.escape)="cancelEdit()"
                  (keydown.enter)="onEditEnter($event, message)"
                  maxlength="4000"
                ></textarea>
                <div class="edit-buttons">
                  <button type="button" (click)="cancelEdit()">Cancel</button>
                  <button type="submit" class="primary" [disabled]="editDraft.trim().length === 0">Save</button>
                </div>
              </form>
            } @else {
              <p class="body">
                @for (segment of segments(message); track $index) {
                  <span [class.mention]="!!segment.mention" [class.me]="segment.mention === me()">{{ segment.text }}</span>
                }
              </p>
            }
          </article>
        } @empty {
          <p class="muted empty">No messages yet. Say hello.</p>
        }
        @if (receiptText()) {
          <p class="seen">{{ receiptText() }}</p>
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
          [placeholder]="'Message ' + (room.kind === 'Direct' ? room.name : '#' + room.name) + ' (use @name to mention)'"
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
  protected editDraft = '';
  protected readonly editingId = signal<number | null>(null);
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

  /** "Seen" when someone has read my latest message, otherwise "Delivered" once a device of theirs has it. */
  protected readonly receiptText = computed(() => {
    const direct = this.store.activeRoom()?.kind === 'Direct';
    const seen = this.store.activeSeenBy();
    if (seen.length > 0) {
      return direct ? 'Seen' : `Seen by ${seen.join(', ')}`;
    }
    const delivered = this.store.activeDeliveredTo();
    if (delivered.length > 0) {
      return direct ? 'Delivered' : `Delivered to ${delivered.join(', ')}`;
    }
    return '';
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

  protected segments(message: Message) {
    return splitMentions(message.body);
  }

  protected mentionsMe(message: Message): boolean {
    return message.sender !== this.me() && !message.isDeleted && mentions(message.body, this.me());
  }

  protected canEdit(message: Message): boolean {
    return Date.now() - new Date(message.sentAt).getTime() <= EDIT_WINDOW_MS;
  }

  protected startEdit(message: Message): void {
    this.editDraft = message.body;
    this.editingId.set(message.id);
    this.sendError.set(null);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
    this.editDraft = '';
  }

  protected onEditEnter(event: Event, message: Message): void {
    if (!(event as KeyboardEvent).shiftKey) {
      event.preventDefault();
      void this.saveEdit(message);
    }
  }

  protected async saveEdit(message: Message): Promise<void> {
    const body = this.editDraft.trim();
    if (!body) {
      return;
    }
    try {
      if (body !== message.body) {
        await this.store.editMessage(message, body);
      }
      this.cancelEdit();
    } catch (error) {
      this.sendError.set(error instanceof Error ? error.message : 'The message could not be edited.');
    }
  }

  protected async remove(message: Message): Promise<void> {
    if (!confirm('Delete this message for everyone?')) {
      return;
    }
    try {
      await this.store.deleteMessage(message);
    } catch (error) {
      this.sendError.set(error instanceof Error ? error.message : 'The message could not be deleted.');
    }
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
