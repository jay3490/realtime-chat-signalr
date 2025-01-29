import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from './auth.service';
import { ChatApiService } from './chat-api.service';
import { ChatHubService } from './chat-hub.service';
import { Message, ReadMarker, RoomSummary } from './models';

interface RoomMessages {
  messages: Message[];
  hasMoreBefore: boolean;
}

/**
 * Client-side state for the chat screen, kept in signals. Initial state comes from the
 * REST API; hub events then keep it current.
 */
@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly api = inject(ChatApiService);
  private readonly hub = inject(ChatHubService);
  private readonly auth = inject(AuthService);

  private readonly roomsById = signal<ReadonlyMap<string, RoomSummary>>(new Map());
  private readonly messagesByRoom = signal<ReadonlyMap<string, RoomMessages>>(new Map());
  private readonly markersByRoom = signal<ReadonlyMap<string, ReadMarker[]>>(new Map());
  /** roomId -> (user -> timer that clears the indicator). */
  private readonly typingTimers = new Map<string, Map<string, ReturnType<typeof setTimeout>>>();
  private readonly typingByRoom = signal<ReadonlyMap<string, readonly string[]>>(new Map());

  readonly activeRoomId = signal<string | null>(null);
  readonly onlineUsers = signal<ReadonlySet<string>>(new Set());
  readonly loadingOlder = signal(false);

  /** Rooms with the most recent activity first. */
  readonly rooms = computed(() =>
    [...this.roomsById().values()].sort(
      (a, b) => b.lastMessageId - a.lastMessageId || a.name.localeCompare(b.name),
    ),
  );

  readonly activeRoom = computed(() => {
    const id = this.activeRoomId();
    return id ? (this.roomsById().get(id) ?? null) : null;
  });

  readonly activeMessages = computed(() => {
    const id = this.activeRoomId();
    return id ? (this.messagesByRoom().get(id)?.messages ?? []) : [];
  });

  readonly activeHasMore = computed(() => {
    const id = this.activeRoomId();
    return id ? (this.messagesByRoom().get(id)?.hasMoreBefore ?? false) : false;
  });

  readonly activeTyping = computed(() => {
    const id = this.activeRoomId();
    return id ? (this.typingByRoom().get(id) ?? []) : [];
  });

  /** Other members whose read marker has reached the newest message in the open room. */
  readonly activeSeenBy = computed(() => {
    const id = this.activeRoomId();
    const messages = this.activeMessages();
    const last = messages[messages.length - 1];
    if (!id || !last) {
      return [];
    }
    return (this.markersByRoom().get(id) ?? [])
      .filter((m) => m.userName !== this.me() && m.userName !== last.sender)
      .filter((m) => m.lastReadMessageId >= last.id)
      .map((m) => m.userName);
  });

  readonly totalUnread = computed(() => this.rooms().reduce((sum, r) => sum + r.unreadCount, 0));

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.hub.messageReceived$.pipe(takeUntilDestroyed(destroyRef)).subscribe((m) => this.onMessage(m));
    this.hub.readReceipt$.pipe(takeUntilDestroyed(destroyRef)).subscribe((r) => {
      this.upsertMarker(r.roomId, { userName: r.userName, lastReadMessageId: r.lastReadMessageId, readAt: r.readAt });
      if (r.userName === this.me()) {
        // Read on another device: clear the badge here too.
        this.patchRoom(r.roomId, (room) => ({
          ...room,
          lastReadMessageId: Math.max(room.lastReadMessageId, r.lastReadMessageId),
          unreadCount: r.lastReadMessageId >= room.lastMessageId ? 0 : room.unreadCount,
        }));
      }
    });
    this.hub.userTyping$.pipe(takeUntilDestroyed(destroyRef)).subscribe((t) =>
      this.setTyping(t.roomId, t.userName, t.expiresInMs),
    );
    this.hub.userStoppedTyping$.pipe(takeUntilDestroyed(destroyRef)).subscribe((t) =>
      this.clearTyping(t.roomId, t.userName),
    );
    this.hub.presenceChanged$.pipe(takeUntilDestroyed(destroyRef)).subscribe((p) => {
      const next = new Set(this.onlineUsers());
      if (p.isOnline) {
        next.add(p.userName);
      } else {
        next.delete(p.userName);
      }
      this.onlineUsers.set(next);
    });
    this.hub.roomAdded$.pipe(takeUntilDestroyed(destroyRef)).subscribe((room) => this.upsertRoom(room));
    this.hub.memberJoined$.pipe(takeUntilDestroyed(destroyRef)).subscribe((m) => {
      if (this.markersByRoom().has(m.roomId)) {
        this.upsertMarker(m.roomId, { userName: m.userName, lastReadMessageId: 0, readAt: null });
      }
    });
  }

  me(): string | null {
    return this.auth.userName();
  }

  isOnline(userName: string): boolean {
    return this.onlineUsers().has(userName);
  }

  async load(): Promise<void> {
    const [rooms, online] = await Promise.all([this.api.rooms(), this.api.onlineUsers()]);
    this.roomsById.set(new Map(rooms.map((r) => [r.id, r])));
    this.onlineUsers.set(new Set(online));
  }

  async open(roomId: string): Promise<void> {
    this.activeRoomId.set(roomId);
    if (!this.messagesByRoom().has(roomId)) {
      const [page, markers] = await Promise.all([this.api.history(roomId), this.api.readMarkers(roomId)]);
      this.setMessages(roomId, { messages: page.messages, hasMoreBefore: page.hasMore });
      this.markersByRoom.update((all) => new Map(all).set(roomId, markers));
    }
    await this.markActiveRead();
  }

  close(): void {
    this.activeRoomId.set(null);
  }

  async loadOlder(): Promise<void> {
    const roomId = this.activeRoomId();
    const current = roomId ? this.messagesByRoom().get(roomId) : undefined;
    if (!roomId || !current?.hasMoreBefore || this.loadingOlder()) {
      return;
    }
    this.loadingOlder.set(true);
    try {
      const page = await this.api.history(roomId, { before: current.messages[0]?.id });
      this.setMessages(roomId, {
        messages: [...page.messages, ...current.messages],
        hasMoreBefore: page.hasMore,
      });
    } finally {
      this.loadingOlder.set(false);
    }
  }

  async send(body: string): Promise<void> {
    const roomId = this.activeRoomId();
    if (!roomId) {
      return;
    }
    // The room's broadcast also echoes the message back; whichever arrives first adds it.
    const sent = await this.hub.sendMessage(roomId, body);
    this.appendMessage(sent);
  }

  /** Marks everything in the open room as read, if the window is being looked at. */
  async markActiveRead(): Promise<void> {
    const room = this.activeRoom();
    if (!room || document.visibilityState !== 'visible') {
      return;
    }
    const newest = this.activeMessages().at(-1)?.id ?? room.lastMessageId;
    if (newest > room.lastReadMessageId) {
      this.patchRoom(room.id, (r) => ({ ...r, lastReadMessageId: newest, unreadCount: 0 }));
      await this.hub.markRead(room.id, newest);
    }
  }

  async joinChannel(roomId: string): Promise<RoomSummary> {
    const room = await this.api.joinChannel(roomId);
    this.upsertRoom(room);
    return room;
  }

  async createChannel(name: string): Promise<RoomSummary> {
    const room = await this.api.createChannel(name);
    this.upsertRoom(room);
    return room;
  }

  async openDirect(userName: string): Promise<RoomSummary> {
    const room = await this.api.openDirect(userName);
    this.upsertRoom(room);
    return room;
  }

  reset(): void {
    this.roomsById.set(new Map());
    this.messagesByRoom.set(new Map());
    this.markersByRoom.set(new Map());
    this.typingTimers.forEach((timers) => timers.forEach((t) => clearTimeout(t)));
    this.typingTimers.clear();
    this.typingByRoom.set(new Map());
    this.activeRoomId.set(null);
    this.onlineUsers.set(new Set());
  }

  private onMessage(message: Message): void {
    this.clearTyping(message.roomId, message.sender);
    const isNew = this.appendMessage(message);
    if (!isNew) {
      return;
    }

    const fromSomeoneElse = message.sender !== this.me();
    const isOpen = this.activeRoomId() === message.roomId && document.visibilityState === 'visible';
    this.patchRoom(message.roomId, (room) => ({
      ...room,
      lastMessageId: Math.max(room.lastMessageId, message.id),
      unreadCount: fromSomeoneElse && !isOpen ? room.unreadCount + 1 : room.unreadCount,
    }));

    if (fromSomeoneElse && isOpen) {
      void this.markActiveRead();
    }
  }

  /** Adds a message to a loaded room, ignoring duplicates. Returns false if it was already there. */
  private appendMessage(message: Message): boolean {
    const loaded = this.messagesByRoom().get(message.roomId);
    if (loaded?.messages.some((m) => m.id === message.id)) {
      return false;
    }
    if (loaded) {
      const messages = [...loaded.messages, message].sort((a, b) => a.id - b.id);
      this.setMessages(message.roomId, { ...loaded, messages });
    }
    if (message.sender === this.me()) {
      this.patchRoom(message.roomId, (room) => ({
        ...room,
        lastMessageId: Math.max(room.lastMessageId, message.id),
      }));
    }
    return true;
  }

  private setMessages(roomId: string, value: RoomMessages): void {
    this.messagesByRoom.update((all) => new Map(all).set(roomId, value));
  }

  private upsertRoom(room: RoomSummary): void {
    this.roomsById.update((all) => new Map(all).set(room.id, { ...all.get(room.id), ...room }));
  }

  private patchRoom(roomId: string, change: (room: RoomSummary) => RoomSummary): void {
    const room = this.roomsById().get(roomId);
    if (room) {
      this.roomsById.update((all) => new Map(all).set(roomId, change(room)));
    }
  }

  private upsertMarker(roomId: string, marker: ReadMarker): void {
    const markers = this.markersByRoom().get(roomId);
    if (!markers) {
      return;
    }
    const others = markers.filter((m) => m.userName !== marker.userName);
    const existing = markers.find((m) => m.userName === marker.userName);
    const merged =
      existing && existing.lastReadMessageId > marker.lastReadMessageId ? existing : marker;
    this.markersByRoom.update((all) => new Map(all).set(roomId, [...others, merged]));
  }

  private setTyping(roomId: string, userName: string, expiresInMs: number): void {
    const timers = this.typingTimers.get(roomId) ?? new Map<string, ReturnType<typeof setTimeout>>();
    clearTimeout(timers.get(userName));
    // The server says how long to show the indicator; if no refresh arrives it goes away.
    timers.set(userName, setTimeout(() => this.clearTyping(roomId, userName), expiresInMs));
    this.typingTimers.set(roomId, timers);
    this.publishTyping(roomId);
  }

  private clearTyping(roomId: string, userName: string): void {
    const timers = this.typingTimers.get(roomId);
    if (!timers?.has(userName)) {
      return;
    }
    clearTimeout(timers.get(userName));
    timers.delete(userName);
    this.publishTyping(roomId);
  }

  private publishTyping(roomId: string): void {
    const users = [...(this.typingTimers.get(roomId)?.keys() ?? [])].sort();
    this.typingByRoom.update((all) => new Map(all).set(roomId, users));
  }
}
