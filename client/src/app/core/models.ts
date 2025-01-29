// Shapes returned by the API and pushed by the hub. They mirror the C# DTOs.

export type RoomKind = 'Channel' | 'Direct';

export interface IssuedToken {
  accessToken: string;
  userName: string;
  expiresAt: string;
}

export interface RoomSummary {
  id: string;
  kind: RoomKind;
  /** Channel name, or the other participant for a direct conversation. */
  name: string;
  unreadCount: number;
  lastMessageId: number;
  lastReadMessageId: number;
}

export interface Channel {
  id: string;
  name: string;
  memberCount: number;
  isMember: boolean;
}

export interface Message {
  id: number;
  roomId: string;
  sender: string;
  body: string;
  sentAt: string;
}

export interface MessagePage {
  messages: Message[];
  hasMore: boolean;
}

export interface ReadMarker {
  userName: string;
  lastReadMessageId: number;
  readAt: string | null;
}

export interface UnreadCount {
  roomId: string;
  unreadCount: number;
  lastReadMessageId: number;
}

export interface ReadReceipt {
  roomId: string;
  userName: string;
  lastReadMessageId: number;
  readAt: string;
}

export interface Typing {
  roomId: string;
  userName: string;
  expiresInMs: number;
}

export interface TypingStopped {
  roomId: string;
  userName: string;
}

export interface Presence {
  userName: string;
  isOnline: boolean;
}

export interface MemberJoined {
  roomId: string;
  userName: string;
}

export interface ConnectionInfo {
  userName: string;
  connectionId: string;
  instance: string;
}

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';
