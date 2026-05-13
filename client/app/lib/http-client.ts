import { useProjectStore } from "@/store/useProjectStore";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { getQueryClient } from "@/providers/query-provider";
import { useAuthStore } from "@/store/useAuthStore";
import {
  AUTH_ENDPOINTS,
  AUTH_OIDC_ENDPOINTS,
} from "@/idp/authentication/constants/endpoint.constant";
import { API_BASES } from "@/constants/endpoint.constant";

class HttpError extends Error {
  status: number;
  errors: Record<string, string | string[]>;

  constructor(
    status: number,
    error: { errors: Record<string, string | string[]> },
  ) {
    super(error.toString());
    this.status = status;
    this.errors = error.errors;
  }
}

type HeadersInit = [string, string][] | Record<string, string> | Headers;
type RequestBody =
  | string
  | Record<string, unknown>
  | Array<unknown>
  | FormData
  | URLSearchParams
  | null
  | unknown;

interface Options {
  skipBlocksKey?: boolean;
  withCredentials?: boolean;
  absoluteUrl?: boolean;
  skipTokenRotation?: boolean;
}

interface RequestOptions extends Options {
  method: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
  headers?: HeadersInit;
  body?: RequestBody;
}

interface RequestQueue<T> {
  url: string;
  requestOption: RequestOptions;
  resolve: (value: T | PromiseLike<T>) => void;
  reject: (reason?: unknown) => void;
}
let isRefreshing = false;
let requestQueue: RequestQueue<unknown>[] = [];

class HttpClient {
  constructor(
    private baseURL: string,
    private BLOCKS_KEY: string,
  ) {}

  private normalizeHeaders(
    headers?: HeadersInit,
    skipBlocksKey?: boolean,
  ): Headers {
    const normalizedHeaders = new Headers({
      Accept: "application/json",
      "Content-Type": "application/json",
      ...(!skipBlocksKey && { "X-Blocks-Key": this.BLOCKS_KEY }),
    });

    if (headers) {
      if (headers instanceof Headers) {
        headers.forEach((value, key) => normalizedHeaders.set(key, value));
      } else if (Array.isArray(headers)) {
        headers.forEach(([key, value]) => normalizedHeaders.set(key, value));
      } else {
        Object.entries(headers).forEach(([key, value]) =>
          normalizedHeaders.set(key, value),
        );
      }
    }

    return normalizedHeaders;
  }

  private async refreshAccessToken() {
    if (isRefreshing) return;

    try {
      isRefreshing = true;

      const formData = new URLSearchParams();
      formData.append("grant_type", "refresh_token");
      formData.append("refresh_token", '""');
      formData.append(
        "client_id",
        getRuntimeEnv("BLOCKS_OIDC_CLIENT_ID") || "",
      );

      const url = `${AUTH_OIDC_ENDPOINTS.OIDC_TOKEN}?tenant_id=${this.BLOCKS_KEY}`;
      const response = await fetch(url, {
        method: "POST",
        body: formData,
        headers: {
          "Content-Type": "application/x-www-form-urlencoded",
          "X-Blocks-Key": this.BLOCKS_KEY,
        },
        credentials: "include",
      });

      if (!response.ok) throw new Error("Failed to refresh token");

      while (requestQueue.length > 0) {
        const { url, requestOption, resolve, reject } = requestQueue.shift()!;
        this.request(url, requestOption).then(resolve).catch(reject);
      }
    } catch (_error) {
      const queryClient = getQueryClient();
      useAuthStore.getState().reset();
      useProjectStore.getState().reset();
      queryClient.cancelQueries();
      queryClient.clear();
      window.location.href = `/login`;
    } finally {
      isRefreshing = false;
      requestQueue = [];
    }
  }

  private async request<T = unknown>(
    url: string,
    requestOption: RequestOptions,
  ): Promise<T> {
    const {
      method,
      body,
      headers,
      absoluteUrl = false,
      skipBlocksKey = false,
      withCredentials = true,
      skipTokenRotation = false,
    } = requestOption;
    const fullUrl = absoluteUrl ? url : `${this.baseURL}${url}`;
    const normalizedHeaders = this.normalizeHeaders(headers, skipBlocksKey);
    const config: RequestInit = {
      method,
      headers: normalizedHeaders,
      credentials: "include",
    };

    if (body) {
      if (
        body instanceof FormData ||
        body instanceof URLSearchParams ||
        body instanceof File ||
        body instanceof Blob
      ) {
        normalizedHeaders.delete("Content-Type");
        config.body = body;
      } else {
        config.body = JSON.stringify(body);
      }
    }

    try {
      const response = await fetch(fullUrl, config);

      if (response.status === 401 && !skipTokenRotation) {
        return new Promise<T>((resolve, reject) => {
          requestQueue.push({
            url,
            requestOption,
            resolve: resolve as (value: unknown | PromiseLike<unknown>) => void,
            reject,
          });
          if (!isRefreshing) this.refreshAccessToken();
        });
      }

      if (!response.ok) {
        const errorBody = await response.json().catch(() => ({}));
        throw new HttpError(response.status, {
          errors: errorBody?.errors || errorBody,
        });
      }

      const contentType = response.headers.get("content-type")?.toLowerCase();
      if (!contentType) return { success: true, status: response.status } as T;
      if (contentType.includes("text/html")) {
        throw new HttpError(response.status, {
          errors: { general: "Unexpected HTML response from server" },
        });
      }
      if (contentType.includes("text/"))
        return (await response.text()) as unknown as T;
      if (
        contentType.includes("image/") ||
        contentType.includes("application/octet-stream") ||
        contentType.includes("application/pdf")
      )
        return (await response.blob()) as unknown as T;

      return await response.json();
    } catch (error) {
      if (error instanceof HttpError) throw error;
      if (typeof error === "object" && error !== null) {
        throw new HttpError(500, {
          errors: error as Record<string, string | string[]>,
        });
      }

      throw new HttpError(500, {
        errors: { general: "Something went wrong" },
      });
    }
  }

  get<T = unknown>(
    url: string,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, { method: "GET", headers, ...options });
  }

  post<T = unknown>(
    url: string,
    body: RequestBody,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, {
      method: "POST",
      body,
      headers,
      ...options,
    });
  }

  put<T = unknown>(
    url: string,
    body: RequestBody,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, { method: "PUT", body, headers, ...options });
  }

  patch<T = unknown>(
    url: string,
    body: RequestBody,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, { method: "PATCH", body, headers, ...options });
  }

  delete<T = unknown>(
    url: string,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, { method: "DELETE", headers, ...options });
  }

  async stream(
    url: string,
    body: RequestBody,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<ReadableStream<Uint8Array>> {
    const {
      absoluteUrl = false,
      skipBlocksKey = false,
      withCredentials = true,
    } = options || {};

    const fullUrl = absoluteUrl ? url : `${this.baseURL}${url}`;
    const normalizedHeaders = this.normalizeHeaders(headers, skipBlocksKey);

    const response = await fetch(fullUrl, {
      method: "POST",
      headers: normalizedHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const errorBody = await response.json().catch(() => ({}));
      throw new HttpError(response.status, {
        errors: errorBody?.errors || errorBody,
      });
    }

    if (!response.body) {
      throw new Error("Response body is not readable");
    }

    return response.body;
  }
}

export const http = new HttpClient(
  getRuntimeEnv("BLOCKS_API_BASE_URL") || "",
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export { HttpClient, HttpError };
