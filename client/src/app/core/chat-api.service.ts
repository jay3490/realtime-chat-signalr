import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  Channel,
  DeliveryMarker,
  Message,
  MessagePage,
  ReadMarker,
  RoomSummary,
  SyncPage,
  UnreadCount,
} from './models';

/** REST calls for loading state. Live updates arrive through the hub instead. */
@Injectable({ providedIn: 'root' })
export class ChatApiService {
  private readonly http = inject(HttpClient);

  rooms(): Promise<RoomSummary[]> {
    return firstValueFrom(this.http.get<RoomSummary[]>('/api/rooms'));
  }

  unreadCounts(): Promise<UnreadCount[]> {
    return firstValueFrom(this.http.get<UnreadCount[]>('/api/rooms/unread'));
  }

  channels(): Promise<Channel[]> {
    return firstValueFrom(this.http.get<Channel[]>('/api/channels'));
  }

  users(): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>('/api/users'));
  }

  onlineUsers(): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>('/api/presence'));
  }

  createChannel(name: string): Promise<RoomSummary> {
    return firstValueFrom(this.http.post<RoomSummary>('/api/channels', { name }));
  }

  joinChannel(roomId: string): Promise<RoomSummary> {
    return firstValueFrom(this.http.post<RoomSummary>(`/api/channels/${roomId}/join`, {}));
  }

  openDirect(userName: string): Promise<RoomSummary> {
    return firstValueFrom(this.http.post<RoomSummary>('/api/direct', { userName }));
  }

  /** Newest messages, or the page before `before`. */
  history(roomId: string, options: { before?: number; after?: number; limit?: number } = {}): Promise<MessagePage> {
    let params = new HttpParams().set('limit', options.limit ?? 50);
    if (options.before !== undefined) {
      params = params.set('before', options.before);
    }
    if (options.after !== undefined) {
      params = params.set('after', options.after);
    }
    return firstValueFrom(this.http.get<MessagePage>(`/api/rooms/${roomId}/messages`, { params }));
  }

  readMarkers(roomId: string): Promise<ReadMarker[]> {
    return firstValueFrom(this.http.get<ReadMarker[]>(`/api/rooms/${roomId}/read-markers`));
  }

  deliveries(roomId: string): Promise<DeliveryMarker[]> {
    return firstValueFrom(this.http.get<DeliveryMarker[]>(`/api/rooms/${roomId}/deliveries`));
  }

  editMessage(roomId: string, messageId: number, body: string): Promise<Message> {
    return firstValueFrom(this.http.put<Message>(`/api/rooms/${roomId}/messages/${messageId}`, { body }));
  }

  deleteMessage(roomId: string, messageId: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/rooms/${roomId}/messages/${messageId}`));
  }

  /** Changes since `cursor`; without one, the current head of the feed and no changes. */
  sync(cursor?: string): Promise<SyncPage> {
    const params = cursor ? new HttpParams().set('cursor', cursor) : undefined;
    return firstValueFrom(this.http.get<SyncPage>('/api/sync', { params }));
  }
}
