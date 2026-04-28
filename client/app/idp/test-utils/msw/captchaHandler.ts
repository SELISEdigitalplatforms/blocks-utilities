import { http, HttpResponse, type JsonBodyType } from "msw";
import { mockCaptchaConfigsResponse } from "../__mocks__/captcha.data.mock";
import { mockSuccessResponseWithItemId } from "@/test-utils/__mocks__";
import { CAPTCHA_ENDPOINTS } from "../../captcha/constants/endpoint.constant";

// ─── Endpoint Patterns ────────────────────────────────────────────────────────

const GET_CAPTCHA_CONFIGS_PATTERN = new RegExp(`${CAPTCHA_ENDPOINTS.GETS}\\?`);
const SAVE_CAPTCHA_PATTERN = new RegExp(CAPTCHA_ENDPOINTS.SAVE);
const UPDATE_CAPTCHA_STATUS_PATTERN = new RegExp(CAPTCHA_ENDPOINTS.UPDATE_STATUS);

// ─── Default Handlers (happy-path) ───────────────────────────────────────────

export const captchaHandlers = [
  http.get(GET_CAPTCHA_CONFIGS_PATTERN, () => HttpResponse.json(mockCaptchaConfigsResponse)),
  http.post(SAVE_CAPTCHA_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.post(UPDATE_CAPTCHA_STATUS_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
];

// ─── Per-Test Override Factories ──────────────────────────────────────────────

export const getCaptchaConfigsHandler = (response: JsonBodyType = mockCaptchaConfigsResponse) =>
  http.get(GET_CAPTCHA_CONFIGS_PATTERN, () => HttpResponse.json(response));

export const getCaptchaConfigsErrorHandler = (status = 500) =>
  http.get(GET_CAPTCHA_CONFIGS_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const saveCaptchaHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(SAVE_CAPTCHA_PATTERN, () => HttpResponse.json(response));

export const saveCaptchaErrorHandler = (status = 500) =>
  http.post(SAVE_CAPTCHA_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const updateCaptchaStatusHandler = (
  response: JsonBodyType = mockSuccessResponseWithItemId,
) => http.post(UPDATE_CAPTCHA_STATUS_PATTERN, () => HttpResponse.json(response));

export const updateCaptchaStatusErrorHandler = (status = 500) =>
  http.post(UPDATE_CAPTCHA_STATUS_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );
