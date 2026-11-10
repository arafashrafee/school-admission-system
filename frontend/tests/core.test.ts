import { test } from "node:test";
import assert from "node:assert/strict";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import {
  isStudentId,
  remainingSeconds,
  effectiveStatus,
  formatCountdown,
} from "../src/domain.js";
import { readIdentity, saveIdentity } from "../src/identity.js";
import { StatusBadge } from "../src/components/StatusBadge.js";
import { AdmissionApi, ApiError } from "../src/api.js";

const student = "00000000000000001";
const classId = "11111111-1111-1111-1111-111111111111";

test("identity accepts exactly 17 ASCII digits and preserves leading zeroes", () => {
  assert.equal(isStudentId(student), true);
  for (const value of [
    "",
    "1".repeat(16),
    "1".repeat(18),
    "１２３４５６７８９０１２３４５６７",
    " 00000000000000001",
    "00000000000000001\n",
    "1234567890123456a",
  ])
    assert.equal(isStudentId(value), false, value);
});

test("identity storage restores, switches, clears, and handles disabled storage", () => {
  const values = new Map<string, string>();
  const storage = {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => {
      values.set(key, value);
    },
    removeItem: (key: string) => {
      values.delete(key);
    },
  };
  assert.equal(readIdentity(storage), null);
  assert.equal(saveIdentity(student, storage), true);
  assert.equal(readIdentity(storage), student);
  saveIdentity("00000000000000002", storage);
  assert.equal(readIdentity(storage), "00000000000000002");
  saveIdentity(null, storage);
  assert.equal(readIdentity(storage), null);
  assert.equal(
    saveIdentity(student, {
      ...storage,
      setItem: () => {
        throw new Error("blocked");
      },
    }),
    false,
  );
  assert.equal(
    readIdentity({
      getItem: () => {
        throw new Error("blocked");
      },
    }),
    null,
  );
});

test("countdown uses the absolute server deadline without drift or timer reset", () => {
  const expiry = "2026-11-11T12:00:00Z";
  const end = Date.parse(expiry);
  assert.equal(remainingSeconds(expiry, end - 61001), 62);
  assert.equal(formatCountdown(62), "01:02");
  assert.equal(remainingSeconds(expiry, end - 1), 1);
  assert.equal(remainingSeconds(expiry, end), 0);
  assert.equal(remainingSeconds(expiry, end + 10000), 0);
  assert.equal(effectiveStatus("HELD", expiry, end), "EXPIRED");
  assert.equal(effectiveStatus("CONFIRMED", expiry, end), "CONFIRMED");
  assert.equal(effectiveStatus("CANCELLED", expiry, end), "CANCELLED");
});

for (const [status, label] of [
  ["HELD", "Seat held"],
  ["CONFIRMED", "Confirmed"],
  ["EXPIRED", "Expired"],
  ["CANCELLED", "Cancelled"],
] as const)
  test(`status badge renders an accessible ${status} label`, () => {
    const html = renderToStaticMarkup(createElement(StatusBadge, { status }));
    assert.ok(html.includes(label));
    assert.ok(html.includes(`status-${status.toLowerCase()}`));
  });

test("client uses assignment paths and identity headers, never identity in URL or body", async () => {
  const calls: { url: string; init?: RequestInit }[] = [];
  const mockFetch: typeof fetch = async (input, init) => {
    const url = String(input);
    calls.push({ url, init });
    if (init?.method === "DELETE") return new Response(null, { status: 204 });
    if (url.endsWith("/confirm"))
      return Response.json({ holdId: classId, status: "CONFIRMED" });
    if (init?.method === "POST")
      return Response.json(
        {
          holdId: classId,
          classId,
          studentId: student,
          status: "HELD",
          expiresAt: "2026-11-11T12:00:00Z",
        },
        { status: 201 },
      );
    return Response.json([]);
  };
  const api = new AdmissionApi("http://localhost:8080/", mockFetch);
  await api.reserve(classId, student);
  await api.confirm(classId, student);
  await api.cancel(classId, student);
  await api.myHolds(student);
  assert.deepEqual(
    calls.map((c) => [c.url, c.init?.method]),
    [
      [`http://localhost:8080/classes/${classId}/holds`, "POST"],
      [`http://localhost:8080/holds/${classId}/confirm`, "POST"],
      [`http://localhost:8080/holds/${classId}`, "DELETE"],
      ["http://localhost:8080/me/holds", "GET"],
    ],
  );
  for (const call of calls) {
    assert.equal(new Headers(call.init?.headers).get("X-Student-Id"), student);
    assert.equal(call.init?.body, undefined);
    assert.equal(call.url.includes(student), false);
  }
});

test("class client preserves exact id/name and sends only name and totalSeats", async () => {
  const api = new AdmissionApi("", async (_url, init) => {
    assert.deepEqual(JSON.parse(String(init?.body)), {
      name: "Class 3 · Morning",
      totalSeats: 20,
    });
    return Response.json(
      { id: classId, name: "Class 3 · Morning", totalSeats: 20 },
      { status: 201 },
    );
  });
  assert.equal((await api.createClass("Class 3 · Morning", 20)).id, classId);
});

test("client accepts 204 without reading JSON", async () => {
  const response = new Response(null, { status: 204 });
  response.json = async () => {
    throw new Error("must not read JSON");
  };
  const api = new AdmissionApi("", async () => response);
  assert.equal(await api.cancel(classId, student), undefined);
});

test("client surfaces structured conflicts and handles non-JSON server failures safely", async () => {
  const api = new AdmissionApi("", async () =>
    Response.json(
      { code: "CAPACITY_EXHAUSTED", message: "Full" },
      { status: 409 },
    ),
  );
  await assert.rejects(
    api.reserve(classId, student),
    (error: unknown) =>
      error instanceof ApiError &&
      error.status === 409 &&
      error.code === "CAPACITY_EXHAUSTED",
  );
  const broken = new AdmissionApi(
    "",
    async () => new Response("<html>bad gateway</html>", { status: 502 }),
  );
  await assert.rejects(broken.classes(), /service|server|connection/i);
});

test("client rejects invalid responses and reports network failures", async () => {
  const invalid = new AdmissionApi("", async () =>
    Response.json([{ classId, className: "Wrong schema" }]),
  );
  await assert.rejects(invalid.classes(), /response/i);
  const network = new AdmissionApi("", async () => {
    throw new TypeError("Failed to fetch");
  });
  await assert.rejects(network.classes(), /connect|network/i);
});

test("default fetch adapter does not bind browser fetch to the API client", async () => {
  const original = globalThis.fetch;
  try {
    globalThis.fetch = async function (this: unknown) {
      assert.ok(
        this === globalThis || this === undefined,
        "fetch must retain a browser-compatible receiver",
      );
      return Response.json([]);
    };
    assert.deepEqual(await new AdmissionApi("").classes(), []);
  } finally {
    globalThis.fetch = original;
  }
});

test("client rejects an empty success where class data is required", async () => {
  const api = new AdmissionApi(
    "",
    async () => new Response(null, { status: 204 }),
  );
  await assert.rejects(api.classes(), /response/i);
});

test("client rejects an unknown hold status", async () => {
  const api = new AdmissionApi("", async () =>
    Response.json({
      holdId: classId,
      classId,
      status: "PAID",
      expiresAt: "2026-11-11T12:00:00Z",
    }),
  );
  await assert.rejects(api.hold(classId), /response/i);
});
