import http from "k6/http";
import { check, sleep } from "k6";

const apiBaseUrl = (__ENV.API_BASE_URL || "https://api.sassoir.com").replace(/\/+$/, "");
const eventSlug = __ENV.EVENT_SLUG || "licha-roula-s-wedding";
const searchTerms = (__ENV.SEARCH_TERMS || "roula,lichaa,antonella,karim,maya,sarah").split(",");
const publicTokens = (__ENV.PUBLIC_TOKENS || "").split(",").filter(Boolean);

export const options = {
  scenarios: {
    public_event_burst: {
      executor: "constant-vus",
      vus: 150,
      duration: "45s",
      exec: "loadPublicEvent",
    },
    public_search_burst: {
      executor: "constant-vus",
      vus: 150,
      duration: "60s",
      startTime: "50s",
      exec: "searchGuests",
    },
    public_seat_results: {
      executor: "constant-vus",
      vus: publicTokens.length > 0 ? 150 : 1,
      duration: "45s",
      startTime: "1m55s",
      exec: "seatResults",
    },
    mixed_public_during_admin_work: {
      executor: "constant-vus",
      vus: 150,
      duration: "60s",
      startTime: "2m45s",
      exec: "searchGuests",
    },
    safety_margin_200: {
      executor: "constant-vus",
      vus: 200,
      duration: "60s",
      startTime: "3m50s",
      exec: "loadPublicEvent",
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
    "http_req_duration{endpoint:public_event}": ["p(95)<200"],
    "http_req_duration{endpoint:guest_search}": ["p(95)<250"],
    "http_req_duration{endpoint:seat_result}": ["p(95)<300"],
  },
};

export function loadPublicEvent() {
  const response = http.get(`${apiBaseUrl}/api/public/events/${eventSlug}`, {
    tags: { endpoint: "public_event" },
  });
  check(response, { "public event ok": (res) => res.status === 200 || res.status === 404 });
  sleep(Math.random() * 2);
}

export function searchGuests() {
  const query = searchTerms[Math.floor(Math.random() * searchTerms.length)]?.trim() || "ro";
  const response = http.post(
    `${apiBaseUrl}/api/public/events/${eventSlug}/guests/search`,
    JSON.stringify({ query }),
    {
      headers: { "Content-Type": "application/json" },
      tags: { endpoint: "guest_search" },
    },
  );
  check(response, { "search ok": (res) => res.status === 200 || res.status === 404 || res.status === 429 });
  sleep(0.3 + Math.random() * 1.2);
}

export function seatResults() {
  if (publicTokens.length === 0) {
    sleep(1);
    return;
  }

  const token = publicTokens[Math.floor(Math.random() * publicTokens.length)];
  const response = http.get(`${apiBaseUrl}/api/public/events/${eventSlug}/guests/${token}`, {
    tags: { endpoint: "seat_result" },
  });
  check(response, { "seat result ok": (res) => res.status === 200 || res.status === 404 || res.status === 429 });
  sleep(0.5 + Math.random());
}
