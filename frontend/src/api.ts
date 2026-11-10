import type {
  ClassOffering,
  CreatedClass,
  CreatedHold,
  HoldDetails,
  StudentHold,
  Confirmation,
} from "./types";
export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}
const messages: Record<string, string> = {
  CAPACITY_EXHAUSTED:
    "This class has just filled up. Refresh the list to explore other available classes.",
  DUPLICATE_APPLICATION:
    "You already have a seat held or confirmed in this class. Check My applications.",
  HOLD_EXPIRED:
    "This reservation has expired. You can apply again if seats are available.",
  HOLD_CANCELLED:
    "This reservation was cancelled. Apply again to reserve a new seat.",
  UNAUTHORIZED:
    "This reservation belongs to a different student. Check your student identity.",
  CANNOT_CANCEL_CONFIRMED: "A confirmed admission cannot be cancelled.",
  DUPLICATE_CLASS:
    "A class with this exact name already exists. Include the shift in the offering name.",
  INVALID_STUDENT_ID:
    "Enter exactly 17 digits from your birth registration number.",
  HOLD_NOT_FOUND:
    "This reservation could not be found. Refresh your applications.",
  CLASS_NOT_FOUND: "This class could not be found. Refresh the class list.",
};
const object = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);
const text = (v: unknown): v is string => typeof v === "string";
const id = (v: unknown): v is string =>
  text(v) &&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v);
const number = (v: unknown): v is number =>
  Number.isInteger(v) && Number(v) >= 0;
const status = (v: unknown) =>
  ["HELD", "CONFIRMED", "EXPIRED", "CANCELLED"].includes(String(v));
const date = (v: unknown) => text(v) && Number.isFinite(Date.parse(v));
const createdClass = (v: unknown): v is CreatedClass =>
  object(v) && id(v.id) && text(v.name) && number(v.totalSeats);
const offering = (v: unknown): v is ClassOffering =>
  object(v) &&
  number(v.heldSeats) &&
  number(v.confirmedSeats) &&
  number(v.availableSeats) &&
  createdClass(v);
const hold = (v: unknown): v is HoldDetails =>
  object(v) &&
  id(v.holdId) &&
  id(v.classId) &&
  status(v.status) &&
  date(v.expiresAt);
const createdHold = (v: unknown): v is CreatedHold =>
  hold(v) && object(v) && text(v.studentId);
const studentHold = (v: unknown): v is StudentHold =>
  hold(v) && object(v) && text(v.className);
const confirmation = (v: unknown): v is Confirmation =>
  object(v) && id(v.holdId) && status(v.status);
const list =
  <T>(guard: (v: unknown) => v is T) =>
  (v: unknown): v is T[] =>
    Array.isArray(v) && v.every(guard);

export class AdmissionApi {
  private base: string;
  constructor(
    base: string,
    private fetcher: typeof fetch = (...args) => fetch(...args),
  ) {
    this.base = base.replace(/\/$/, "");
  }
  private async request<T>(
    path: string,
    method: string,
    guard: (value: unknown) => value is T,
    studentId?: string,
    body?: unknown,
    signal?: AbortSignal,
  ): Promise<T> {
    const headers = new Headers();
    if (studentId) headers.set("X-Student-Id", studentId);
    if (body !== undefined) headers.set("Content-Type", "application/json");
    let response: Response;
    try {
      response = await this.fetcher(`${this.base}${path}`, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        signal,
      });
    } catch (error) {
      if (error instanceof Error && error.name === "AbortError") throw error;
      throw new ApiError(
        0,
        "NETWORK",
        "Could not connect to the admission service. Check your connection and try again.",
      );
    }
    if (!response.ok) {
      let data: unknown;
      try {
        data = await response.json();
      } catch {
        /* A gateway may return HTML or an empty body. */
      }
      const code =
        object(data) && text(data.code) ? data.code : "REQUEST_FAILED";
      throw new ApiError(
        response.status,
        code,
        messages[code] ??
          (response.status >= 500
            ? "The admission service is unavailable. Please try again shortly."
            : "The request could not be completed. Check your details and refresh."),
      );
    }
    if (response.status === 204) {
      if (method !== "DELETE")
        throw new ApiError(
          204,
          "INVALID_RESPONSE",
          "The service returned an empty response where data was required. Please refresh.",
        );
      return undefined as T;
    }
    let data: unknown;
    try {
      data = await response.json();
    } catch {
      throw new ApiError(
        response.status,
        "INVALID_RESPONSE",
        "The service returned an unreadable response. Please refresh.",
      );
    }
    if (!guard(data))
      throw new ApiError(
        response.status,
        "INVALID_RESPONSE",
        "The service returned an unexpected response. Please refresh.",
      );
    return data;
  }
  classes = (signal?: AbortSignal) =>
    this.request(
      "/classes",
      "GET",
      list(offering),
      undefined,
      undefined,
      signal,
    );
  classDetails = (classId: string, signal?: AbortSignal) =>
    this.request(
      `/classes/${encodeURIComponent(classId)}`,
      "GET",
      offering,
      undefined,
      undefined,
      signal,
    );
  createClass = (name: string, totalSeats: number, signal?: AbortSignal) =>
    this.request(
      "/classes",
      "POST",
      createdClass,
      undefined,
      { name, totalSeats },
      signal,
    );
  reserve = (classId: string, studentId: string, signal?: AbortSignal) =>
    this.request(
      `/classes/${encodeURIComponent(classId)}/holds`,
      "POST",
      createdHold,
      studentId,
      undefined,
      signal,
    );
  myHolds = (studentId: string, signal?: AbortSignal) =>
    this.request(
      "/me/holds",
      "GET",
      list(studentHold),
      studentId,
      undefined,
      signal,
    );
  hold = (holdId: string, signal?: AbortSignal) =>
    this.request(
      `/holds/${encodeURIComponent(holdId)}`,
      "GET",
      hold,
      undefined,
      undefined,
      signal,
    );
  confirm = (holdId: string, studentId: string, signal?: AbortSignal) =>
    this.request(
      `/holds/${encodeURIComponent(holdId)}/confirm`,
      "POST",
      confirmation,
      studentId,
      undefined,
      signal,
    );
  cancel = (holdId: string, studentId: string, signal?: AbortSignal) =>
    this.request<void>(
      `/holds/${encodeURIComponent(holdId)}`,
      "DELETE",
      (_v): _v is void => false,
      studentId,
      undefined,
      signal,
    );
}
