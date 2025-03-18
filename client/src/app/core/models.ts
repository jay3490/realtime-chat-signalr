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
  /** Empty once the message has been deleted. */
  body: string;
  sentAt: string;
  /** 1 as sent, incremented by every edit. */
  revision: number;
  editedAt: string | null;
  isDeleted: boolean;
}

export interface MessageDeleted {
  roomId: string;
  messageId: number;
  deletedAt: string;
}

export interface DeliveryReceipt {
  roomId: string;
  userName: string;
  deviceId: string;
  lastDeliveredMessageId: number;
  deliveredAt: string;
}

export type DeliveryMarker = Omit<DeliveryReceipt, 'roomId'>;

export type SyncChangeKind =
  | 'RoomCreated'
  | 'MemberJoined'
  | 'MessageSent'
  | 'MessageEdited'
  | 'MessageDeleted'
  | 'ReadReceipt';

/** One change from the server's feed; message changes carry the message as it is now. */
export interface SyncChange {
  roomId: string;
  kind: SyncChangeKind;
  message: Message | null;
  messageId: number | null;
  userName: string | null;
  occurredAt: string;
}

export interface SyncPage {
  changes: SyncChange[];
  cursor: string;
  hasMore: boolean;
  /** The cursor is no longer valid (the read model was rebuilt): reload everything. */
  reset: boolean;
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
  deviceId: string | null;
}

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';
