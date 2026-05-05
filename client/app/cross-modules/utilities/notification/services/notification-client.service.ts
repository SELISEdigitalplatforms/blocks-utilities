import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
} from "@microsoft/signalr";
import { getRuntimeEnv } from "@/lib/runtime-env";

export class NotificationClientService {
  public connection: HubConnection;

  constructor() {
    const apiBaseUrl = getRuntimeEnv("BLOCKS_API_BASE_URL");
    const xBlocksKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");

    this.connection = new HubConnectionBuilder()
      .withUrl(
        `${apiBaseUrl}/NotificationHub?x-blocks-key=${xBlocksKey}`,
        {
          transport: HttpTransportType.WebSockets,
        },
      )
      .withAutomaticReconnect()
      .build();
    this.connect();
  }

  async connect() {
    this.connection.start();
  }

  async disconnect() {
    if (this.connection.state !== "Disconnected") {
      await this.connection.stop();
    }
  }
}

export const notificationClientService = new NotificationClientService();
