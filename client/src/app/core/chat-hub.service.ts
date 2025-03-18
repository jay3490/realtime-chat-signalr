import { Injectable, NgZone, inject, signal } from '@angular/core';
import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  IRetryPolicy,
  LogLevel,
  RetryContext,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthService } from './auth.service';
import { deviceId } from './device';
import {
  ConnectionInfo,
  ConnectionState,
  DeliveryReceipt,
  MemberJoined,
  Message,
  MessageDeleted,
  Presence,
  ReadReceipt,
  RoomSummary,
  Typing,
  TypingStopped,
} from './models';

/** Retries quickly at first, then every 30 seconds, and gives up after about five minutes. */
class BackoffRetryPolicy implements IRetryPolicy {
  private static readonly delays = [0, 2_000, 5_000, 10_000, 30_000];

  nextRetryDelayInMilliseconds(context: RetryContext): number | null {
    if (context.elapsedMilliseconds > 5 * 60_000) {
      return null;
    }
    return BackoffRetryPolicy.delays[context.previousRetryCount] ?? 30_000;
  }
}

/**
 * Owns the SignalR connection. Server pushes are re-emitted as observables, and the
 * connection state is exposed as a signal so the UI can show reconnect progress.
 */
@Injectable({ providedIn: 'root' })
export class ChatHubService {
  private readonly auth = inject(AuthService);
  private readonly zone = inject(NgZone);
  private connection: HubConnection | null = null;

  readonly state = signal<ConnectionState>('disconnected');
  /** The API instance serving this connection; useful when several run behind the proxy. */
  readonly serverInstance = signal<string | null>(null);

  readonly messageReceived$ = new Subject<Message>();
  readonly messageEdited$ = new Subject<Message>();
  readonly messageDeleted$ = new Subject<MessageDeleted>();
  readonly readReceipt$ = new Subject<ReadReceipt>();
  readonly deliveryReceipt$ = new Subject<DeliveryReceipt>();
  readonly userTyping$ = new Subject<Typing>();
  readonly userStoppedTyping$ = new Subject<TypingStopped>();
  readonly presenceChanged$ = new Subject<Presence>();
  readonly roomAdded$ = new Subject<RoomSummary>();
  readonly memberJoined$ = new Subject<MemberJoined>();
  /** Fires after an automatic or manual reconnect, so state missed while offline can be reloaded. */
  readonly reconnected$ = new Subject<void>();

  async start(): Promise<void> {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
      return;
    }

    this.connection ??= this.build();
    this.state.set('connecting');
    try {
      await this.connection.start();
      await this.onConnected();
    } catch (error) {
      this.state.set('disconnected');
      throw error;
    }
  }

  /** Manual retry after the automatic policy has given up. */
  async restart(): Promise<void> {
    await this.start();
    this.reconnected$.next();
  }

  async stop(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    await connection?.stop();
    this.state.set('disconnected');
    this.serverInstance.set(null);
  }

  sendMessage(roomId: string, body: string): Promise<Message> {
    return this.hub().invoke<Message>('SendMessage', roomId, body);
  }

  editMessage(roomId: string, messageId: number, body: string): Promise<Message> {
    return this.hub().invoke<Message>('EditMessage', roomId, messageId, body);
  }

  deleteMessage(roomId: string, messageId: number): Promise<boolean> {
    return this.hub().invoke<boolean>('DeleteMessage', roomId, messageId);
  }

  markRead(roomId: string, messageId: number): Promise<boolean> {
    return this.hub().invoke<boolean>('MarkRead', roomId, messageId);
  }

  /** Tells the room this device has received everything up to `messageId`. */
  markDelivered(roomId: string, messageId: number): Promise<boolean> {
    return this.hub().invoke<boolean>('MarkDelivered', roomId, messageId);
  }

  startTyping(roomId: string): Promise<void> {
    return this.hub().invoke('StartTyping', roomId);
  }

  stopTyping(roomId: string): Promise<void> {
    return this.hub().invoke('StopTyping', roomId);
  }

  onlineUsers(): Promise<string[]> {
    return this.hub().invoke<string[]>('GetOnlineUsers');
  }

  private hub(): HubConnection {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      throw new Error('Not connected to the chat server.');
    }
    return this.connection;
  }

  private build(): HubConnection {
    const connection = new HubConnectionBuilder()
      // The device id lets the server keep delivery receipts per device.
      .withUrl(`/hubs/chat?deviceId=${encodeURIComponent(deviceId())}`, {
        accessTokenFactory: () => this.auth.accessToken ?? '',
        // WebSockets only, without the negotiate round trip: the connection never has to
        // return to the instance that negotiated it, so no sticky sessions are needed.
        transport: HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect(new BackoffRetryPolicy())
      .configureLogging(LogLevel.Warning)
      .build();

    const forward = <T>(method: string, subject: Subject<T>) =>
      connection.on(method, (payload: T) => this.zone.run(() => subject.next(payload)));

    forward('MessageReceived', this.messageReceived$);
    forward('MessageEdited', this.messageEdited$);
    forward('MessageDeleted', this.messageDeleted$);
    forward('ReadReceipt', this.readReceipt$);
    forward('DeliveryReceipt', this.deliveryReceipt$);
    forward('UserTyping', this.userTyping$);
    forward('UserStoppedTyping', this.userStoppedTyping$);
    forward('PresenceChanged', this.presenceChanged$);
    forward('RoomAdded', this.roomAdded$);
    forward('MemberJoined', this.memberJoined$);

    connection.onreconnecting(() => this.zone.run(() => this.state.set('reconnecting')));
    connection.onreconnected(() =>
      this.zone.run(async () => {
        await this.onConnected();
        this.reconnected$.next();
      }),
    );
    connection.onclose(() =>
      this.zone.run(() => {
        this.state.set('disconnected');
        this.serverInstance.set(null);
      }),
    );

    return connection;
  }

  private async onConnected(): Promise<void> {
    // The server finishes joining room groups before it answers any invocation, so once
    // this returns, broadcasts for every room the user belongs to will arrive.
    const info = await this.hub().invoke<ConnectionInfo>('GetConnectionInfo');
    this.serverInstance.set(info.instance);
    this.state.set('connected');
  }
}
