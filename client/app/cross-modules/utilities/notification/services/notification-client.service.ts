import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
} from "@microsoft/signalr";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";

export class NotificationClientService {
  public connection: HubConnection;
  private connectionStarted = false;

  constructor() {
    const logicApiBaseUrl = deriveLogicBaseUrl();
    const xBlocksKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");

    this.connection = new HubConnectionBuilder()
      .withUrl(
        `${logicApiBaseUrl}/NotificationHub?x-blocks-key=${xBlocksKey}`,
        {
          transport: HttpTransportType.WebSockets,
        },
      )
      .withAutomaticReconnect()
      .build();
  }

  async connect() {
    // Only start the connection once
    if (this.connectionStarted) {
      return;
    }
    this.connectionStarted = true;
    await this.connection.start();
  }

  async disconnect() {
    if (this.connection.state !== "Disconnected") {
      await this.connection.stop();
    }
  }
}

export const notificationClientService = new NotificationClientService();
