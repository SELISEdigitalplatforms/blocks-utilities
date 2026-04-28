export type APIErrorResponse<T = unknown> = {
  data?: never;
  error: APIError<T>;
  status?: number;
};

export type APIError<T = unknown> = { errors: string[] } & T;

export interface APIResponse<T> {
  isSuccess?: boolean;
  data: T;
  error?: unknown;
  status?: number;
}

export interface IAPIResponse<T> {
  data: T;
  errors?: unknown;
  status?: number;
  totalCount: number;
}
