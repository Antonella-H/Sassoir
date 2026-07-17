import { ChangeEvent, Component, FormEvent, ReactNode, useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Archive,
  ArrowLeft,
  BarChart3,
  CalendarDays,
  Check,
  ClipboardList,
  Download,
  Eye,
  FileWarning,
  LayoutDashboard,
  Lock,
  LogOut,
  LocateFixed,
  Map,
  MapPin,
  Minus,
  Pencil,
  Plus,
  QrCode,
  Search,
  Send,
  Settings,
  ShieldCheck,
  Table2,
  Trash2,
  Upload,
  Users,
  X,
} from "lucide-react";
import "./styles.css";
import { createQrDataUri, createQrSvg } from "./qr";

const guestWeddingBanner = "/guest-wedding-banner.png";
const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

function apiUrl(path: string) {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  return `${apiBaseUrl}${normalizedPath}`;
}

function assetUrl(url: string | null | undefined) {
  if (!url) return "";
  if (/^(https?:|data:|blob:)/i.test(url)) return url;
  if (url.startsWith("/guest-wedding-banner.png")) return url;
  return apiUrl(url);
}

type Guest = {
  publicToken: string;
  displayName: string;
  searchAliases: string[];
  groupLabel: string;
  tableCode: string;
  tableName: string;
  seatNumber?: string | null;
  directions: string;
  companions: string[];
};

type SearchResult = {
  publicToken: string;
  displayName: string;
  groupLabel: string;
};

type PublicSeatResult = {
  publicToken: string;
  displayName: string;
  groupLabel: string;
  tableCode: string;
  tableName: string;
  seatNumber?: string | null;
  directions: string;
  companions: string[];
  event?: PublicEvent;
  floorPlan?: { objects?: unknown[] } | null;
  highlightedObjectId?: string | null;
};

type FloorObject = {
  id: string;
  type: string;
  label: string;
  linkedTableId?: string | null;
  tableCode?: string | null;
  tableName?: string | null;
  x: number;
  y: number;
  width: number;
  height: number;
  shape: "round" | "square" | "rectangle" | "tear" | "rect";
  zIndex?: number;
};

type AdminGuest = {
  id: string;
  firstName: string;
  lastName: string;
  displayName: string;
  notes: string;
  personCount: number;
  tableId?: string | null;
  tableCode: string;
  tableName: string;
  status: "Active" | "Cancelled" | "CheckedIn" | "Archived";
  isDuplicate: boolean;
};

type AdminTable = {
  id: string;
  name: string;
  number: string;
  maximumCapacity: number;
  assignedGuestCount: number;
  shape: "round" | "square" | "rectangle" | "tear";
  notes: string;
};

type AdminTableDraft = {
  name: string;
  number: string;
  maximumCapacity: number;
  shape: AdminTable["shape"];
  notes: string;
};

type AdminGuestMessage = {
  id: string;
  guestName: string;
  message: string;
  createdAt: string;
};

type ContactSubmission = {
  id: string;
  name: string;
  email: string;
  message: string;
  submittedAtUtc: string;
};

type GuestListCacheEntry = {
  guests: AdminGuest[];
  tables: AdminTable[];
};

type FloorPlanCacheEntry = GuestListCacheEntry & {
  floorObjects: FloorObject[];
};

type PaginatedResponse<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};

type AdminEventCacheEntry = {
  guests?: AdminGuest[];
  allDesignGuests?: AdminGuest[];
  tables?: AdminTable[];
  floorObjects?: FloorObject[];
};

const adminEventCache = new globalThis.Map<string, AdminEventCacheEntry>();
const adminGuestPageCache = new globalThis.Map<string, PaginatedResponse<AdminGuest>>();
const adminGuestPageRequests = new globalThis.Map<string, Promise<PaginatedResponse<AdminGuest>>>();
const adminTablePageCache = new globalThis.Map<string, PaginatedResponse<AdminTable>>();
const adminTablePageRequests = new globalThis.Map<string, Promise<PaginatedResponse<AdminTable>>>();
const adminTableRequests = new globalThis.Map<string, Promise<AdminTable[]>>();
const adminFloorPlanRequests = new globalThis.Map<string, Promise<FloorPlanCacheEntry>>();
const publicEventCache = new globalThis.Map<string, { event: PublicEvent; floorObjects: FloorObject[] }>();
const minimumGuestSearchCharacters = 2;

function clearEventAdminCaches(eventId: string) {
  adminEventCache.delete(eventId);
  adminTableRequests.delete(eventId);
  adminFloorPlanRequests.delete(eventId);
  [...adminGuestPageCache.keys()]
    .filter((key) => key.startsWith(`${eventId}:`))
    .forEach((key) => adminGuestPageCache.delete(key));
  [...adminGuestPageRequests.keys()]
    .filter((key) => key.startsWith(`${eventId}:`))
    .forEach((key) => adminGuestPageRequests.delete(key));
  [...adminTablePageCache.keys()]
    .filter((key) => key.startsWith(`${eventId}:`))
    .forEach((key) => adminTablePageCache.delete(key));
  [...adminTablePageRequests.keys()]
    .filter((key) => key.startsWith(`${eventId}:`))
    .forEach((key) => adminTablePageRequests.delete(key));
}

function adminGuestPageKey(eventId: string, params: { page: number; pageSize: number; search: string; status: string; tableId: string }) {
  return [eventId, params.page, params.pageSize, params.search, params.status, params.tableId].join(":");
}

async function getAdminGuestPage(eventId: string, token: string, params: { page: number; pageSize: number; search: string; status: string; tableId: string }, options?: { force?: boolean }) {
  const cacheKey = adminGuestPageKey(eventId, params);
  if (!options?.force) {
    const cached = adminGuestPageCache.get(cacheKey);
    if (cached) return cached;

    const pending = adminGuestPageRequests.get(cacheKey);
    if (pending) return pending;
  }

  const searchParams = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
  });
  if (params.search) searchParams.set("search", params.search);
  if (params.status !== "All") searchParams.set("status", params.status);
  if (params.tableId !== "All") searchParams.set("tableId", params.tableId);

  const request = fetch(apiUrl(`/api/admin/events/${eventId}/guests/page?${searchParams.toString()}`), {
    headers: { Authorization: `Bearer ${token}` },
  }).then(async (response) => {
    if (!response.ok) throw new Error(await readError(response));
    const payload = (await response.json()) as PaginatedResponse<AdminGuest>;
    adminGuestPageCache.set(cacheKey, payload);
    return payload;
  }).finally(() => {
    adminGuestPageRequests.delete(cacheKey);
  });

  adminGuestPageRequests.set(cacheKey, request);
  return request;
}

async function getAllAdminDesignGuests(eventId: string, token: string, options?: { force?: boolean }) {
  const cached = adminEventCache.get(eventId);
  if (!options?.force && cached?.allDesignGuests) return cached.allDesignGuests;

  const pageSize = 100;
  const firstPage = await getAdminGuestPage(eventId, token, {
    page: 1,
    pageSize,
    search: "",
    status: "All",
    tableId: "All",
  }, options);

  const totalPages = Math.max(1, Math.ceil(firstPage.totalCount / pageSize));
  const remainingPages = totalPages <= 1
    ? []
    : await Promise.all(Array.from({ length: totalPages - 1 }, (_, index) => getAdminGuestPage(eventId, token, {
      page: index + 2,
      pageSize,
      search: "",
      status: "All",
      tableId: "All",
    }, options)));

  const guests = [firstPage, ...remainingPages].flatMap((page) => page.items);
  const previous = adminEventCache.get(eventId) ?? {};
  adminEventCache.set(eventId, { ...previous, guests, allDesignGuests: guests });
  return guests;
}

async function getAdminTables(eventId: string, token: string, options?: { force?: boolean }) {
  const cached = adminEventCache.get(eventId);
  if (!options?.force && cached?.tables) return cached.tables;

  const requestKey = `${eventId}:${token}`;
  const pending = adminTableRequests.get(requestKey);
  if (!options?.force && pending) return pending;

  const request = fetch(apiUrl(`/api/admin/events/${eventId}/tables/page?page=1&pageSize=100`), {
    headers: { Authorization: `Bearer ${token}` },
  }).then(async (response) => {
    if (!response.ok) throw new Error(await readError(response));
    const payload = (await response.json()) as PaginatedResponse<AdminTable>;
    const tables = payload.items;
    const previous = adminEventCache.get(eventId) ?? {};
    adminEventCache.set(eventId, { ...previous, tables });
    return tables;
  }).finally(() => {
    adminTableRequests.delete(requestKey);
  });

  adminTableRequests.set(requestKey, request);
  return request;
}

function adminTablePageKey(eventId: string, params: { page: number; pageSize: number; search: string }) {
  return [eventId, params.page, params.pageSize, params.search].join(":");
}

async function getAdminTablePage(eventId: string, token: string, params: { page: number; pageSize: number; search: string }, options?: { force?: boolean }) {
  const cacheKey = adminTablePageKey(eventId, params);
  if (!options?.force) {
    const cached = adminTablePageCache.get(cacheKey);
    if (cached) return cached;

    const pending = adminTablePageRequests.get(cacheKey);
    if (pending) return pending;
  }

  const searchParams = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
  });
  if (params.search) searchParams.set("search", params.search);

  const request = fetch(apiUrl(`/api/admin/events/${eventId}/tables/page?${searchParams.toString()}`), {
    headers: { Authorization: `Bearer ${token}` },
  }).then(async (response) => {
    if (!response.ok) throw new Error(await readError(response));
    const payload = (await response.json()) as PaginatedResponse<AdminTable>;
    adminTablePageCache.set(cacheKey, payload);
    return payload;
  }).finally(() => {
    adminTablePageRequests.delete(cacheKey);
  });

  adminTablePageRequests.set(cacheKey, request);
  return request;
}

async function getAdminFloorPlan(eventId: string, token: string, options?: { force?: boolean }) {
  const cached = adminEventCache.get(eventId);
  if (!options?.force && cached?.guests && cached.tables && cached.floorObjects) {
    return { guests: cached.guests, tables: cached.tables, floorObjects: cached.floorObjects };
  }

  const requestKey = `${eventId}:${token}`;
  const pending = adminFloorPlanRequests.get(requestKey);
  if (!options?.force && pending) return pending;

  const request = Promise.all([
    getAdminTables(eventId, token, options),
    getAllAdminDesignGuests(eventId, token, options),
    fetch(apiUrl(`/api/admin/events/${eventId}/floor-plan`), { headers: { Authorization: `Bearer ${token}` } }),
  ]).then(async ([tables, guests, floorPlanResponse]) => {
    if (!floorPlanResponse.ok) throw new Error(await readError(floorPlanResponse));

    const floorPlanPayload = await floorPlanResponse.json();
    const floorObjects = withTableFloorObjects(toFloorObjects(floorPlanPayload?.objects), tables);
    const previous = adminEventCache.get(eventId) ?? {};
    adminEventCache.set(eventId, { ...previous, guests, allDesignGuests: guests, tables, floorObjects });
    return { guests, tables, floorObjects };
  }).finally(() => {
    adminFloorPlanRequests.delete(requestKey);
  });

  adminFloorPlanRequests.set(requestKey, request);
  return request;
}

type PublicEvent = {
  name: string;
  slug: string;
  eventType: string;
  subtitle: string;
  dateLabel: string;
  venueName: string;
  venueAddress: string;
  theme: {
    logoText: string;
    heroText: string;
    primaryColor: string;
    secondaryColor: string;
    backgroundColor: string;
    textColor: string;
    welcomeTitle: string;
    searchInputLabel: string;
    searchPlaceholder: string;
    heroImageUrl?: string | null;
  };
};

type AdminEvent = {
  id: string;
  name: string;
  slug: string;
  eventType: string;
  subtitle: string;
  dateLabel: string;
  venueName: string;
  venueAddress: string;
  status: string | number;
  heroText: string;
  primaryColor: string;
  secondaryColor: string;
  backgroundColor: string;
  textColor: string;
  welcomeTitle: string;
  searchInputLabel: string;
  searchPlaceholder: string;
  heroImageUrl?: string | null;
  guestCount: number;
  assignedGuests: number;
};

type AdminUser = {
  email: string;
  displayName: string;
  roles: string[];
};

type AuthPayload = AdminUser & {
  token: string;
  refreshToken: string;
  expiresAt: string;
  refreshExpiresAt: string;
};

type AdminEventDraft = {
  name: string;
  slug: string;
  eventType: string;
  subtitle: string;
  dateLabel: string;
  venueName: string;
  venueAddress: string;
  status: string;
  heroText: string;
  primaryColor: string;
  secondaryColor: string;
  backgroundColor: string;
  textColor: string;
  welcomeTitle: string;
  searchInputLabel: string;
  searchPlaceholder: string;
  heroImageUrl: string;
};

type AdminGuestDraft = {
  firstName: string;
  lastName: string;
  displayName: string;
  notes: string;
  personCount: number;
  tableId: string;
  status: AdminGuest["status"];
};

type ImportPreviewRow = {
  rowNumber: number;
  firstName: string;
  lastName: string;
  displayName: string;
  notes: string;
  personCount: number;
  isDuplicate: boolean;
  errors: string[];
};

type ImportPreview = {
  rows: ImportPreviewRow[];
  errorCount: number;
  duplicateCount: number;
};

type EventFieldDefinition = {
  label: string;
  draftField?: keyof AdminEventDraft;
  type?: "text" | "color" | "select" | "url";
  options?: string[];
  placeholder?: string;
};

type EventSubsectionDefinition = {
  name: string;
  fields: EventFieldDefinition[];
};

type EventSectionDefinition = {
  name: string;
  subsections: EventSubsectionDefinition[];
};

type PublicMode = "search" | "seat";
type PublicLoadState = "loading" | "ready" | "notFound" | "offline";
type AdminPage = "dashboard" | "events" | "guests" | "floor-plan" | "publish" | "analytics" | "contact-submissions" | "settings";

const defaultEventSlug = "lichaa-and-roula";

const fallbackEvent: PublicEvent = {
  name: "Lichaa & Roula's Wedding",
  slug: defaultEventSlug,
  eventType: "Wedding",
  subtitle: "Together with their families, they welcome you to an evening of love, dinner, and dancing.",
  dateLabel: "Saturday, August 22",
  venueName: "The Olive Garden Venue",
  venueAddress: "Beirut, Lebanon",
  theme: {
    logoText: "L & R",
    heroText: "An elegant garden celebration under soft summer lights.",
    primaryColor: "#D8CFBC",
    secondaryColor: "#565449",
    backgroundColor: "#FFFBF4",
    textColor: "#11120D",
    welcomeTitle: "Welcome to Licha & Roula's wedding",
    searchInputLabel: "Search by name",
    searchPlaceholder: "Search by name",
    heroImageUrl: guestWeddingBanner,
  },
};

const fallbackAdminEvents: AdminEvent[] = [
  {
    id: "2eb2f4b0-67c8-4d99-a91f-caa1007084e8",
    name: fallbackEvent.name,
    slug: fallbackEvent.slug,
    eventType: fallbackEvent.eventType,
    subtitle: fallbackEvent.subtitle,
    dateLabel: fallbackEvent.dateLabel,
    venueName: fallbackEvent.venueName,
    venueAddress: fallbackEvent.venueAddress,
    status: "Published",
    heroText: fallbackEvent.theme.heroText,
    primaryColor: fallbackEvent.theme.primaryColor,
    secondaryColor: fallbackEvent.theme.secondaryColor,
    backgroundColor: fallbackEvent.theme.backgroundColor,
    textColor: fallbackEvent.theme.textColor,
    welcomeTitle: fallbackEvent.theme.welcomeTitle,
    searchInputLabel: fallbackEvent.theme.searchInputLabel,
    searchPlaceholder: fallbackEvent.theme.searchPlaceholder,
    heroImageUrl: fallbackEvent.theme.heroImageUrl,
    guestCount: 6,
    assignedGuests: 6,
  },
];

const fallbackGuests: Guest[] = [
  { publicToken: "guest-sarah-lichaa", displayName: "Sarah Lichaa", searchAliases: ["sarah", "sara lichaa", "\u0633\u0627\u0631\u0629 \u0644\u062d\u0627\u0621"], groupLabel: "Lichaa Family", tableCode: "12", tableName: "The Olive Garden", seatNumber: "4", directions: "Near the dance floor, with a clear view of the stage.", companions: ["Roula L.", "Maya K.", "Karim H."] },
  { publicToken: "guest-roula-lichaa", displayName: "Roula Lichaa", searchAliases: ["roula", "rula", "\u0631\u0648\u0644\u0627"], groupLabel: "Couple's Table", tableCode: "12", tableName: "The Olive Garden", seatNumber: "1", directions: "Near the dance floor, with a clear view of the stage.", companions: ["Sarah L.", "Maya K.", "Karim H."] },
  { publicToken: "guest-maya-k", displayName: "Maya K.", searchAliases: ["maya", "maia", "\u0645\u0627\u064a\u0627"], groupLabel: "Friends of Roula", tableCode: "12", tableName: "The Olive Garden", seatNumber: "5", directions: "Near the dance floor, with a clear view of the stage.", companions: ["Sarah L.", "Roula L.", "Karim H."] },
  { publicToken: "guest-antonella-hitti", displayName: "Antonella Hitti", searchAliases: ["antonella", "antoinella", "hitti", "\u0627\u0646\u0637\u0648\u0646\u064a\u0644\u0627"], groupLabel: "Hitti Family", tableCode: "8", tableName: "Cedar Grove", seatNumber: "2", directions: "Close to the garden entrance.", companions: ["Nadine H.", "Marc H."] },
  { publicToken: "guest-antonella-h", displayName: "Antonella H.", searchAliases: ["antonella guest of roula", "anto"], groupLabel: "Guest of Roula", tableCode: "10", tableName: "Jasmine Court", directions: "Beside the left garden aisle.", companions: ["Lea R.", "Nour S."] },
  { publicToken: "guest-karim-h", displayName: "Karim Haddad", searchAliases: ["karim", "\u0643\u0631\u064a\u0645"], groupLabel: "Friends of Lichaa", tableCode: "14", tableName: "Terrace", directions: "Near the lower terrace aisle.", companions: ["Omar D.", "Elias B."] },
];

const fallbackFloorObjects: FloorObject[] = [
  { id: "stage", type: "stage", label: "Stage", x: 0.35, y: 0.06, width: 0.38, height: 0.11, shape: "rect" },
  { id: "table-8", type: "table", label: "Table 8", x: 0.13, y: 0.25, width: 0.15, height: 0.15, shape: "round" },
  { id: "table-10", type: "table", label: "Table 10", x: 0.13, y: 0.53, width: 0.16, height: 0.16, shape: "round" },
  { id: "dance", type: "dance", label: "Dance Floor", x: 0.42, y: 0.4, width: 0.28, height: 0.25, shape: "rect" },
  { id: "bar", type: "bar", label: "Bar", x: 0.82, y: 0.27, width: 0.13, height: 0.25, shape: "rect" },
  { id: "table-12", type: "table", label: "Table 12", x: 0.76, y: 0.56, width: 0.15, height: 0.15, shape: "round" },
  { id: "restroom", type: "restroom", label: "Toilets", x: 0.83, y: 0.69, width: 0.13, height: 0.12, shape: "rect" },
  { id: "table-14", type: "table", label: "Table 14", x: 0.75, y: 0.82, width: 0.16, height: 0.16, shape: "round" },
  { id: "entrance", type: "entrance", label: "Entrance", x: 0.1, y: 0.83, width: 0.15, height: 0.09, shape: "rect" },
];

const eventTypeOptions = ["Wedding", "Corporate", "Gala", "Conference", "Birthday", "Private Dinner", "Other"];
const eventStatusOptions = ["Draft", "Published", "Archived"];
const tableShapeOptions: Array<{ value: AdminTable["shape"]; label: string }> = [
  { value: "round", label: "Round" },
  { value: "square", label: "Square" },
  { value: "rectangle", label: "Rectangle" },
  { value: "tear", label: "Tear shaped" },
];
const floorSectionTemplates: Array<Pick<FloorObject, "type" | "label" | "width" | "height" | "shape">> = [
  { type: "stage", label: "Stage", width: 0.34, height: 0.1, shape: "rect" },
  { type: "dance", label: "Dance Floor", width: 0.26, height: 0.2, shape: "rect" },
  { type: "entrance", label: "Entrance", width: 0.16, height: 0.08, shape: "rect" },
  { type: "bar", label: "Bar", width: 0.14, height: 0.18, shape: "rect" },
  { type: "restroom", label: "Toilets", width: 0.14, height: 0.1, shape: "rect" },
];

const eventFormSections: EventSectionDefinition[] = [
  {
    name: "General",
    subsections: [
      {
        name: "Basic information",
        fields: [
          { label: "Event Title", draftField: "name", placeholder: "Lichaa & Roula's Wedding" },
          { label: "Event Type", draftField: "eventType", type: "select", options: eventTypeOptions },
          { label: "Event Status", draftField: "status", type: "select", options: eventStatusOptions },
        ],
      },
    ],
  },
  {
    name: "Branding",
    subsections: [
      {
        name: "General",
        fields: [
          { label: "Primary Color", draftField: "primaryColor", type: "color" },
          { label: "Secondary Color", draftField: "secondaryColor", type: "color" },
          { label: "Text Color", draftField: "textColor", type: "color" },
          { label: "Background Color", draftField: "backgroundColor", type: "color" },
        ],
      },
      {
        name: "Welcome page content",
        fields: [
          { label: "Welcome Title", draftField: "welcomeTitle", placeholder: "Welcome to Licha & Roula's wedding" },
          { label: "Search Input Label", draftField: "searchInputLabel", placeholder: "Search by name" },
          { label: "Search Placeholder", draftField: "searchPlaceholder", placeholder: "Search by name" },
        ],
      },
    ],
  },
  {
    name: "Guests",
    subsections: [
      {
        name: "Guest list",
        fields: [],
      },
    ],
  },
  {
    name: "Floor Plan",
    subsections: [
      {
        name: "Tables",
        fields: [],
      },
      {
        name: "Design",
        fields: [],
      },
    ],
  },
  {
    name: "Setup",
    subsections: [
      {
        name: "QR Code",
        fields: [],
      },
      {
        name: "Guest messages",
        fields: [],
      },
    ],
  },
];

function getRoute() {
  const path = window.location.pathname || "/";

  if (path === "/") {
    return {
      area: "landing" as const,
    };
  }

  const eventMatch = path.match(/^\/e\/([^/]+)/);
  if (eventMatch?.[1]) {
    return {
      area: "public" as const,
      eventSlug: decodeURIComponent(eventMatch[1]),
    };
  }

  return {
    area: "admin" as const,
    adminPage: getAdminPage(path),
  };
}

function getAdminPage(path: string): AdminPage {
  if (path.includes("/admin/events")) return "events";
  if (path.includes("/admin/guests")) return "guests";
  if (path.includes("/admin/floor-plan")) return "floor-plan";
  if (path.includes("/admin/publish")) return "publish";
  if (path.includes("/admin/analytics")) return "analytics";
  if (path.includes("/admin/contact-submissions")) return "contact-submissions";
  if (path.includes("/admin/settings")) return "settings";
  return "dashboard";
}

function normalizeSearch(value: string) {
  return value
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[\u0623\u0625\u0622\u0671]/g, "\u0627")
    .replace(/\u0649/g, "\u064a")
    .replace(/\u0624/g, "\u0648")
    .replace(/\u0626/g, "\u064a")
    .replace(/\u0629/g, "\u0647")
    .toLowerCase()
    .replace(/\s+/g, " ")
    .trim();
}

function rankGuest(guest: Guest, rawQuery: string) {
  const query = normalizeSearch(rawQuery);
  const display = normalizeSearch(guest.displayName);
  const aliases = guest.searchAliases.map(normalizeSearch);

  if (display === query) return 1;
  if (aliases.includes(query)) return 2;
  if (display.startsWith(query)) return 3;
  if (aliases.some((alias) => alias.startsWith(query))) return 4;
  if (display.includes(query)) return 5;
  if (aliases.some((alias) => alias.includes(query))) return 6;
  return 99;
}

function findFallbackGuest(publicToken: string) {
  return fallbackGuests.find((guest) => guest.publicToken === publicToken);
}

function toFloorObjects(objects: any[] | undefined): FloorObject[] {
  if (!Array.isArray(objects)) return fallbackFloorObjects;

  return objects.map((item) => ({
    id: String(item.id),
    type: String(item.objectType ?? item.type ?? "venue"),
    label: String(item.label ?? ""),
    linkedTableId: item.linkedTableId ?? null,
    tableCode: item.tableCode ?? null,
    tableName: item.tableName ?? null,
    x: Number(item.x),
    y: Number(item.y),
    width: Number(item.width),
    height: Number(item.height),
    shape: normalizeFloorShape(item.shape),
    zIndex: Number(item.zIndex ?? 0),
  }));
}

function normalizeFloorShape(shape: unknown): FloorObject["shape"] {
  if (shape === "round" || shape === "square" || shape === "rectangle" || shape === "tear" || shape === "rect") return shape;
  return "rect";
}

function withTableFloorObjects(objects: FloorObject[], tables: AdminTable[]) {
  const merged = [...objects];

  tables.forEach((table, index) => {
    const existingIndex = merged.findIndex((object) => object.linkedTableId === table.id || (object.type === "table" && object.tableCode === table.number));
    if (existingIndex >= 0) {
      const existing = merged[existingIndex];
      const shapeChanged = existing.shape !== table.shape;
      merged[existingIndex] = {
        ...existing,
        label: table.name || `Table ${table.number}`,
        linkedTableId: table.id,
        tableCode: table.number,
        tableName: table.name,
        shape: table.shape,
        width: shapeChanged ? tableShapeDefaultWidth(table.shape) : existing.width,
        height: shapeChanged ? tableShapeDefaultHeight(table.shape) : existing.height,
      };
      return;
    }

    merged.push({
      id: `table-${table.id}`,
      type: "table",
      label: table.name || `Table ${table.number}`,
      linkedTableId: table.id,
      tableCode: table.number,
      tableName: table.name,
      x: 0.12 + (index % 4) * 0.18,
      y: 0.24 + Math.floor(index / 4) * 0.18,
      width: tableShapeDefaultWidth(table.shape),
      height: tableShapeDefaultHeight(table.shape),
      shape: table.shape,
      zIndex: 5 + index,
    });
  });

  return merged;
}

function tableShapeDefaultWidth(shape: AdminTable["shape"]) {
  return shape === "rectangle" ? 0.18 : 0.14;
}

function tableShapeDefaultHeight(shape: AdminTable["shape"]) {
  return shape === "rectangle" ? 0.11 : 0.14;
}

function floorObjectsForSave(objects: FloorObject[]) {
  return objects.map((object, index) => ({
    id: object.id,
    objectType: object.type,
    label: object.label,
    linkedTableId: object.linkedTableId ?? null,
    x: clampUnit(object.x),
    y: clampUnit(object.y),
    width: clampUnit(object.width),
    height: clampUnit(object.height),
    shape: object.shape,
    zIndex: object.zIndex ?? index,
  }));
}

function clampUnit(value: number) {
  if (Number.isNaN(value)) return 0;
  return Math.max(0, Math.min(1, value));
}

function safeGetStorage(key: string) {
  try {
    return window.localStorage.getItem(key) ?? "";
  } catch {
    return "";
  }
}

function safeSetStorage(key: string, value: string) {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // The app should still work in restricted preview browsers.
  }
}

function safeRemoveStorage(key: string) {
  try {
    window.localStorage.removeItem(key);
  } catch {
    // The app should still work in restricted preview browsers.
  }
}

function saveAdminSession(payload: AuthPayload) {
  safeSetStorage("sassoir_admin_token", payload.token);
  safeSetStorage("sassoir_admin_refresh_token", payload.refreshToken);
  safeSetStorage("sassoir_admin_expires_at", payload.expiresAt);
  safeSetStorage("sassoir_admin_refresh_expires_at", payload.refreshExpiresAt);
}

function clearAdminSession() {
  safeRemoveStorage("sassoir_admin_token");
  safeRemoveStorage("sassoir_admin_refresh_token");
  safeRemoveStorage("sassoir_admin_expires_at");
  safeRemoveStorage("sassoir_admin_refresh_expires_at");
}

function parseDateTime(value: string) {
  const timestamp = Date.parse(value);
  return Number.isNaN(timestamp) ? 0 : timestamp;
}

function eventStatusText(status: string | number) {
  if (typeof status === "string") return status;

  switch (status) {
    case 0:
      return "Draft";
    case 1:
      return "Published";
    case 2:
      return "Archived";
    default:
      return "Draft";
  }
}

function useDebouncedValue<T>(value: T, delayMs: number) {
  const [debouncedValue, setDebouncedValue] = useState(value);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedValue(value), delayMs);
    return () => window.clearTimeout(timer);
  }, [delayMs, value]);

  return debouncedValue;
}

export default function App() {
  return (
    <AppErrorBoundary>
      <RoutedApp />
    </AppErrorBoundary>
  );
}

function RoutedApp() {
  const [route, setRoute] = useState(getRoute);

  useEffect(() => {
    const syncRoute = () => setRoute(getRoute());
    window.addEventListener("popstate", syncRoute);
    return () => window.removeEventListener("popstate", syncRoute);
  }, []);

  if (route.area === "landing") return <LandingPage />;
  return route.area === "admin" ? <AdminDashboard page={route.adminPage} /> : <PublicGuestExperience eventSlug={route.eventSlug} />;
}

function LandingPage() {
  const [contactDraft, setContactDraft] = useState({ name: "", email: "", message: "" });
  const [contactState, setContactState] = useState<"idle" | "sending" | "success" | "error">("idle");
  const [contactMessage, setContactMessage] = useState("");

  useEffect(() => {
    const revealEls = Array.from(document.querySelectorAll<HTMLElement>(".landing-page .reveal"));
    const lineEl = document.querySelector<SVGSVGElement>(".landing-page .steps-line");
    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) entry.target.classList.add("in-view");
      });
    }, { threshold: 0.2 });
    revealEls.forEach((element) => observer.observe(element));

    const lineObserver = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) entry.target.classList.add("in-view");
      });
    }, { threshold: 0.3 });
    if (lineEl) lineObserver.observe(lineEl);

    return () => {
      observer.disconnect();
      lineObserver.disconnect();
    };
  }, []);

  function navigateToAdmin() {
    window.history.pushState({}, "", "/admin");
    window.dispatchEvent(new PopStateEvent("popstate"));
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  async function submitContact(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setContactState("sending");
    setContactMessage("");

    try {
      const response = await fetch(apiUrl("/api/contact"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(contactDraft),
      });
      if (!response.ok) throw new Error(await readError(response));

      setContactDraft({ name: "", email: "", message: "" });
      setContactState("success");
      setContactMessage("Message sent. We'll get back to you personally.");
    } catch (contactError) {
      setContactState("error");
      setContactMessage(contactError instanceof Error ? contactError.message : "Could not send your message.");
    }
  }

  return (
    <div className="landing-page">
      <header>
        <div className="header-inner">
          <a href="#" className="brand-mark" aria-label="S'assoir home">
            <img src="/sassoir-logo-sentence.png" alt="S'assoir" />
          </a>
          <nav>
            <div className="nav-links">
              <a className="nav-link" href="#how-it-works">How it works</a>
              <a className="nav-link" href="#planners">For planners</a>
              <a className="nav-link" href="#roadmap">What's next</a>
              <a className="nav-link" href="#contact">Contact</a>
            </div>
            <button className="btn-login" type="button" onClick={navigateToAdmin}>Log in</button>
          </nav>
        </div>
      </header>

      <section className="hero">
        <div className="hero-inner">
          <img className="hero-ghost" src="/sassoir-logo.png" alt="" />
          <div className="hero-text">
            <span className="eyebrow-script">Scan, Sit, Share</span>
            <h1>Every seat has a story.<br />Help your guests find <em>theirs</em> in seconds.</h1>
            <p className="lead">S'assoir turns a printed seating chart into a five-second phone tap. Guests scan a code, search their name, and walk straight to their table &mdash; no lines, no confusion, no clipboard at the door.</p>
            <div className="badge"><span className="dot" /> Under construction &mdash; new features added every week</div>
            <div className="hero-actions">
              <a href="#how-it-works" className="btn-primary">See how it works</a>
              <a href="#contact" className="btn-ghost">Get in touch</a>
            </div>
          </div>
        </div>
      </section>

      <section className="section" id="how-it-works">
        <div className="wrap">
          <div className="section-head reveal">
            <span className="eyebrow">The guest experience</span>
            <h2>Three steps. No app, no printed chart, no queue.</h2>
            <p>Built for weddings, galas, corporate dinners, conferences, and every event where seating matters.</p>
          </div>

          <div className="steps-wrap">
            <svg className="steps-line" viewBox="0 0 1000 140" preserveAspectRatio="none">
              <path d="M 165 40 C 300 -20, 380 120, 500 60 C 620 0, 700 130, 835 45" />
            </svg>
            <div className="steps">
              <div className="step reveal">
                <div className="icon">
                  <svg viewBox="0 0 24 24" fill="none" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="3" y="3" width="7" height="7" />
                    <rect x="14" y="3" width="7" height="7" />
                    <rect x="3" y="14" width="7" height="7" />
                    <line x1="14" y1="14" x2="14" y2="21" />
                    <line x1="21" y1="14" x2="21" y2="21" />
                    <line x1="14" y1="17.5" x2="21" y2="17.5" />
                  </svg>
                </div>
                <span className="num">01</span>
                <h3>Scan</h3>
                <p>A single QR code at the entrance opens the event's own branded page. No downloads, no accounts.</p>
              </div>
              <div className="step reveal">
                <div className="icon">
                  <svg viewBox="0 0 24 24" fill="none" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round">
                    <circle cx="12" cy="12" r="9" />
                    <path d="M8 13c0 2.2 1.8 4 4 4s4-1.8 4-4" />
                    <line x1="9" y1="10" x2="9" y2="10.5" />
                    <line x1="15" y1="10" x2="15" y2="10.5" />
                  </svg>
                </div>
                <span className="num">02</span>
                <h3>Sit</h3>
                <p>They search their name and see their exact table and seat on a clear, mobile-friendly floor plan.</p>
              </div>
              <div className="step reveal">
                <div className="icon">
                  <svg viewBox="0 0 24 24" fill="none" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round">
                    <circle cx="6" cy="12" r="2.6" />
                    <circle cx="18" cy="6" r="2.6" />
                    <circle cx="18" cy="18" r="2.6" />
                    <line x1="8.3" y1="10.8" x2="15.7" y2="7.2" />
                    <line x1="8.3" y1="13.2" x2="15.7" y2="16.8" />
                  </svg>
                </div>
                <span className="num">03</span>
                <h3>Share</h3>
                <p>Photos, moments, and table shoutouts &mdash; the next layer of the guest experience, on its way.</p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="section section-bone" id="planners">
        <div className="wrap">
          <div className="section-head reveal">
            <span className="eyebrow">For planners &amp; hosts</span>
            <h2>One dashboard for the whole night.</h2>
            <p>Replace spreadsheets and printed charts with an admin portal built to run the room.</p>
          </div>

          <div className="planner-grid reveal">
            <PlannerCard title="Guest lists" text="Import, organize, and update every guest in one place, from RSVP status to dietary notes." icon="users" />
            <PlannerCard title="Table & seat assignments" text="Build the floor plan and assign seats visually, then publish it as a searchable guest page." icon="table" />
            <PlannerCard title="Custom branding" text="Every event page carries its own colors, fonts, and identity - never a generic template." icon="brand" />
            <PlannerCard title="QR code generation" text="Generate and download entrance codes in one click, ready for print or digital signage." icon="qr" />
            <PlannerCard title="Lightweight analytics" text="See scans, searches, and arrival flow in real time, so you know how the room is filling." icon="analytics" />
            <PlannerCard title="One event, many hosts" text="Publish an event page in minutes and reuse the same platform for every event you run." icon="host" />
          </div>
        </div>
      </section>

      <section className="section section-dark" id="roadmap">
        <div className="wrap">
          <div className="section-head reveal">
            <span className="eyebrow">What's next</span>
            <h2>S'assoir is just getting started.</h2>
            <p>We're actively building. Here's what's joining the platform next.</p>
          </div>
          <div className="chips reveal">
            {["RSVP management", "Digital invitations", "Guest messaging", "Photo sharing", "Menus & schedules", "Sponsor sections", "Advanced analytics", "Subscription plans"].map((chip) => (
              <span className="chip" key={chip}>{chip}</span>
            ))}
          </div>
          <p className="roadmap-note reveal">Have a feature your events need? We'd love to hear it.</p>
        </div>
      </section>

      <section className="section" id="contact">
        <div className="wrap">
          <div className="contact-grid">
            <div className="contact-left reveal">
              <span className="eyebrow">Contact</span>
              <h2>Building something for your event?</h2>
              <p>Whether you're planning one event or running dozens a year, we'd love to hear what you need. Reach out and we'll get back to you personally.</p>
            </div>
            <form className="reveal" onSubmit={submitContact}>
              <div className="field">
                <label htmlFor="landing-contact-name">Name</label>
                <input id="landing-contact-name" type="text" value={contactDraft.name} onChange={(event) => setContactDraft({ ...contactDraft, name: event.target.value })} required />
              </div>
              <div className="field">
                <label htmlFor="landing-contact-email">Email</label>
                <input id="landing-contact-email" type="email" value={contactDraft.email} onChange={(event) => setContactDraft({ ...contactDraft, email: event.target.value })} required />
              </div>
              <div className="field">
                <label htmlFor="landing-contact-message">Message</label>
                <textarea id="landing-contact-message" value={contactDraft.message} onChange={(event) => setContactDraft({ ...contactDraft, message: event.target.value })} required />
              </div>
              <button type="submit" className="form-submit" disabled={contactState === "sending"}>{contactState === "sending" ? "Sending..." : "Send message"}</button>
              {contactMessage ? <p className={`contact-status ${contactState}`} role={contactState === "error" ? "alert" : "status"}>{contactMessage}</p> : null}
            </form>
          </div>
        </div>
      </section>

      <footer>
        <div className="footer-inner">
          <div className="footer-brand">
            <img src="/sassoir-logo.png" alt="S'assoir" />
            <span>S'assoir</span>
          </div>
          <div className="footer-tag">Scan. Sit. Share.</div>
          <div className="footer-copy">&copy; 2026 S'assoir. All rights reserved.</div>
        </div>
      </footer>
    </div>
  );
}

function PlannerCard({ title, text, icon }: { title: string; text: string; icon: "users" | "table" | "brand" | "qr" | "analytics" | "host" }) {
  const paths = {
    users: (
      <>
        <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
        <path d="M16 3.13a4 4 0 0 1 0 7.75" />
      </>
    ),
    table: (
      <>
        <rect x="3" y="3" width="18" height="18" rx="2" />
        <line x1="3" y1="9" x2="21" y2="9" />
        <line x1="9" y1="21" x2="9" y2="9" />
      </>
    ),
    brand: (
      <>
        <path d="M12 19l7-7 3 3-7 7-3-3z" />
        <path d="M18 13l-1.5-7.5L2 2l3.5 14.5L13 18l5-5z" />
        <path d="M2 2l7.586 7.586" />
        <circle cx="11" cy="11" r="2" />
      </>
    ),
    qr: (
      <>
        <rect x="3" y="3" width="7" height="7" />
        <rect x="14" y="3" width="7" height="7" />
        <rect x="3" y="14" width="7" height="7" />
        <rect x="14" y="14" width="7" height="7" />
      </>
    ),
    analytics: (
      <>
        <path d="M3 3v18h18" />
        <path d="M7 15l4-5 3 3 5-7" />
      </>
    ),
    host: (
      <>
        <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
        <circle cx="12" cy="7" r="4" />
      </>
    ),
  };

  return (
    <div className="planner-card">
      <svg className="icon" viewBox="0 0 24 24" fill="none" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round">{paths[icon]}</svg>
      <h3>{title}</h3>
      <p>{text}</p>
    </div>
  );
}

class AppErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state: { error: Error | null } = { error: null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  render() {
    if (this.state.error) {
      return (
        <main className="login-shell">
          <section className="status-card app-error-card" role="alert">
            <p className="eyebrow">Application error</p>
            <h1>Something blocked the admin page from rendering.</h1>
            <p>{this.state.error.message}</p>
            <button className="primary-button" type="button" onClick={() => {
              clearAdminSession();
              window.location.assign("/admin");
            }}>Reset Admin Session</button>
          </section>
        </main>
      );
    }

    return this.props.children;
  }
}

function PublicGuestExperience({ eventSlug }: { eventSlug: string }) {
  const [event, setEvent] = useState<PublicEvent>(fallbackEvent);
  const [floorObjects, setFloorObjects] = useState<FloorObject[]>(fallbackFloorObjects);
  const [query, setQuery] = useState("");
  const [remoteResults, setRemoteResults] = useState<SearchResult[] | null>(null);
  const [selectedGuest, setSelectedGuest] = useState<Guest | null>(null);
  const [mode, setMode] = useState<PublicMode>("search");
  const [searchTouched, setSearchTouched] = useState(false);
  const [loading, setLoading] = useState(false);
  const [loadState, setLoadState] = useState<PublicLoadState>("loading");
  const [apiOnline, setApiOnline] = useState(false);
  const [message, setMessage] = useState("");
  const [sent, setSent] = useState(false);
  const searchAbortRef = useRef<AbortController | null>(null);
  const seatAbortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    let cancelled = false;
    const cached = publicEventCache.get(eventSlug);
    const controller = new AbortController();

    async function loadEvent() {
      setLoadState("loading");
      setSelectedGuest(null);
      setMode("search");
      setQuery("");
      setRemoteResults(null);

      if (cached) {
        setEvent(cached.event);
        setFloorObjects(cached.floorObjects);
        setApiOnline(true);
        setLoadState("ready");
        return;
      }

      try {
        const [eventResponse, floorPlanResponse] = await Promise.all([
          fetch(apiUrl(`/api/public/events/${eventSlug}`), { signal: controller.signal }),
          fetch(apiUrl(`/api/public/events/${eventSlug}/floor-plan`), { signal: controller.signal }),
        ]);

        if (eventResponse.status === 404) {
          if (!cancelled) setLoadState("notFound");
          return;
        }

        if (!eventResponse.ok) throw new Error("API unavailable");

        const publicEvent = (await eventResponse.json()) as PublicEvent;
        const floorPlan = floorPlanResponse.ok ? await floorPlanResponse.json() : null;
        if (cancelled) return;

        const nextFloorObjects = toFloorObjects(floorPlan?.objects);
        setEvent(publicEvent);
        setFloorObjects(nextFloorObjects);
        publicEventCache.set(eventSlug, { event: publicEvent, floorObjects: nextFloorObjects });
        setApiOnline(true);
        setLoadState("ready");
      } catch (loadError) {
        if (cancelled) return;
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;

        if (eventSlug === defaultEventSlug) {
          setEvent(fallbackEvent);
          setFloorObjects(fallbackFloorObjects);
          setApiOnline(false);
          setLoadState("ready");
        } else {
          setApiOnline(false);
          setLoadState("offline");
        }
      }
    }

    void loadEvent();
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [eventSlug]);

  const localResults = useMemo(() => {
    if (normalizeSearch(query).length < minimumGuestSearchCharacters) return [];

    return fallbackGuests
      .map((guest) => ({ guest, rank: rankGuest(guest, query) }))
      .filter((match) => match.rank < 99)
      .sort((a, b) => a.rank - b.rank || a.guest.displayName.localeCompare(b.guest.displayName))
      .slice(0, 5)
      .map((match) => ({
        publicToken: match.guest.publicToken,
        displayName: match.guest.displayName,
        groupLabel: match.guest.groupLabel,
      }));
  }, [query]);

  const results = remoteResults ?? (apiOnline ? [] : localResults);

  const performSearch = useCallback(
    async (rawQuery: string) => {
      const normalizedQuery = normalizeSearch(rawQuery);
      setSearchTouched(true);

      if (normalizedQuery.length < minimumGuestSearchCharacters) {
        searchAbortRef.current?.abort();
        setRemoteResults(null);
        setLoading(false);
        return;
      }

      searchAbortRef.current?.abort();
      const controller = new AbortController();
      searchAbortRef.current = controller;
      setLoading(true);

      try {
        const response = await fetch(apiUrl(`/api/public/events/${eventSlug}/guests/search`), {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ query: rawQuery }),
          signal: controller.signal,
        });
        if (!response.ok) throw new Error("Search failed");
        const payload = await response.json();
        setRemoteResults(payload.results ?? []);
        setApiOnline(true);
      } catch (searchError) {
        if (searchError instanceof DOMException && searchError.name === "AbortError") return;
        setRemoteResults(null);
        setApiOnline(false);
      } finally {
        if (searchAbortRef.current === controller) {
          searchAbortRef.current = null;
          setLoading(false);
        }
      }
    },
    [eventSlug],
  );

  useEffect(() => {
    const normalizedQuery = normalizeSearch(query);
    setRemoteResults(null);

    if (normalizedQuery.length < minimumGuestSearchCharacters) {
      searchAbortRef.current?.abort();
      setLoading(false);
      return;
    }

    const timer = window.setTimeout(() => {
      void performSearch(query);
    }, 320);

    return () => window.clearTimeout(timer);
  }, [performSearch, query]);

  async function searchGuests(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    await performSearch(query);
  }

  async function chooseGuest(searchResult: SearchResult) {
    seatAbortRef.current?.abort();
    const controller = new AbortController();
    seatAbortRef.current = controller;

    try {
      const response = await fetch(apiUrl(`/api/public/events/${eventSlug}/guests/${searchResult.publicToken}`), { signal: controller.signal });
      if (!response.ok) throw new Error("Lookup failed");
      const payload = (await response.json()) as PublicSeatResult;
      setSelectedGuest({
        publicToken: searchResult.publicToken,
        displayName: payload.displayName,
        searchAliases: [],
        groupLabel: payload.groupLabel,
        tableCode: payload.tableCode,
        tableName: payload.tableName,
        seatNumber: payload.seatNumber,
        directions: payload.directions,
        companions: payload.companions ?? [],
      });
      if (payload.event) setEvent(payload.event);
      if (payload.floorPlan?.objects) {
        setFloorObjects(toFloorObjects(payload.floorPlan.objects as any[]));
      }
      setApiOnline(true);
    } catch (seatError) {
      if (seatError instanceof DOMException && seatError.name === "AbortError") return;
      const fallbackGuest = findFallbackGuest(searchResult.publicToken);
      if (!fallbackGuest) return;
      setSelectedGuest(fallbackGuest);
      setApiOnline(false);
    } finally {
      if (seatAbortRef.current === controller) {
        seatAbortRef.current = null;
      }
    }

    setMode("seat");
    setSent(false);
    setMessage("");
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  async function sendMessage(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    if (!selectedGuest || !message.trim()) return;

    try {
      const response = await fetch(apiUrl(`/api/public/events/${eventSlug}/guests/${selectedGuest.publicToken}/messages`), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message }),
      });
      setApiOnline(response.ok);
    } catch {
      setApiOnline(false);
    }

    setSent(true);
    setMessage("");
  }

  if (loadState === "loading") {
    return (
      <main className="page-shell public-shell">
        <section className="experience-frame" aria-label="Loading event">
          <div className="app-screen loading-screen">
            <span className="loading-mark" />
            <p className="eyebrow">Preparing event</p>
            <h1>Finding your welcome page...</h1>
          </div>
        </section>
      </main>
    );
  }

  if (loadState === "notFound" || loadState === "offline") {
    return (
      <main className="page-shell public-shell">
        <section className="experience-frame" aria-label="Event unavailable">
          <NotFoundScreen state={loadState} eventSlug={eventSlug} />
        </section>
      </main>
    );
  }

  return (
    <main
      className="page-shell public-shell"
      style={{
        "--event-primary": event.theme.primaryColor || fallbackEvent.theme.primaryColor,
        "--event-secondary": event.theme.secondaryColor || fallbackEvent.theme.secondaryColor,
        "--event-background": event.theme.backgroundColor || fallbackEvent.theme.backgroundColor,
        "--event-text": event.theme.textColor || fallbackEvent.theme.textColor,
        "--guest-ink": event.theme.textColor || fallbackEvent.theme.textColor,
        "--guest-olive": event.theme.secondaryColor || fallbackEvent.theme.secondaryColor,
        "--guest-bone": event.theme.primaryColor || fallbackEvent.theme.primaryColor,
        "--guest-paper": event.theme.backgroundColor || fallbackEvent.theme.backgroundColor,
      } as React.CSSProperties}
    >
      <section className="experience-frame" aria-label="Public event guest experience">
        {mode === "search" ? (
          <WelcomeScreen
            event={event}
            query={query}
            results={results}
            loading={loading}
            apiOnline={apiOnline}
            searchTouched={searchTouched}
            onQueryChange={(value) => {
              setQuery(value);
              setSearchTouched(false);
            }}
            onSearch={searchGuests}
            onSelectGuest={chooseGuest}
          />
        ) : null}

        {mode === "seat" && selectedGuest ? (
          <SeatScreen
            event={event}
            guest={selectedGuest}
            floorObjects={floorObjects}
            message={message}
            sent={sent}
            onBack={() => {
              setSelectedGuest(null);
              setMode("search");
            }}
            onMessageChange={setMessage}
            onSendMessage={sendMessage}
          />
        ) : null}
      </section>
    </main>
  );
}

function WelcomeScreen({ event, query, results, loading, apiOnline, searchTouched, onQueryChange, onSearch, onSelectGuest }: {
  event: PublicEvent;
  query: string;
  results: SearchResult[];
  loading: boolean;
  apiOnline: boolean;
  searchTouched: boolean;
  onQueryChange: (value: string) => void;
  onSearch: (event: FormEvent<HTMLFormElement>) => void;
  onSelectGuest: (guest: SearchResult) => void;
}) {
  const queryReady = normalizeSearch(query).length >= 2;
  const showEmpty = searchTouched && queryReady && !loading && results.length === 0;
  const welcomeTitle = event.theme.welcomeTitle || `Welcome to ${event.name}`;
  const searchInputLabel = event.theme.searchInputLabel || "Search by name";
  const searchPlaceholder = event.theme.searchPlaceholder || searchInputLabel;
  const heroImageUrl = assetUrl(event.theme.heroImageUrl) || guestWeddingBanner;

  return (
    <div className="app-screen guest-welcome-screen">
      <header
        className="guest-photo-banner"
        style={{
          backgroundImage: `linear-gradient(180deg, rgba(17,18,13,0.02), rgba(17,18,13,0.2)), url(${heroImageUrl})`,
        }}
        aria-label={`${event.name} photo banner`}
      />

      <section className="guest-welcome-content">
        <p className="guest-welcome-note">{welcomeTitle}</p>
        <h1>Please Find Your Seat</h1>

        <form className="guest-search-form" onSubmit={onSearch}>
          <label className="sr-only" htmlFor="guest-search">{searchInputLabel}</label>
          <input
            className="guest-search-input"
            id="guest-search"
            type="search"
            placeholder={searchPlaceholder}
            value={query}
            onChange={(event) => onQueryChange(event.target.value)}
            autoComplete="off"
          />
        </form>

        {loading ? <p className="guest-search-status" role="status">Searching...</p> : null}

        {results.length > 0 ? (
          <section className="guest-results-list" aria-label="Search results">
            {results.map((guest) => (
              <button className="guest-result-minimal" key={guest.publicToken} type="button" onClick={() => onSelectGuest(guest)}>
                {guest.displayName}
              </button>
            ))}
          </section>
        ) : null}

        {showEmpty ? <p className="guest-search-status" role="status">No guests found</p> : null}
        {!apiOnline ? <span className="guest-demo-source">Demo guest list</span> : null}

        <a className="guest-seat-logo-link" href="/" aria-label="Go to S'assoir home">
          <img className="guest-seat-logo" src="/sassoir-logo-sentence.png" alt="S'assoir - Scan. Sit. Share." />
        </a>
      </section>
    </div>
  );
}

function SeatScreen({ guest, floorObjects, message, sent, onBack, onMessageChange, onSendMessage }: {
  event: PublicEvent;
  guest: Guest;
  floorObjects: FloorObject[];
  message: string;
  sent: boolean;
  onBack: () => void;
  onMessageChange: (value: string) => void;
  onSendMessage: (event: FormEvent<HTMLFormElement>) => void;
}) {
  const tableGuests = guest.companions.length > 0 ? guest.companions : [guest.groupLabel].filter(Boolean);

  return (
    <div className="app-screen guest-seat-screen">
      <button className="guest-back-button" type="button" onClick={onBack} aria-label="Back to search"><ArrowLeft aria-hidden="true" /></button>

      <section className="guest-seat-content">
        <header className="guest-seat-hero">
          <h1>Welcome, {guest.displayName.split(" ")[0]}!</h1>
          <p className="guest-table-assignment">You are on table <strong>{guest.tableCode}{guest.tableName ? ` - "${guest.tableName}"` : ""}</strong></p>
          <span>Please find your way to your table</span>
        </header>

        <GuestFloorPlan floorObjects={floorObjects} tableCode={guest.tableCode} />

        <section className="guest-table-names" aria-label={`Guests at table ${guest.tableCode}`}>
          <p>You can find on your table</p>
          <div>
            {tableGuests.map((companion) => <span key={companion}>{companion}</span>)}
          </div>
        </section>

        <form className="guest-message-form" onSubmit={onSendMessage}>
          <label htmlFor="guest-message">Leave a message to the newlyweds</label>
          <textarea id="guest-message" value={message} onChange={(event) => onMessageChange(event.target.value)} rows={5} placeholder="Write your message..." />
          <button type="submit">Leave a Message</button>
          {sent ? <p role="status">Message saved. Thank you.</p> : null}
        </form>

        <a className="guest-seat-logo-link" href="/" aria-label="Go to S'assoir home">
          <img className="guest-seat-logo" src="/sassoir-logo-sentence.png" alt="S'assoir - Scan. Sit. Share." />
        </a>
      </section>
    </div>
  );
}

function MinimalFloorPlan({ tableCode }: { tableCode: string }) {
  return (
    <section className="minimal-floor-plan" aria-label={`Floor plan highlighting table ${tableCode}`}>
      <div className="plan-zone plan-stage">Stage</div>
      <div className="plan-zone plan-dance">Dance Floor</div>
      <div className="plan-zone plan-entrance">Entrance</div>
      <div className="plan-route" aria-hidden="true" />
      <div className="plan-table plan-table-one">1</div>
      <div className="plan-table plan-table-three">3</div>
      <div className="plan-table plan-table-seven">7</div>
      <div className="plan-table plan-table-highlight" aria-label={`Your table, table ${tableCode}`}>{tableCode}</div>
    </section>
  );
}

function GuestFloorPlan({ floorObjects, tableCode }: { floorObjects: FloorObject[]; tableCode: string }) {
  const objects = floorObjects.length > 0 ? floorObjects : fallbackFloorObjects;
  const highlightedObject = objects.find((object) => object.type === "table" && (object.tableCode === tableCode || object.id === `table-${tableCode}`));
  const entranceObject = objects.find((object) => object.type === "entrance");

  if (!highlightedObject) return <MinimalFloorPlan tableCode={tableCode} />;

  const routePath = buildGuestRoutePath(
    entranceObject ?? { id: "guest-route-entrance", type: "entrance", label: "Entrance", x: 0.12, y: 0.84, width: 0.16, height: 0.08, shape: "rect" },
    highlightedObject,
    objects,
  );

  return (
    <section className="minimal-floor-plan" aria-label={`Floor plan highlighting table ${tableCode}`}>
      <svg className="guest-plan-route-svg" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
        <path d={routePath} />
      </svg>
      {objects.map((object) => {
        const highlighted = object.id === highlightedObject.id;
        return (
          <div
            className={`guest-plan-object ${object.type} ${object.shape} ${highlighted ? "highlighted" : ""}`}
            key={object.id}
            style={{
              left: `${object.x * 100}%`,
              top: `${object.y * 100}%`,
              width: `${object.width * 100}%`,
              height: `${object.height * 100}%`,
              zIndex: object.zIndex ?? 1,
            }}
            aria-label={`${object.label}${highlighted ? ", your table" : ""}`}
          >
            {object.type === "table" ? object.tableName || object.tableCode || object.label.replace(/^Table\s+/i, "") : object.label}
          </div>
        );
      })}
    </section>
  );
}

function centerOfObject(object: Pick<FloorObject, "x" | "y" | "width" | "height">) {
  return {
    x: (object.x + object.width / 2) * 100,
    y: (object.y + object.height / 2) * 100,
  };
}

type FloorRoutePoint = { x: number; y: number };

function buildGuestRoutePath(startObject: FloorObject, endObject: FloorObject, objects: FloorObject[]) {
  const startCenter = centerOfObject(startObject);
  const endCenter = centerOfObject(endObject);
  const start = edgeAnchor(startObject, endCenter, 2);
  const end = edgeAnchor(endObject, startCenter, 2);
  const pathPoints = findFloorRoute(start, end, objects, new Set([startObject.id, endObject.id]));

  return pointsToPath(pathPoints.length > 0 ? pathPoints : [start, { x: start.x, y: end.y }, end]);
}

function edgeAnchor(object: FloorObject, toward: FloorRoutePoint, offset: number): FloorRoutePoint {
  const center = centerOfObject(object);
  const halfWidth = Math.max(object.width * 50, 1);
  const halfHeight = Math.max(object.height * 50, 1);
  const dx = toward.x - center.x;
  const dy = toward.y - center.y;

  if (Math.abs(dx / halfWidth) > Math.abs(dy / halfHeight)) {
    return {
      x: clampPercent(center.x + Math.sign(dx || 1) * (halfWidth + offset)),
      y: clampPercent(center.y),
    };
  }

  return {
    x: clampPercent(center.x),
    y: clampPercent(center.y + Math.sign(dy || 1) * (halfHeight + offset)),
  };
}

function findFloorRoute(start: FloorRoutePoint, end: FloorRoutePoint, objects: FloorObject[], ignoredObjectIds: Set<string>) {
  const gridStep = 2;
  const routePadding = 3.5;
  const obstacles = objects
    .filter((object) => !ignoredObjectIds.has(object.id))
    .map((object) => ({
      left: object.x * 100 - routePadding,
      right: (object.x + object.width) * 100 + routePadding,
      top: object.y * 100 - routePadding,
      bottom: (object.y + object.height) * 100 + routePadding,
    }));

  const blocked = (point: FloorRoutePoint) => obstacles.some((obstacle) => (
    point.x >= obstacle.left &&
    point.x <= obstacle.right &&
    point.y >= obstacle.top &&
    point.y <= obstacle.bottom
  ));

  const startGrid = nearestOpenGridPoint(start, gridStep, blocked);
  const endGrid = nearestOpenGridPoint(end, gridStep, blocked);
  const startKey = routeKey(startGrid);
  const endKey = routeKey(endGrid);
  const open = new Set([startKey]);
  const cameFrom = new globalThis.Map<string, string>();
  const points = new globalThis.Map<string, FloorRoutePoint>([[startKey, startGrid]]);
  const gScore = new globalThis.Map<string, number>([[startKey, 0]]);
  const fScore = new globalThis.Map<string, number>([[startKey, routeDistance(startGrid, endGrid)]]);

  while (open.size > 0) {
    let currentKey = "";
    let currentScore = Number.POSITIVE_INFINITY;
    open.forEach((key) => {
      const score = fScore.get(key) ?? Number.POSITIVE_INFINITY;
      if (score < currentScore) {
        currentScore = score;
        currentKey = key;
      }
    });

    if (currentKey === endKey) {
      const route = simplifyRoute(reconstructRoute(currentKey, cameFrom, points));
      return [start, ...route, end];
    }

    open.delete(currentKey);
    const current = points.get(currentKey);
    if (!current) continue;

    for (const neighbor of routeNeighbors(current, gridStep)) {
      if (blocked(neighbor)) continue;

      const neighborKey = routeKey(neighbor);
      const tentativeScore = (gScore.get(currentKey) ?? 0) + routeDistance(current, neighbor);
      if (tentativeScore >= (gScore.get(neighborKey) ?? Number.POSITIVE_INFINITY)) continue;

      cameFrom.set(neighborKey, currentKey);
      points.set(neighborKey, neighbor);
      gScore.set(neighborKey, tentativeScore);
      fScore.set(neighborKey, tentativeScore + routeDistance(neighbor, endGrid));
      open.add(neighborKey);
    }
  }

  return [];
}

function nearestOpenGridPoint(point: FloorRoutePoint, step: number, blocked: (point: FloorRoutePoint) => boolean) {
  const snapped = {
    x: clampPercent(Math.round(point.x / step) * step),
    y: clampPercent(Math.round(point.y / step) * step),
  };

  if (!blocked(snapped)) return snapped;

  for (let radius = step; radius <= 16; radius += step) {
    for (let x = snapped.x - radius; x <= snapped.x + radius; x += step) {
      for (let y = snapped.y - radius; y <= snapped.y + radius; y += step) {
        const candidate = { x: clampPercent(x), y: clampPercent(y) };
        if (!blocked(candidate)) return candidate;
      }
    }
  }

  return snapped;
}

function routeNeighbors(point: FloorRoutePoint, step: number) {
  return [
    { x: point.x + step, y: point.y },
    { x: point.x - step, y: point.y },
    { x: point.x, y: point.y + step },
    { x: point.x, y: point.y - step },
  ].filter((neighbor) => neighbor.x >= 0 && neighbor.x <= 100 && neighbor.y >= 0 && neighbor.y <= 100);
}

function reconstructRoute(currentKey: string, cameFrom: Map<string, string>, points: Map<string, FloorRoutePoint>) {
  const route: FloorRoutePoint[] = [];
  let key = currentKey;

  while (points.has(key)) {
    route.unshift(points.get(key)!);
    const previous = cameFrom.get(key);
    if (!previous) break;
    key = previous;
  }

  return route;
}

function simplifyRoute(points: FloorRoutePoint[]) {
  return points.filter((point, index) => {
    const previous = points[index - 1];
    const next = points[index + 1];
    if (!previous || !next) return true;
    return !((previous.x === point.x && point.x === next.x) || (previous.y === point.y && point.y === next.y));
  });
}

function pointsToPath(points: FloorRoutePoint[]) {
  return points.map((point, index) => `${index === 0 ? "M" : "L"} ${roundRouteValue(point.x)} ${roundRouteValue(point.y)}`).join(" ");
}

function routeKey(point: FloorRoutePoint) {
  return `${roundRouteValue(point.x)},${roundRouteValue(point.y)}`;
}

function routeDistance(a: FloorRoutePoint, b: FloorRoutePoint) {
  return Math.abs(a.x - b.x) + Math.abs(a.y - b.y);
}

function clampPercent(value: number) {
  return Math.min(100, Math.max(0, value));
}

function roundRouteValue(value: number) {
  return Math.round(value * 10) / 10;
}

function FloorPlanScreen({ event, guest, floorObjects, highlightedTableId, zoom, onBack, onZoomIn, onZoomOut, onCenter }: {
  event: PublicEvent;
  guest: Guest;
  floorObjects: FloorObject[];
  highlightedTableId: string;
  zoom: number;
  onBack: () => void;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onCenter: () => void;
}) {
  return (
    <div className="app-screen floor-plan-screen">
      <header className="floor-plan-header">
        <button className="icon-button" type="button" onClick={onBack} aria-label="Back to seat details"><ArrowLeft aria-hidden="true" /></button>
        <div>
          <p className="eyebrow">{event.venueName}</p>
          <h1>Table {guest.tableCode}: {guest.tableName}</h1>
        </div>
      </header>

      <section className="floor-plan-stage" aria-label="Venue floor plan">
        <FloorPlan floorObjects={floorObjects} highlightedTableId={highlightedTableId} zoom={zoom} />
        <div className="floor-legend" aria-label="Floor plan legend">
          <span><i className="legend-assigned" />Your table</span>
          <span><i className="legend-table" />Other tables</span>
          <span><i className="legend-venue" />Venue areas</span>
        </div>
      </section>

      <div className="floor-action-bar" aria-label="Floor plan controls">
        <button className="icon-button" type="button" onClick={onZoomOut} aria-label="Zoom out"><Minus aria-hidden="true" /></button>
        <button className="secondary-button" type="button" onClick={onCenter}><LocateFixed aria-hidden="true" />Center on My Table</button>
        <button className="icon-button" type="button" onClick={onZoomIn} aria-label="Zoom in"><Plus aria-hidden="true" /></button>
      </div>
    </div>
  );
}

function FloorPlan({ floorObjects, highlightedTableId, zoom }: { floorObjects: FloorObject[]; highlightedTableId: string; zoom: number }) {
  return (
    <div className="floor-plan-viewport">
      <div className="floor-plan" style={{ transform: `scale(${zoom})` }}>
        <div className="floor-grid" aria-hidden="true" />
        <div className="path-line" aria-hidden="true" />
        {floorObjects.map((object) => {
          const highlighted = object.id === highlightedTableId;
          return (
            <div
              className={`floor-object ${object.type} ${object.shape} ${highlighted ? "highlighted" : "dimmed"}`}
              key={object.id}
              style={{ left: `${object.x * 100}%`, top: `${object.y * 100}%`, width: `${object.width * 100}%`, height: `${object.height * 100}%` }}
              aria-label={`${object.label}${highlighted ? ", your assigned table" : ""}`}
            >
              <span>{object.label}</span>
              {highlighted ? <i aria-hidden="true" /> : null}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function NotFoundScreen({ state, eventSlug }: { state: Exclude<PublicLoadState, "loading" | "ready">; eventSlug: string }) {
  return (
    <div className="app-screen status-screen">
      <div className="status-card">
        <p className="eyebrow">{state === "notFound" ? "Event not found" : "Event unavailable"}</p>
        <h1>{state === "notFound" ? "This event page is not published." : "We could not reach the event service."}</h1>
        <p>{state === "notFound" ? `No public event is available for "${eventSlug}". Please check the QR code or event link.` : "Please try again in a moment, or ask the welcome desk for help."}</p>
      </div>
    </div>
  );
}

function AdminDashboard({ page }: { page: AdminPage }) {
  const [events, setEvents] = useState<AdminEvent[]>([]);
  const [token, setToken] = useState(() => safeGetStorage("sassoir_admin_token"));
  const [refreshToken, setRefreshToken] = useState(() => safeGetStorage("sassoir_admin_refresh_token"));
  const [user, setUser] = useState<AdminUser | null>(null);
  const [apiOnline, setApiOnline] = useState(false);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [editingEventId, setEditingEventId] = useState<string | null>(null);
  const [draft, setDraft] = useState<AdminEventDraft>(emptyEventDraft());
  const [showPasswordDialog, setShowPasswordDialog] = useState(false);

  const resetDraft = () => {
    setEditingEventId(null);
    setDraft(emptyEventDraft());
    setError("");
  };

  const endSession = useCallback((message = "") => {
    clearAdminSession();
    setToken("");
    setRefreshToken("");
    setUser(null);
    setEvents([]);
    setError(message);
    setEditingEventId(null);
    setDraft(emptyEventDraft());
  }, []);

  const refreshAdminSession = useCallback(async () => {
    if (!refreshToken) return null;

    const refreshExpiry = parseDateTime(safeGetStorage("sassoir_admin_refresh_expires_at"));
    if (refreshExpiry && refreshExpiry <= Date.now()) {
      endSession("Your admin session expired. Please sign in again.");
      return null;
    }

    const response = await fetch(apiUrl("/api/auth/refresh"), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    });
    if (!response.ok) {
      endSession("Your admin session expired. Please sign in again.");
      return null;
    }

    const payload = (await response.json()) as AuthPayload;
    saveAdminSession(payload);
    setToken(payload.token);
    setRefreshToken(payload.refreshToken);
    setUser({ email: payload.email, displayName: payload.displayName, roles: payload.roles });
    return payload.token;
  }, [endSession, refreshToken]);

  const loadEvents = useCallback(async (authToken: string) => {
    setLoading(true);
    setError("");

    try {
      let response = await fetch(apiUrl("/api/admin/events"), {
        headers: { Authorization: `Bearer ${authToken}` },
      });
      if (response.status === 401) {
        const nextToken = await refreshAdminSession();
        if (!nextToken) {
          throw new Error("Your admin session expired. Please sign in again.");
        }
        response = await fetch(apiUrl("/api/admin/events"), {
          headers: { Authorization: `Bearer ${nextToken}` },
        });
      }
      if (!response.ok) throw new Error("Could not load events.");

      const payload = (await response.json()) as AdminEvent[];
      setEvents(payload);
      setApiOnline(true);
    } catch (loadError) {
      setApiOnline(false);
      setError(loadError instanceof Error ? loadError.message : "Could not load events.");
    } finally {
      setLoading(false);
    }
  }, [refreshAdminSession]);

  useEffect(() => {
    let cancelled = false;

    async function restoreSession() {
      if (!token) return;

      try {
        const response = await fetch(apiUrl("/api/auth/me"), {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (response.status === 401) {
          const nextToken = await refreshAdminSession();
          if (!nextToken) return;
          await loadEvents(nextToken);
          return;
        }
        if (!response.ok) throw new Error("Session unavailable");
        const payload = (await response.json()) as AdminUser;
        if (cancelled) return;
        setUser(payload);
        await loadEvents(token);
      } catch {
        if (!cancelled) {
          setApiOnline(false);
          endSession("Your admin session expired. Please sign in again.");
        }
      }
    }

    void restoreSession();
    return () => {
      cancelled = true;
    };
  }, [endSession, loadEvents, refreshAdminSession, token]);

  useEffect(() => {
    if (!token) return;

    const scheduleMs = Math.max(15_000, parseDateTime(safeGetStorage("sassoir_admin_expires_at")) - Date.now() - 60_000);
    const timer = window.setTimeout(() => {
      void refreshAdminSession();
    }, scheduleMs);

    return () => window.clearTimeout(timer);
  }, [refreshAdminSession, token]);

  async function handleLogin(email: string, password: string) {
    setLoading(true);
    setError("");

    try {
      const response = await fetch(apiUrl("/api/auth/login"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });
      if (!response.ok) throw new Error("Invalid email or password.");

      const payload = (await response.json()) as AuthPayload;
      saveAdminSession(payload);
      setToken(payload.token);
      setRefreshToken(payload.refreshToken);
      setUser({ email: payload.email, displayName: payload.displayName, roles: payload.roles });
      await loadEvents(payload.token);
    } catch (loginError) {
      setError(loginError instanceof Error ? loginError.message : "Could not sign in.");
    } finally {
      setLoading(false);
    }
  }

  function handleLogout() {
    endSession();
  }

  function navigateAdmin(path: string) {
    window.history.pushState({}, "", path);
    window.dispatchEvent(new PopStateEvent("popstate"));
  }

  function startEdit(event: AdminEvent) {
    setEditingEventId(event.id);
    setDraft({
      name: event.name,
      slug: event.slug,
      eventType: event.eventType,
      subtitle: event.subtitle,
      dateLabel: event.dateLabel,
      venueName: event.venueName,
      venueAddress: event.venueAddress,
      status: eventStatusText(event.status),
      heroText: event.heroText,
      primaryColor: event.primaryColor,
      secondaryColor: event.secondaryColor,
      backgroundColor: event.backgroundColor,
      textColor: event.textColor,
      welcomeTitle: event.welcomeTitle,
      searchInputLabel: event.searchInputLabel,
      searchPlaceholder: event.searchPlaceholder,
      heroImageUrl: event.heroImageUrl ?? "",
    });
    setError("");
    navigateAdmin(`/admin/events/${event.id}`);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  async function saveEvent(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    if (!token) return;

    setSaving(true);
    setError("");

    try {
      const response = await fetch(apiUrl(editingEventId ? `/api/admin/events/${editingEventId}` : "/api/admin/events"), {
        method: editingEventId ? "PUT" : "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(draft),
      });

      if (!response.ok) {
        const detail = await readError(response);
        throw new Error(detail);
      }

      await loadEvents(token);
      resetDraft();
      navigateAdmin("/admin/events");
    } catch (saveError) {
      const localEvent: AdminEvent = {
        id: editingEventId ?? crypto.randomUUID(),
        name: draft.name,
        slug: draft.slug,
        eventType: draft.eventType,
        subtitle: draft.subtitle,
        dateLabel: draft.dateLabel,
        venueName: draft.venueName,
        venueAddress: draft.venueAddress,
        status: draft.status,
        heroText: draft.heroText,
        primaryColor: draft.primaryColor,
        secondaryColor: draft.secondaryColor,
        backgroundColor: draft.backgroundColor,
        textColor: draft.textColor,
        welcomeTitle: draft.welcomeTitle,
        searchInputLabel: draft.searchInputLabel,
        searchPlaceholder: draft.searchPlaceholder,
        heroImageUrl: draft.heroImageUrl,
        guestCount: editingEventId ? events.find((event) => event.id === editingEventId)?.guestCount ?? 0 : 0,
        assignedGuests: editingEventId ? events.find((event) => event.id === editingEventId)?.assignedGuests ?? 0 : 0,
      };
      setEvents((current) => editingEventId ? current.map((event) => event.id === editingEventId ? localEvent : event) : [localEvent, ...current]);
      setApiOnline(false);
      setError(saveError instanceof Error ? `${saveError.message} Saved locally for preview.` : "Saved locally for preview.");
      resetDraft();
      navigateAdmin("/admin/events");
    } finally {
      setSaving(false);
    }
  }

  async function uploadEventImage(file: File) {
    if (!token) throw new Error("Please sign in again before uploading an image.");

    const formData = new FormData();
    formData.append("file", file);

    const response = await fetch(apiUrl("/api/admin/uploads/event-image"), {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
      },
      body: formData,
    });

    if (!response.ok) {
      const detail = await readError(response);
      throw new Error(detail);
    }

    const payload = (await response.json()) as { url: string };
    return assetUrl(payload.url);
  }

  async function deleteEvent(eventId: string) {
    if (!token) return;
    const eventToDelete = events.find((event) => event.id === eventId);
    if (!eventToDelete) return;

    setSaving(true);
    setError("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${eventId}`), {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) throw new Error("Could not delete event.");
      await loadEvents(token);
      if (editingEventId === eventId) resetDraft();
    } catch (deleteError) {
      setEvents((current) => current.filter((event) => event.id !== eventId));
      setApiOnline(false);
      setError(deleteError instanceof Error ? `${deleteError.message} Removed locally for preview.` : "Removed locally for preview.");
      if (editingEventId === eventId) resetDraft();
    } finally {
      setSaving(false);
    }
  }

  async function setEventPublication(eventId: string, status: "Published" | "Draft") {
    if (!token) return;

    setSaving(true);
    setError("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${eventId}/${status === "Published" ? "publish" : "unpublish"}`), {
        method: "POST",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) throw new Error(status === "Published" ? "Could not publish event." : "Could not unpublish event.");
      await loadEvents(token);
    } catch (publishError) {
      setEvents((current) => current.map((event) => event.id === eventId ? { ...event, status } : event));
      setApiOnline(false);
      setError(publishError instanceof Error ? `${publishError.message} Updated locally for preview.` : "Updated locally for preview.");
    } finally {
      setSaving(false);
    }
  }

  async function changePassword(currentPassword: string, newPassword: string) {
    if (!token) return;

    setSaving(true);
    setError("");

    try {
      const response = await fetch(apiUrl("/api/auth/change-password"), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ currentPassword, newPassword }),
      });
      if (response.status === 401) {
        endSession("Your admin session expired. Please sign in again.");
        return;
      }
      if (!response.ok) throw new Error(await readError(response));
      setShowPasswordDialog(false);
      setError("Password updated.");
    } catch (passwordError) {
      setError(passwordError instanceof Error ? passwordError.message : "Could not update password.");
    } finally {
      setSaving(false);
    }
  }

  const adminPath = window.location.pathname;
  const eventRouteMatch = adminPath.match(/^\/admin\/events\/([^/]+)/);
  const routedEventId = eventRouteMatch?.[1] && eventRouteMatch[1] !== "new" ? eventRouteMatch[1] : "";

  useEffect(() => {
    if (page !== "events" || !routedEventId || editingEventId === routedEventId) return;
    const routedEvent = events.find((event) => event.id === routedEventId);
    if (!routedEvent) return;

    setEditingEventId(routedEvent.id);
    setDraft({
      name: routedEvent.name,
      slug: routedEvent.slug,
      eventType: routedEvent.eventType,
      subtitle: routedEvent.subtitle,
      dateLabel: routedEvent.dateLabel,
      venueName: routedEvent.venueName,
      venueAddress: routedEvent.venueAddress,
      status: eventStatusText(routedEvent.status),
      heroText: routedEvent.heroText,
      primaryColor: routedEvent.primaryColor,
      secondaryColor: routedEvent.secondaryColor,
      backgroundColor: routedEvent.backgroundColor,
      textColor: routedEvent.textColor,
      welcomeTitle: routedEvent.welcomeTitle,
      searchInputLabel: routedEvent.searchInputLabel,
      searchPlaceholder: routedEvent.searchPlaceholder,
      heroImageUrl: routedEvent.heroImageUrl ?? "",
    });
    setError("");
  }, [editingEventId, events, page, routedEventId]);

  if (!user) {
    return <AdminLogin onLogin={handleLogin} loading={loading} error={error} />;
  }

  const selectedEvent = events[0] ?? fallbackAdminEvents[0];

  return (
    <main className="admin-shell">
      <aside className="admin-sidebar" aria-label="Admin navigation">
        <div className="sidebar-top">
          <strong>S'assoir</strong>
          <span className="brand-sub">Event studio</span>
          <nav className="sidebar-primary">
          <button className={page === "dashboard" ? "active" : ""} type="button" onClick={() => navigateAdmin("/admin")}><LayoutDashboard aria-hidden="true" />Dashboard</button>
          <button className={page === "events" ? "active" : ""} type="button" onClick={() => navigateAdmin("/admin/events")}><CalendarDays aria-hidden="true" />Events</button>
          <button className={page === "publish" ? "active" : ""} type="button" onClick={() => navigateAdmin("/admin/publish")}><QrCode aria-hidden="true" />Publish</button>
          <button className={page === "analytics" ? "active" : ""} type="button" onClick={() => navigateAdmin("/admin/analytics")}><BarChart3 aria-hidden="true" />Analytics</button>
          <button className={page === "contact-submissions" ? "active" : ""} type="button" onClick={() => navigateAdmin("/admin/contact-submissions")}><Send aria-hidden="true" />Contact</button>
          </nav>
        </div>
        <nav className="sidebar-secondary">
          <button type="button"><ClipboardList aria-hidden="true" />Notifications</button>
          <button className={page === "settings" ? "active" : ""} type="button" onClick={() => navigateAdmin("/admin/settings")}><Settings aria-hidden="true" />Setup</button>
          <button type="button"><Users aria-hidden="true" />Profile</button>
          <button type="button" onClick={() => setShowPasswordDialog(true)}><Lock aria-hidden="true" />Change password</button>
          <button type="button" onClick={handleLogout}><LogOut aria-hidden="true" />Sign out</button>
        </nav>
        <button className="profile-chip" type="button">
          <span className="profile-avatar">{initials(user.displayName || user.email)}</span>
          <span className="profile-info">
            <span className="profile-name">{user.displayName}</span>
            <span className="profile-role">Admin</span>
          </span>
        </button>
      </aside>

      <section className="admin-main">
        <header className="admin-header">
          <div>
            <h1>{adminPageTitle(page)}</h1>
            <p>{adminPageDescription(page)}</p>
          </div>
          <div className="admin-account">
            <span className={`api-status ${apiOnline ? "online" : "offline"}`}>{apiOnline ? "Live API" : "Offline"}</span>
            <strong>{user.displayName}</strong>
          </div>
        </header>

        {page === "dashboard" ? (
          <DashboardPage events={events} onNavigate={navigateAdmin} />
        ) : null}

        {page === "events" ? (
          <EventsPage
            events={events}
            draft={draft}
            editingEventId={editingEventId}
            saving={saving}
            loading={loading}
            error={error}
            onDraftChange={setDraft}
            onSubmit={saveEvent}
            onImageUpload={uploadEventImage}
            onResetDraft={resetDraft}
            onCreate={() => {
              resetDraft();
              navigateAdmin("/admin/events/new");
              window.scrollTo({ top: 0, behavior: "smooth" });
            }}
            onBackToList={() => {
              resetDraft();
              navigateAdmin("/admin/events");
            }}
            onStartEdit={startEdit}
            onDelete={(eventId) => void deleteEvent(eventId)}
            onSetPublication={(eventId, status) => void setEventPublication(eventId, status)}
            token={token}
          />
        ) : null}

        {page === "guests" ? <GuestsPage event={selectedEvent} token={token} /> : null}
        {page === "floor-plan" ? <FloorPlanAdminPage event={selectedEvent} token={token} activeSubsection="Design" /> : null}
        {page === "publish" ? <PublishPage events={events} saving={saving} onSetPublication={(eventId, status) => void setEventPublication(eventId, status)} /> : null}
        {page === "analytics" ? <AnalyticsPage events={events} /> : null}
        {page === "contact-submissions" ? <ContactSubmissionsPage token={token} /> : null}
        {page === "settings" ? <SettingsPage /> : null}
      </section>

      {showPasswordDialog ? (
        <ChangePasswordDialog
          saving={saving}
          error={error}
          onClose={() => setShowPasswordDialog(false)}
          onSubmit={(currentPassword, newPassword) => void changePassword(currentPassword, newPassword)}
        />
      ) : null}
    </main>
  );
}

function adminPageTitle(page: AdminPage) {
  switch (page) {
    case "events":
      return "Events";
    case "guests":
      return "Guests & Seating";
    case "floor-plan":
      return "Floor Plan";
    case "publish":
      return "Publish & QR";
    case "analytics":
      return "Analytics";
    case "contact-submissions":
      return "Contact Submissions";
    case "settings":
      return "Settings";
    default:
      return "Event Operations Dashboard";
  }
}

function adminPageDescription(page: AdminPage) {
  switch (page) {
    case "events":
      return "Create, edit, publish, and archive reusable event pages.";
    case "guests":
      return "Prepare imports, review guest assignment health, and plan seating workflows.";
    case "floor-plan":
      return "Preview the venue canvas and the tools needed for the floor plan designer.";
    case "publish":
      return "Check readiness, publish public links, and prepare QR codes.";
    case "analytics":
      return "Track the guest search and seating experience for each event.";
    case "contact-submissions":
      return "Review inquiries sent from the public landing page.";
    case "settings":
      return "Manage organization defaults, privacy, and admin access.";
    default:
      return "Create reusable, branded seating experiences for many events.";
  }
}

function DashboardPage({ events, onNavigate }: { events: AdminEvent[]; onNavigate: (path: string) => void }) {
  return (
    <>
      <section className="admin-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Build order</p>
            <h2>MVP setup workflow</h2>
          </div>
          <button className="primary-button compact-button" type="button" onClick={() => onNavigate("/admin/events")}><Plus aria-hidden="true" />Create Event</button>
        </div>
        <div className="workflow-grid">
          {["Basic information", "Branding", "Guest list", "Tables", "Floor plan", "Preview", "Publish", "QR code"].map((step, index) => (
            <div className="workflow-step" key={step}>
              <span>{index + 1}</span>
              <strong>{step}</strong>
            </div>
          ))}
        </div>
      </section>

      <section className="admin-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Recent events</p>
            <h2>Continue setup</h2>
          </div>
        </div>
        <EventList events={events} compact />
      </section>

      <section className="admin-panel">
        <div className="security-note">
          <ShieldCheck aria-hidden="true" />
          <p>Public search results only expose safe labels. Full assignments stay hidden until a guest selects their matching record.</p>
        </div>
      </section>
    </>
  );
}

function EventsPage({ events, draft, editingEventId, saving, loading, error, onDraftChange, onSubmit, onImageUpload, onResetDraft, onCreate, onBackToList, onStartEdit, onDelete, onSetPublication, token }: {
  events: AdminEvent[];
  draft: AdminEventDraft;
  editingEventId: string | null;
  saving: boolean;
  loading: boolean;
  error: string;
  onDraftChange: (draft: AdminEventDraft) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onImageUpload: (file: File) => Promise<string>;
  onResetDraft: () => void;
  onCreate: () => void;
  onBackToList: () => void;
  onStartEdit: (event: AdminEvent) => void;
  onDelete: (eventId: string) => void;
  onSetPublication: (eventId: string, status: "Published" | "Draft") => void;
  token: string;
}) {
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("All");
  const adminPath = window.location.pathname;
  const isFormRoute = /^\/admin\/events\/(new|[^/]+)/.test(adminPath);
  const totalGuests = events.reduce((sum, event) => sum + event.guestCount, 0);
  const assignedGuests = events.reduce((sum, event) => sum + event.assignedGuests, 0);
  const publishedEvents = events.filter((event) => eventStatusText(event.status).toLowerCase() === "published").length;
  const draftEvents = events.filter((event) => eventStatusText(event.status).toLowerCase() === "draft").length;
  const normalizedQuery = normalizeSearch(query);
  const visibleEvents = events.filter((event) => {
    const matchesQuery = !normalizedQuery || normalizeSearch(`${event.name} ${event.slug} ${event.dateLabel}`).includes(normalizedQuery);
    const matchesStatus = statusFilter === "All" || eventStatusText(event.status) === statusFilter;
    return matchesQuery && matchesStatus;
  });
  const editingEvent = editingEventId ? events.find((event) => event.id === editingEventId) : undefined;

  if (isFormRoute) {
    return (
      <section className="event-workspace">
        <div className="event-form-title">
          <button className="secondary-button compact-button" type="button" onClick={onBackToList}><ArrowLeft aria-hidden="true" />Back</button>
          <div>
            <h2>{editingEventId ? "Edit event" : "Add a new event"}</h2>
            <p>{editingEventId ? "Update the event details and setup sections." : "Fill in the details to create a new event."}</p>
          </div>
          <div className="event-title-actions">
            <button className="secondary-button compact-button" type="button" onClick={onBackToList}>Cancel</button>
            <button className="primary-button compact-button" type="submit" form="event-editor-form">{saving ? "Saving..." : "Save event"}</button>
          </div>
        </div>
        <EventEditorForm
          draft={draft}
          editorEvent={editingEvent}
          token={token}
          onDraftChange={onDraftChange}
          onSubmit={onSubmit}
          onImageUpload={onImageUpload}
          saving={saving}
          editing={Boolean(editingEventId)}
        />
        {error ? <p className="form-error" role="alert">{error}</p> : null}
      </section>
    );
  }

  return (
    <>
      <section className="metric-grid" aria-label="Event metrics">
        <MetricCard icon={<CalendarDays aria-hidden="true" />} label="Total events" value={events.length} />
        <MetricCard icon={<Eye aria-hidden="true" />} label="Published" value={publishedEvents} />
        <MetricCard icon={<ClipboardList aria-hidden="true" />} label="Drafts" value={draftEvents} />
        <MetricCard icon={<Users aria-hidden="true" />} label="Assigned guests" value={`${assignedGuests}/${totalGuests}`} />
      </section>

      <section className="event-list-page">
        <div className="list-toolbar">
          <label className="admin-search">
            <Search aria-hidden="true" />
            <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search events" aria-label="Search events" />
          </label>
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)} aria-label="Filter events by status">
            <option>All</option>
            <option>Draft</option>
            <option>Published</option>
            <option>Archived</option>
          </select>
          <button className="primary-button create-object-button" type="button" onClick={() => {
            onResetDraft();
            onCreate();
          }}><Plus aria-hidden="true" />Create new event</button>
        </div>
        {loading ? <span className="api-status">Loading...</span> : null}
        {error ? <p className="form-error" role="alert">{error}</p> : null}
        <EventList events={visibleEvents} onStartEdit={onStartEdit} onDelete={onDelete} onSetPublication={onSetPublication} />
      </section>
    </>
  );
}

function EventList({ events, compact = false, onStartEdit, onDelete, onSetPublication }: {
  events: AdminEvent[];
  compact?: boolean;
  onStartEdit?: (event: AdminEvent) => void;
  onDelete?: (eventId: string) => void;
  onSetPublication?: (eventId: string, status: "Published" | "Draft") => void;
}) {
  if (compact) {
    return (
      <div className="event-list">
        {events.map((event) => (
          <article className="event-row" key={event.id}>
            <span className="event-status">{eventStatusText(event.status)}</span>
            <div>
              <h3>{event.name}</h3>
              <p>/e/{event.slug} - {event.dateLabel || "No date set"} - {event.venueName || "No venue set"}</p>
            </div>
            <strong>{event.assignedGuests}/{event.guestCount} seated</strong>
          </article>
        ))}
        {events.length === 0 ? <p className="empty-state">No events yet. Create the first draft from the Events page.</p> : null}
      </div>
    );
  }

  return (
    <div className="admin-table-wrap">
      <table className="admin-table">
        <thead>
          <tr>
            <th>Event name</th>
            <th>Event status</th>
            <th>Number of guests</th>
            <th>Floor plan added</th>
            <th>Active</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {events.map((event) => {
            const published = eventStatusText(event.status).toLowerCase() === "published";
            return (
              <tr key={event.id}>
                <td data-label="Event name">
                  <div className="ev-cell">
                    <span className="ev-thumb" aria-hidden="true" />
                    <span>
                      <strong>{event.name || "Untitled event"}</strong>
                      <span>/e/{event.slug || "event-slug"}</span>
                    </span>
                  </div>
                </td>
                <td data-label="Event status"><span className={`event-status ${published ? "published" : ""}`}>{eventStatusText(event.status)}</span></td>
                <td data-label="Number of guests"><span className="mono-value">{event.assignedGuests} / {event.guestCount}</span></td>
                <td data-label="Floor plan added"><span className="event-status">{event.assignedGuests > 0 ? "Added" : "Not added"}</span></td>
                <td className="active-cell" data-label="Active">
                  <button
                    className={`toggle-indicator ${published ? "on" : ""}`}
                    type="button"
                    role="switch"
                    aria-checked={published}
                    aria-label={`${published ? "Deactivate" : "Activate"} ${event.name}`}
                    onClick={() => onSetPublication?.(event.id, published ? "Draft" : "Published")}
                  >
                    <span aria-hidden="true" />
                  </button>
                </td>
                <td className="actions-cell" data-label="Actions">
                  <div className="event-actions">
                    <a className="icon-button" href={`/e/${event.slug}`} aria-label={`Preview ${event.name}`}><Eye aria-hidden="true" /></a>
                    <button className="icon-button" type="button" onClick={() => onStartEdit?.(event)} aria-label={`Edit ${event.name}`}><Pencil aria-hidden="true" /></button>
                    <button className="icon-button danger-button" type="button" onClick={() => onDelete?.(event.id)} aria-label={`Delete ${event.name}`}><Trash2 aria-hidden="true" /></button>
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {events.length > 0 ? (
        <div className="pagination">
          <span>Showing 1 to {events.length} of {events.length} events</span>
          <div className="page-nums" aria-label="Pagination">
            <button type="button">‹</button>
            <button className="active" type="button">1</button>
            <button type="button">2</button>
            <button type="button">3</button>
            <button type="button">›</button>
          </div>
        </div>
      ) : null}
      {events.length === 0 ? <p className="empty-state">No events yet. Create the first draft from the Events page.</p> : null}
    </div>
  );
}

function GuestsPage({ event, token }: { event: AdminEvent; token: string }) {
  const [guests, setGuests] = useState<AdminGuest[]>([]);
  const [tables, setTables] = useState<AdminTable[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("Active");
  const [tableFilter, setTableFilter] = useState("All");
  const [formMode, setFormMode] = useState<"closed" | "create" | "edit" | "details">("closed");
  const [selectedGuestId, setSelectedGuestId] = useState("");
  const [draft, setDraft] = useState<AdminGuestDraft>(emptyGuestDraft());
  const [importPreview, setImportPreview] = useState<ImportPreview | null>(null);
  const [importRows, setImportRows] = useState<ImportPreviewRow[]>([]);
  const [showImportDialog, setShowImportDialog] = useState(false);
  const [selectedGuestIds, setSelectedGuestIds] = useState<string[]>([]);
  const [bulkTableId, setBulkTableId] = useState("");
  const [showBulkAssignDialog, setShowBulkAssignDialog] = useState(false);
  const [guestPage, setGuestPage] = useState(1);
  const [totalGuestCount, setTotalGuestCount] = useState(0);
  const debouncedQuery = useDebouncedValue(query, 300);
  const guestsPerPage = 20;

  const loadGuests = useCallback(async (options?: { force?: boolean }) => {
    if (!token) return;

    setLoading(true);
    setError("");

    try {
      const [guestPayload, tablePayload] = await Promise.all([
        getAdminGuestPage(event.id, token, {
          page: guestPage,
          pageSize: guestsPerPage,
          search: debouncedQuery.trim(),
          status: statusFilter,
          tableId: tableFilter,
        }, options),
        getAdminTables(event.id, token, options),
      ]);
      setGuests(guestPayload.items);
      setTotalGuestCount(guestPayload.totalCount);
      setTables(tablePayload);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Could not load guests.");
    } finally {
      setLoading(false);
    }
  }, [debouncedQuery, event.id, guestPage, statusFilter, tableFilter, token]);

  useEffect(() => {
    void loadGuests();
  }, [loadGuests]);

  function startCreateGuest() {
    setSelectedGuestId("");
    setDraft(emptyGuestDraft());
    setFormMode("create");
    setMessage("");
    setError("");
  }

  function selectGuest(guest: AdminGuest, mode: "edit" | "details") {
    setSelectedGuestId(guest.id);
    setDraft(guestToDraft(guest));
    setFormMode(mode);
    setMessage("");
    setError("");
  }

  function updateGuestName(field: "firstName" | "lastName", value: string) {
    setDraft((current) => {
      const previousGeneratedName = buildGuestDisplayName(current.firstName, current.lastName);
      const nextDraft = { ...current, [field]: value };
      const shouldSyncDisplayName = current.displayName.trim() === "" || current.displayName === previousGeneratedName;

      return {
        ...nextDraft,
        displayName: shouldSyncDisplayName ? buildGuestDisplayName(nextDraft.firstName, nextDraft.lastName) : current.displayName,
      };
    });
  }

  function downloadGuestTemplate() {
    downloadTextFile(
      "sassoir-guest-import-template.csv",
      "First Name,Last Name,Display Name,Person Count,Notes\nAntonella,Hitti,,2,Vegetarian meal\nKarim,Saab,Karim Saab,1,Needs aisle access\n",
      "text/csv",
    );
  }

  async function saveGuest(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    if (!token) return;

    setSaving(true);
    setError("");
    setMessage("");

    try {
      const editing = formMode === "edit" || formMode === "details";
      const response = await fetch(apiUrl(editing ? `/api/admin/events/${event.id}/guests/${selectedGuestId}` : `/api/admin/events/${event.id}/guests`), {
        method: editing ? "PUT" : "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          ...draft,
          tableId: draft.tableId || null,
        }),
      });

      if (!response.ok) throw new Error(await readError(response));

      setDraft(emptyGuestDraft());
      setSelectedGuestId("");
      setFormMode("closed");
      setMessage(editing ? "Guest updated." : "Guest created.");
      clearEventAdminCaches(event.id);
      await loadGuests({ force: true });
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Could not save guest.");
    } finally {
      setSaving(false);
    }
  }

  async function archiveGuest(guest: AdminGuest) {
    if (!token) return;

    setSaving(true);
    setError("");
    setMessage("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/guests/${guest.id}/archive`), {
        method: "POST",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) throw new Error(await readError(response));
      setMessage(`${guest.displayName} archived.`);
      clearEventAdminCaches(event.id);
      await loadGuests({ force: true });
    } catch (archiveError) {
      setError(archiveError instanceof Error ? archiveError.message : "Could not archive guest.");
    } finally {
      setSaving(false);
    }
  }

  async function deleteGuest(guest: AdminGuest) {
    if (!token) return;

    setSaving(true);
    setError("");
    setMessage("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/guests/${guest.id}`), {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) throw new Error(await readError(response));
      setMessage(`${guest.displayName} deleted.`);
      if (selectedGuestId === guest.id) {
        setSelectedGuestId("");
        setFormMode("closed");
      }
      clearEventAdminCaches(event.id);
      await loadGuests({ force: true });
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : "Could not delete guest.");
    } finally {
      setSaving(false);
    }
  }

  async function previewImport(file: File | undefined) {
    if (!token || !file) return;

    setSaving(true);
    setError("");
    setMessage("");
    setImportPreview(null);
    setImportRows([]);

    try {
      if (!file.name.toLowerCase().endsWith(".csv")) {
        throw new Error("Import Excel sheets by exporting them as CSV first.");
      }

      const rows = parseGuestCsv(await file.text());
      if (rows.length === 0) throw new Error("No guest rows were found in the import file.");

      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/guests/import/preview`), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ guests: rows }),
      });
      if (!response.ok) throw new Error(await readError(response));

      const payload = (await response.json()) as ImportPreview;
      setImportPreview(payload);
      setImportRows(payload.rows);
      setMessage(`${payload.rows.length} rows reviewed. Fix errors before saving.`);
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : "Could not preview import.");
    } finally {
      setSaving(false);
    }
  }

  async function commitImport() {
    if (!token || !importPreview) return;

    const validRows = importPreview.rows.filter((row) => row.errors.length === 0 && !row.isDuplicate);
    if (validRows.length === 0) {
      setError("No valid import rows to save.");
      return;
    }

    setSaving(true);
    setError("");
    setMessage("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/guests/import/commit`), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ guests: validRows }),
      });
      if (!response.ok) throw new Error(await readError(response));

      setImportPreview(null);
      setImportRows([]);
      setMessage(`${validRows.length} guests imported.`);
      clearEventAdminCaches(event.id);
      await loadGuests({ force: true });
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : "Could not import guests.");
    } finally {
      setSaving(false);
    }
  }

  async function exportGuests() {
    if (!token) return;

    setSaving(true);
    setError("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/guests/export`), {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) throw new Error(await readError(response));

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = `${slugify(event.name || "guests")}-guests.csv`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (exportError) {
      setError(exportError instanceof Error ? exportError.message : "Could not export guests.");
    } finally {
      setSaving(false);
    }
  }

  async function bulkAssignGuests() {
    if (!token || selectedGuestIds.length === 0) return;

    setSaving(true);
    setError("");
    setMessage("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/guests/bulk-assign-table`), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ guestIds: selectedGuestIds, tableId: bulkTableId || null }),
      });
      if (!response.ok) throw new Error(await readError(response));
      setMessage(`${selectedGuestIds.length} guests assigned.`);
      setSelectedGuestIds([]);
      setBulkTableId("");
      setShowBulkAssignDialog(false);
      clearEventAdminCaches(event.id);
      await loadGuests({ force: true });
    } catch (bulkError) {
      setError(bulkError instanceof Error ? bulkError.message : "Could not bulk assign guests.");
    } finally {
      setSaving(false);
    }
  }

  const activeGuests = guests.filter((guest) => guest.status === "Active");
  const seatingGuests = guests.filter((guest) => guest.status === "Active" || guest.status === "CheckedIn");
  const totalGuestPages = Math.max(1, Math.ceil(totalGuestCount / guestsPerPage));
  const currentGuestPage = Math.min(guestPage, totalGuestPages);
  const pagedGuests = guests;
  const assignedGuests = seatingGuests.filter((guest) => guest.tableId).length;
  const unassignedGuests = seatingGuests.length - assignedGuests;
  const activePeople = sumGuestPeople(activeGuests);
  const assignedPeople = sumGuestPeople(seatingGuests.filter((guest) => guest.tableId));
  const unassignedPeople = sumGuestPeople(seatingGuests.filter((guest) => !guest.tableId));
  const duplicateGuests = guests.filter((guest) => guest.isDuplicate).length;
  const selectedGuest = guests.find((guest) => guest.id === selectedGuestId);
  const formTitle = formMode === "create" ? "Create new guest" : formMode === "edit" ? "Edit guest" : "Guest details";
  const canSaveImport = importPreview ? importPreview.rows.some((row) => row.errors.length === 0 && !row.isDuplicate) : false;
  const pageGuestIds = pagedGuests.map((guest) => guest.id);
  const pageSelected = pageGuestIds.length > 0 && pageGuestIds.every((id) => selectedGuestIds.includes(id));
  const paginationStart = Math.max(1, currentGuestPage - 2);
  const paginationEnd = Math.min(totalGuestPages, currentGuestPage + 2);
  const paginationPages = Array.from({ length: paginationEnd - paginationStart + 1 }, (_, index) => paginationStart + index);

  return (
    <section className="guest-manager">
      <div className="list-toolbar guest-toolbar">
        <label className="admin-search">
          <Search aria-hidden="true" />
          <input value={query} onChange={(formEvent) => {
            setQuery(formEvent.target.value);
            setGuestPage(1);
          }} placeholder="Search guests" aria-label="Search guests" />
        </label>
        <select value={statusFilter} onChange={(formEvent) => {
          setStatusFilter(formEvent.target.value);
          setGuestPage(1);
        }} aria-label="Filter guests by status">
          <option>Active</option>
          <option>All</option>
          <option>Unassigned</option>
          <option>Cancelled</option>
          <option>CheckedIn</option>
          <option>Archived</option>
        </select>
        <select value={tableFilter} onChange={(formEvent) => {
          setTableFilter(formEvent.target.value);
          setGuestPage(1);
        }} aria-label="Filter guests by table">
          <option value="All">All tables</option>
          <option value="Unassigned">Unassigned</option>
          {tables.map((table) => <option key={table.id} value={table.id}>Table {table.number}</option>)}
        </select>
        <button className="secondary-button compact-button" type="button" onClick={() => void exportGuests()} disabled={saving || guests.length === 0}><Download aria-hidden="true" />Export</button>
        <button className="secondary-button compact-button" type="button" onClick={() => setShowImportDialog(true)} disabled={saving}>
          <Upload aria-hidden="true" />
          Import
        </button>
        <button className="secondary-button compact-button" type="button" onClick={() => setShowBulkAssignDialog(true)} disabled={saving || selectedGuestIds.length === 0}><Users aria-hidden="true" />Assign selected</button>
        <button className="primary-button create-object-button" type="button" onClick={startCreateGuest}><Plus aria-hidden="true" />Create new guest</button>
      </div>

      {showImportDialog ? (
        <div className="modal-backdrop" role="presentation">
          <section className="modal-panel import-dialog" role="dialog" aria-modal="true" aria-label="Import guests">
            <div className="panel-heading">
              <div>
                <p className="eyebrow">Guest import</p>
                <h2>Download the template first</h2>
              </div>
              <button className="icon-button" type="button" onClick={() => setShowImportDialog(false)} aria-label="Close import dialog"><X aria-hidden="true" /></button>
            </div>
            <p className="admin-muted">Use the CSV template for first name, last name, optional display name, and notes. If display name is blank, Sassoir fills it from first and last name.</p>
            <div className="template-actions">
              <button className="secondary-button compact-button" type="button" onClick={downloadGuestTemplate}><Download aria-hidden="true" />Download template</button>
              <label className="primary-button compact-button import-button">
                <Upload aria-hidden="true" />
                Upload completed CSV
                <input type="file" accept=".csv,text/csv,application/vnd.ms-excel" onChange={(formEvent) => {
                  void previewImport(formEvent.target.files?.[0]);
                  formEvent.target.value = "";
                  setShowImportDialog(false);
                }} />
              </label>
            </div>
          </section>
        </div>
      ) : null}

      {showBulkAssignDialog ? (
        <div className="modal-backdrop" role="presentation">
          <section className="modal-panel" role="dialog" aria-modal="true" aria-label="Bulk assign guests">
            <div className="panel-heading">
              <div>
                <p className="eyebrow">Bulk assign</p>
                <h2>Assign {selectedGuestIds.length} selected guests</h2>
              </div>
              <button className="icon-button" type="button" onClick={() => setShowBulkAssignDialog(false)} aria-label="Close bulk assign dialog"><X aria-hidden="true" /></button>
            </div>
            <label className="modal-field">
              Table
              <select value={bulkTableId} onChange={(formEvent) => setBulkTableId(formEvent.target.value)}>
                <option value="">System: unassigned</option>
                {tables.map((table) => <option key={table.id} value={table.id}>Table {table.number} - {table.name}</option>)}
              </select>
            </label>
            <div className="form-actions">
              <button className="secondary-button compact-button" type="button" onClick={() => setShowBulkAssignDialog(false)}>Cancel</button>
              <button className="primary-button compact-button" type="button" onClick={() => void bulkAssignGuests()} disabled={saving}>{saving ? "Assigning..." : "Assign"}</button>
            </div>
          </section>
        </div>
      ) : null}

      {formMode !== "closed" ? (
        <section className="guest-form-panel" aria-label={formTitle}>
          <div className="panel-heading">
            <div>
              <p className="eyebrow">{formMode === "create" ? "Manual entry" : selectedGuest?.status ?? "Guest record"}</p>
              <h2>{formTitle}</h2>
            </div>
            <button className="icon-button" type="button" onClick={() => setFormMode("closed")} aria-label="Close guest form"><X aria-hidden="true" /></button>
          </div>
          <form className="event-form" onSubmit={saveGuest}>
            <div className="form-field-grid guest-form-grid">
              <label>
                First Name
                <input value={draft.firstName} onChange={(formEvent) => updateGuestName("firstName", formEvent.target.value)} placeholder="Antonella" />
              </label>
              <label>
                Last Name
                <input value={draft.lastName} onChange={(formEvent) => updateGuestName("lastName", formEvent.target.value)} placeholder="Hitti" />
              </label>
              <label>
                Display Name
                <input value={draft.displayName} onChange={(formEvent) => setDraft((current) => ({ ...current, displayName: formEvent.target.value }))} placeholder={buildGuestDisplayName(draft.firstName, draft.lastName) || "Antonella Hitti"} />
              </label>
              <label>
                Number of persons
                <input type="number" min="1" value={draft.personCount} onChange={(formEvent) => setDraft((current) => ({ ...current, personCount: normalizePersonCount(Number(formEvent.target.value)) }))} />
              </label>
              <label>
                Status
                <select value={draft.status} onChange={(formEvent) => setDraft((current) => ({ ...current, status: formEvent.target.value as AdminGuest["status"], tableId: formEvent.target.value === "Archived" ? "" : current.tableId }))}>
                  <option>Active</option>
                  <option>Cancelled</option>
                  <option>CheckedIn</option>
                  <option>Archived</option>
                </select>
              </label>
              <label>
                Assigned Table
                <select value={draft.tableId} onChange={(formEvent) => setDraft((current) => ({ ...current, tableId: formEvent.target.value }))} disabled={draft.status === "Archived"}>
                  <option value="">System: unassigned</option>
                  {tables.map((table) => <option key={table.id} value={table.id}>Table {table.number} - {table.name}</option>)}
                </select>
              </label>
              <label className="span-3">
                Notes
                <textarea value={draft.notes} onChange={(formEvent) => setDraft((current) => ({ ...current, notes: formEvent.target.value }))} rows={4} placeholder="Dietary notes, family group, special handling..." />
              </label>
            </div>
            <div className="form-actions">
              <button className="secondary-button compact-button" type="button" onClick={() => setFormMode("closed")}>Cancel</button>
              <button className="primary-button compact-button" type="submit" disabled={saving}>{saving ? "Saving..." : formMode === "create" ? "Create Guest" : "Update Guest"}</button>
            </div>
          </form>
        </section>
      ) : null}

      <section className="guest-list-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Guest list</p>
            <h2>{event.name}</h2>
          </div>
          <div className="guest-summary" aria-label="Guest summary">
            <span>{activeGuests.length} active</span>
            <span>{assignedGuests} assigned</span>
            <span>{unassignedGuests} unassigned</span>
            <span>{activePeople} active people</span>
            <span>{assignedPeople} seated people</span>
            <span>{unassignedPeople} unseated people</span>
            {duplicateGuests ? <span>{duplicateGuests} duplicate flags</span> : null}
          </div>
        </div>

        {error ? <p className="form-error" role="alert">{error}</p> : null}
        {message ? <p className="designer-warning" role="status">{message}</p> : null}

        {importPreview ? (
          <section className="import-preview" aria-label="Import preview">
            <div className="panel-heading">
              <div>
                <p className="eyebrow">Import review</p>
                <h3>{importPreview.rows.length} rows checked</h3>
              </div>
              <div className="event-actions">
                <button className="secondary-button compact-button" type="button" onClick={() => {
                  setImportPreview(null);
                  setImportRows([]);
                }}><X aria-hidden="true" />Cancel</button>
                <button className="primary-button compact-button" type="button" onClick={() => void commitImport()} disabled={!canSaveImport || saving}><Check aria-hidden="true" />Save valid rows</button>
              </div>
            </div>
            <div className="admin-table-wrap import-table-wrap">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Guest</th>
                    <th>People</th>
                    <th>Notes</th>
                    <th>Review</th>
                  </tr>
                </thead>
                <tbody>
                  {importRows.map((row) => (
                    <tr key={`${row.rowNumber}-${row.displayName}`}>
                      <td data-label="Row">{row.rowNumber}</td>
                      <td data-label="Guest"><strong>{row.displayName || "Missing name"}</strong><span>{[row.firstName, row.lastName].filter(Boolean).join(" ") || "No legal name set"}</span></td>
                      <td data-label="People">{row.personCount}</td>
                      <td data-label="Notes">{row.notes || "-"}</td>
                      <td data-label="Review">{row.errors.length ? <span className="guest-flag error"><FileWarning aria-hidden="true" />{row.errors.join(" ")}</span> : <span className="guest-flag ok"><Check aria-hidden="true" />Ready</span>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        ) : null}

        <div className="admin-table-wrap">
          <table className="admin-table guest-table">
            <thead>
              <tr>
                <th>
                  <input
                    type="checkbox"
                    checked={pageSelected}
                    onChange={(formEvent) => {
                      setSelectedGuestIds((current) => {
                        const withoutPage = current.filter((id) => !pageGuestIds.includes(id));
                        return formEvent.target.checked ? [...withoutPage, ...pageGuestIds] : withoutPage;
                      });
                    }}
                    aria-label="Select guests on this page"
                  />
                </th>
                <th>Guest</th>
                <th>People</th>
                <th>Status</th>
                <th>Assigned Table</th>
                <th>Notes</th>
                <th>Flags</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {pagedGuests.map((guest) => {
                const table = tables.find((item) => item.id === guest.tableId);
                return (
                  <tr key={guest.id}>
                    <td data-label="Select">
                      <input
                        type="checkbox"
                        checked={selectedGuestIds.includes(guest.id)}
                        onChange={(formEvent) => {
                          setSelectedGuestIds((current) => formEvent.target.checked ? [...current, guest.id] : current.filter((id) => id !== guest.id));
                        }}
                        aria-label={`Select ${guest.displayName}`}
                      />
                    </td>
                    <td data-label="Guest">
                      <button className="guest-name-button" type="button" onClick={() => selectGuest(guest, "details")}>
                        <strong>{guest.displayName}</strong>
                        <span>{[guest.firstName, guest.lastName].filter(Boolean).join(" ") || "No legal name set"}</span>
                      </button>
                    </td>
                    <td data-label="People">{normalizePersonCount(guest.personCount)}</td>
                    <td data-label="Status"><span className={`event-status ${guest.status === "Active" ? "published" : ""}`}>{guest.status}</span></td>
                    <td data-label="Assigned Table">{guest.tableCode ? `Table ${guest.tableCode}` : "System: unassigned"}<span>{guest.tableName || (table ? table.name : "")}</span></td>
                    <td data-label="Notes">{guest.notes || "-"}</td>
                    <td data-label="Flags">{guest.isDuplicate ? <span className="guest-flag warning"><FileWarning aria-hidden="true" />Duplicate</span> : <span className="guest-flag ok"><Check aria-hidden="true" />Clear</span>}</td>
                    <td className="actions-cell" data-label="Actions">
                      <div className="event-actions">
                        <button className="icon-button" type="button" onClick={() => selectGuest(guest, "details")} aria-label={`View ${guest.displayName}`}><Eye aria-hidden="true" /></button>
                        <button className="icon-button" type="button" onClick={() => selectGuest(guest, "edit")} aria-label={`Edit ${guest.displayName}`}><Pencil aria-hidden="true" /></button>
                        <button className="icon-button" type="button" onClick={() => void archiveGuest(guest)} disabled={saving || guest.status === "Archived"} aria-label={`Archive ${guest.displayName}`}><Archive aria-hidden="true" /></button>
                        <button className="icon-button danger-button" type="button" onClick={() => void deleteGuest(guest)} disabled={saving} aria-label={`Delete ${guest.displayName}`}><Trash2 aria-hidden="true" /></button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {!loading && totalGuestCount === 0 ? <p className="empty-state">No guests match this view. Create a guest or adjust the filters.</p> : null}
          {totalGuestCount > 0 ? (
            <div className="pagination">
              <span>Showing {(currentGuestPage - 1) * guestsPerPage + 1}-{Math.min(currentGuestPage * guestsPerPage, totalGuestCount)} of {totalGuestCount}</span>
              <div className="page-nums">
                <button type="button" onClick={() => setGuestPage(Math.max(1, currentGuestPage - 1))} disabled={currentGuestPage === 1}>Prev</button>
                {paginationStart > 1 ? <button type="button" onClick={() => setGuestPage(1)}>1</button> : null}
                {paginationStart > 2 ? <span>...</span> : null}
                {paginationPages.map((pageNumber) => (
                  <button className={pageNumber === currentGuestPage ? "active" : ""} key={pageNumber} type="button" onClick={() => setGuestPage(pageNumber)}>
                    {pageNumber}
                  </button>
                ))}
                {paginationEnd < totalGuestPages - 1 ? <span>...</span> : null}
                {paginationEnd < totalGuestPages ? <button type="button" onClick={() => setGuestPage(totalGuestPages)}>{totalGuestPages}</button> : null}
                <button type="button" onClick={() => setGuestPage(Math.min(totalGuestPages, currentGuestPage + 1))} disabled={currentGuestPage === totalGuestPages}>Next</button>
              </div>
            </div>
          ) : null}
        </div>
        {loading ? <p className="admin-muted">Loading guests...</p> : null}
      </section>

    </section>
  );
}

function emptyGuestDraft(): AdminGuestDraft {
  return {
    firstName: "",
    lastName: "",
    displayName: "",
    notes: "",
    personCount: 1,
    tableId: "",
    status: "Active",
  };
}

function guestToDraft(guest: AdminGuest): AdminGuestDraft {
  return {
    firstName: guest.firstName ?? "",
    lastName: guest.lastName ?? "",
    displayName: guest.displayName ?? "",
    notes: guest.notes ?? "",
    personCount: normalizePersonCount(guest.personCount),
    tableId: guest.tableId ?? "",
    status: guest.status ?? "Active",
  };
}

function normalizePersonCount(value: number | null | undefined) {
  return Math.max(1, Math.floor(Number.isFinite(value ?? NaN) ? Number(value) : 1));
}

function sumGuestPeople(guests: AdminGuest[]) {
  return guests.reduce((total, guest) => total + normalizePersonCount(guest.personCount), 0);
}

function parseGuestCsv(text: string) {
  const rows = parseCsv(text).filter((row) => row.some((cell) => cell.trim().length > 0));
  if (rows.length === 0) return [];

  const headers = rows[0].map((header) => normalizeCsvHeader(header));
  const dataRows = rows.slice(1);

  return dataRows.map((row, index) => {
    const value = (...names: string[]) => {
      const headerIndex = headers.findIndex((header) => names.includes(header));
      return headerIndex >= 0 ? row[headerIndex]?.trim() ?? "" : "";
    };

    return {
      rowNumber: index + 2,
      firstName: value("firstname", "first", "first_name"),
      lastName: value("lastname", "last", "last_name", "surname"),
      displayName: value("displayname", "display", "name", "fullname"),
      personCount: normalizePersonCount(Number(value("personcount", "people", "persons", "partysize", "party", "numberofpersons", "numberofperson"))),
      notes: value("notes", "note", "comment", "comments"),
    };
  });
}

function normalizeCsvHeader(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9 ]+/g, "").replace(/\s+/g, " ").trim().replace(/\s/g, "");
}

function parseCsv(text: string) {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = "";
  let quoted = false;

  for (let index = 0; index < text.length; index += 1) {
    const character = text[index];
    const next = text[index + 1];

    if (character === "\"" && quoted && next === "\"") {
      cell += "\"";
      index += 1;
      continue;
    }

    if (character === "\"") {
      quoted = !quoted;
      continue;
    }

    if (character === "," && !quoted) {
      row.push(cell);
      cell = "";
      continue;
    }

    if ((character === "\n" || character === "\r") && !quoted) {
      if (character === "\r" && next === "\n") index += 1;
      row.push(cell);
      rows.push(row);
      row = [];
      cell = "";
      continue;
    }

    cell += character;
  }

  row.push(cell);
  rows.push(row);
  return rows;
}

function csvCell(value: string) {
  return `"${value.replace(/"/g, "\"\"")}"`;
}

function emptyTableDraft(): AdminTableDraft {
  return {
    name: "",
    number: "",
    maximumCapacity: 10,
    shape: "round",
    notes: "",
  };
}

function parseTableCsv(text: string): AdminTableDraft[] {
  const rows = parseCsv(text).filter((row) => row.some((cell) => cell.trim().length > 0));
  if (rows.length === 0) return [];

  const headers = rows[0].map((header) => normalizeCsvHeader(header));
  return rows.slice(1).map((row) => {
    const value = (...names: string[]) => {
      const headerIndex = headers.findIndex((header) => names.includes(header));
      return headerIndex >= 0 ? row[headerIndex]?.trim() ?? "" : "";
    };

    const shape = value("shape").toLowerCase();
    return {
      name: value("name", "tablename", "table"),
      number: value("number", "code", "tablenumber"),
      maximumCapacity: Number(value("maximumcapacity", "capacity", "maxcapacity")) || 1,
      shape: tableShapeOptions.some((option) => option.value === shape) ? shape as AdminTable["shape"] : "round",
      notes: value("notes", "note", "comments"),
    };
  }).filter((row) => row.name && row.number);
}

function compareTableNumbers(left: string, right: string) {
  const leftNumber = Number(left);
  const rightNumber = Number(right);
  if (!Number.isNaN(leftNumber) && !Number.isNaN(rightNumber) && leftNumber !== rightNumber) {
    return leftNumber - rightNumber;
  }

  return left.localeCompare(right, undefined, { numeric: true, sensitivity: "base" });
}

function drawFloorObject(context: CanvasRenderingContext2D, object: FloorObject, canvasWidth: number, canvasHeight: number) {
  const x = object.x * canvasWidth;
  const y = object.y * canvasHeight;
  const width = object.width * canvasWidth;
  const height = object.height * canvasHeight;
  const label = object.type === "table"
    ? `Table ${object.tableCode ?? object.label}${object.tableName ? ` - ${object.tableName}` : ""}`
    : object.label;

  context.save();
  context.fillStyle = object.type === "table" ? "#17171a" : object.type === "dance" ? "#f7f7f7" : "#f5f3ee";
  context.strokeStyle = "#17171a";
  context.lineWidth = 3;

  context.beginPath();
  if (object.shape === "round") {
    context.ellipse(x + width / 2, y + height / 2, width / 2, height / 2, 0, 0, Math.PI * 2);
  } else if (object.shape === "tear") {
    context.moveTo(x + width * 0.5, y);
    context.bezierCurveTo(x + width, y + height * 0.08, x + width * 0.95, y + height * 0.72, x + width * 0.5, y + height);
    context.bezierCurveTo(x + width * 0.05, y + height * 0.72, x, y + height * 0.08, x + width * 0.5, y);
  } else {
    context.roundRect(x, y, width, height, object.shape === "rectangle" || object.shape === "rect" ? 10 : 16);
  }
  context.fill();
  context.stroke();

  context.fillStyle = object.type === "table" ? "#ffffff" : "#17171a";
  context.font = "700 22px Inter, Arial, sans-serif";
  context.textAlign = "center";
  context.textBaseline = "middle";
  context.fillText(label, x + width / 2, y + height / 2, Math.max(width - 16, 40));
  context.restore();
}

function FloorPlanAdminPage({ event, token, activeSubsection }: { event: AdminEvent; token: string; activeSubsection: string }) {
  const [tables, setTables] = useState<AdminTable[]>([]);
  const [guests, setGuests] = useState<AdminGuest[]>([]);
  const [floorObjects, setFloorObjects] = useState<FloorObject[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [showTableForm, setShowTableForm] = useState(false);
  const [editingTableId, setEditingTableId] = useState("");
  const [selectedObjectId, setSelectedObjectId] = useState("");
  const [assignmentQuery, setAssignmentQuery] = useState("");
  const [designExportOrder, setDesignExportOrder] = useState<"lastName" | "firstName" | "tableNumber">("lastName");
  const [error, setError] = useState("");
  const [warning, setWarning] = useState("");
  const [tableDraft, setTableDraft] = useState<AdminTableDraft>(emptyTableDraft());
  const [tableQuery, setTableQuery] = useState("");
  const [tablePage, setTablePage] = useState(1);
  const [totalTableCount, setTotalTableCount] = useState(0);
  const debouncedTableQuery = useDebouncedValue(tableQuery, 300);
  const tablesPerPage = 20;

  const loadTables = useCallback(async (options?: { force?: boolean }) => {
    if (!token) return;

    setLoading(true);
    setError("");

    try {
      const payload = await getAdminTablePage(event.id, token, {
        page: tablePage,
        pageSize: tablesPerPage,
        search: debouncedTableQuery.trim(),
      }, options);
      setTables(payload.items);
      setTotalTableCount(payload.totalCount);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Could not load tables.");
    } finally {
      setLoading(false);
    }
  }, [debouncedTableQuery, event.id, tablePage, token]);

  const loadFloorPlan = useCallback(async (options?: { force?: boolean }) => {
    if (!token) return;

    setLoading(true);
    setError("");

    try {
      const payload = await getAdminFloorPlan(event.id, token, options);
      setTables(payload.tables);
      setGuests(payload.guests);
      setFloorObjects(payload.floorObjects);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Could not load floor plan.");
    } finally {
      setLoading(false);
    }
  }, [event.id, token]);

  useEffect(() => {
    if (activeSubsection === "Tables") {
      void loadTables();
      return;
    }

    if (activeSubsection === "Design") {
      void loadFloorPlan();
    }
  }, [activeSubsection, loadFloorPlan, loadTables]);

  function startCreateTable() {
    setEditingTableId("");
    setTableDraft(emptyTableDraft());
    setShowTableForm(true);
    setError("");
    setWarning("");
  }

  function startEditTable(table: AdminTable) {
    setEditingTableId(table.id);
    setTableDraft({
      name: table.name,
      number: table.number,
      maximumCapacity: table.maximumCapacity,
      shape: table.shape,
      notes: table.notes,
    });
    setShowTableForm(true);
    setError("");
    setWarning("");
  }

  async function saveTable(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    if (!token) return;

    setSaving(true);
    setError("");

    try {
      const linkedObject = floorObjects.find((object) => object.linkedTableId === editingTableId);
      const response = await fetch(apiUrl(editingTableId ? `/api/admin/events/${event.id}/tables/${editingTableId}` : `/api/admin/events/${event.id}/tables`), {
        method: editingTableId ? "PUT" : "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          ...tableDraft,
          width: linkedObject?.width,
          height: linkedObject?.height,
        }),
      });

      if (!response.ok) throw new Error(await readError(response));

      setTableDraft(emptyTableDraft());
      setEditingTableId("");
      setShowTableForm(false);
      clearEventAdminCaches(event.id);
      if (activeSubsection === "Tables") {
        await loadTables({ force: true });
      } else {
        await loadFloorPlan({ force: true });
      }
    } catch (createError) {
      setError(createError instanceof Error ? createError.message : "Could not save table.");
    } finally {
      setSaving(false);
    }
  }

  async function deleteTable(table: AdminTable) {
    if (!token) return;

    setSaving(true);
    setError("");
    setWarning("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/tables/${table.id}`), {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) throw new Error(await readError(response));
      if (editingTableId === table.id) {
        setEditingTableId("");
        setShowTableForm(false);
      }
      clearEventAdminCaches(event.id);
      if (activeSubsection === "Tables") {
        await loadTables({ force: true });
      } else {
        await loadFloorPlan({ force: true });
      }
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : "Could not delete table.");
    } finally {
      setSaving(false);
    }
  }

  function downloadTableTemplate() {
    downloadTextFile(
      "sassoir-table-import-template.csv",
      "Name,Number,Maximum Capacity,Shape,Notes\nOlive Garden,12,10,round,Near the dance floor\nCedar Grove,8,8,rectangle,Family table\n",
      "text/csv",
    );
  }

  function exportTables() {
    const lines = ["Name,Number,Maximum Capacity,Shape,Notes"];
    tables.forEach((table) => {
      lines.push([table.name, table.number, String(table.maximumCapacity), table.shape, table.notes].map(csvCell).join(","));
    });
    downloadTextFile(`${slugify(event.name || "event")}-tables.csv`, `${lines.join("\n")}\n`, "text/csv");
  }

  async function importTables(file: File | undefined) {
    if (!file) return;
    setSaving(true);
    setError("");
    setWarning("");

    try {
      const rows = parseTableCsv(await file.text());
      if (rows.length === 0) throw new Error("No table rows were found in the import file.");

      for (const row of rows) {
        const response = await fetch(apiUrl(`/api/admin/events/${event.id}/tables`), {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${token}`,
          },
          body: JSON.stringify(row),
        });
        if (!response.ok) throw new Error(await readError(response));
      }

      setWarning(`${rows.length} tables imported.`);
      clearEventAdminCaches(event.id);
      if (activeSubsection === "Tables") {
        await loadTables({ force: true });
      } else {
        await loadFloorPlan({ force: true });
      }
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : "Could not import tables.");
    } finally {
      setSaving(false);
    }
  }

  function moveObject(dropEvent: React.DragEvent<HTMLDivElement>) {
    const objectId = dropEvent.dataTransfer.getData("floor-object-id");
    if (!objectId) return;

    dropEvent.preventDefault();
    const rect = dropEvent.currentTarget.getBoundingClientRect();
    setFloorObjects((current) => current.map((object) => {
      if (object.id !== objectId) return object;
      const x = clampUnit((dropEvent.clientX - rect.left) / rect.width - object.width / 2);
      const y = clampUnit((dropEvent.clientY - rect.top) / rect.height - object.height / 2);
      return { ...object, x, y };
    }));
  }

  function addSection(template: Pick<FloorObject, "type" | "label" | "width" | "height" | "shape">) {
    setFloorObjects((current) => [
      ...current,
      {
        id: `${template.type}-${crypto.randomUUID()}`,
        type: template.type,
        label: template.label,
        x: 0.38,
        y: 0.32,
        width: template.width,
        height: template.height,
        shape: template.shape,
        zIndex: current.length + 10,
      },
    ]);
  }

  function resizeSelectedObject(delta: number) {
    if (!selectedObjectId) return;

    setFloorObjects((current) => current.map((object) => {
      if (object.id !== selectedObjectId) return object;
      return {
        ...object,
        width: Math.max(0.04, Math.min(1, object.width + delta)),
        height: Math.max(0.04, Math.min(1, object.height + delta)),
      };
    }));
  }

  const selectedObject = floorObjects.find((object) => object.id === selectedObjectId);
  const filteredAssignmentGuests = guests.filter((guest) => {
    const normalized = normalizeSearch(`${guest.displayName} ${guest.tableCode} ${guest.tableName}`);
    return !normalizeSearch(assignmentQuery) || normalized.includes(normalizeSearch(assignmentQuery));
  });

  async function assignGuestToTable(guestId: string, tableId: string | null | undefined) {
    if (!token || !tableId) {
      setWarning("This table needs to be saved before guests can be assigned to it.");
      return;
    }

    const guest = guests.find((item) => item.id === guestId);
    const table = tables.find((item) => item.id === tableId);
    const guestPeople = guest?.status === "Active" || guest?.status === "CheckedIn" ? normalizePersonCount(guest.personCount) : 0;
    if (guest?.tableId !== tableId && table && table.assignedGuestCount + guestPeople > table.maximumCapacity) {
      setWarning(`${table.name || `Table ${table.number}`} is full.`);
      return;
    }

    setSaving(true);
    setWarning("");
    setError("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/guests/${guestId}/assign-table`), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ tableId }),
      });

      if (!response.ok) throw new Error(await readError(response));
      clearEventAdminCaches(event.id);
      await loadFloorPlan({ force: true });
    } catch (assignError) {
      setWarning(assignError instanceof Error ? assignError.message : "Could not assign guest.");
    } finally {
      setSaving(false);
    }
  }

  async function saveFloorPlan() {
    if (!token) return;

    setSaving(true);
    setError("");
    setWarning("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/floor-plan`), {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ objects: floorObjectsForSave(floorObjects) }),
      });

      if (!response.ok) throw new Error(await readError(response));
      const payload = await response.json();
      const mergedObjects = withTableFloorObjects(toFloorObjects(payload?.objects), tables);
      setFloorObjects(mergedObjects);
      adminEventCache.set(event.id, {
        ...(adminEventCache.get(event.id) ?? {}),
        tables,
        guests,
        floorObjects: mergedObjects,
      });
      setWarning("Floor plan saved.");
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Could not save floor plan.");
    } finally {
      setSaving(false);
    }
  }

  function exportDesignGuests() {
    const sortedGuests = [...guests].sort((left, right) => {
      if (designExportOrder === "firstName") {
        return left.firstName.localeCompare(right.firstName) || left.lastName.localeCompare(right.lastName) || left.displayName.localeCompare(right.displayName);
      }

      if (designExportOrder === "tableNumber") {
        return compareTableNumbers(left.tableCode, right.tableCode) || left.lastName.localeCompare(right.lastName) || left.firstName.localeCompare(right.firstName);
      }

      return left.lastName.localeCompare(right.lastName) || left.firstName.localeCompare(right.firstName) || left.displayName.localeCompare(right.displayName);
    });

    const lines = ["First Name,Last Name,Display Name,Person Count,Table Number,Table Name,Status"];
    sortedGuests.forEach((guest) => {
      lines.push([
        guest.firstName,
        guest.lastName,
        guest.displayName,
        String(normalizePersonCount(guest.personCount)),
        guest.tableCode || "Unassigned",
        guest.tableName,
        guest.status,
      ].map(csvCell).join(","));
    });

    downloadTextFile(`${slugify(event.name || "event")}-guest-table-assignments.csv`, `${lines.join("\n")}\n`, "text/csv");
  }

  function downloadFloorPlanImage() {
    const canvas = document.createElement("canvas");
    const width = 1600;
    const height = 1000;
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext("2d");
    if (!context) return;

    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, width, height);
    context.strokeStyle = "#ededed";
    context.lineWidth = 1;
    for (let x = 0; x <= width; x += 40) {
      context.beginPath();
      context.moveTo(x, 0);
      context.lineTo(x, height);
      context.stroke();
    }
    for (let y = 0; y <= height; y += 40) {
      context.beginPath();
      context.moveTo(0, y);
      context.lineTo(width, y);
      context.stroke();
    }

    [...floorObjects]
      .sort((left, right) => (left.zIndex ?? 0) - (right.zIndex ?? 0))
      .forEach((object) => drawFloorObject(context, object, width, height));

    const link = document.createElement("a");
    link.href = canvas.toDataURL("image/png");
    link.download = `${slugify(event.name || "event")}-floor-plan.png`;
    link.click();
  }

  const totalTablePages = Math.max(1, Math.ceil(totalTableCount / tablesPerPage));
  const currentTablePage = Math.min(tablePage, totalTablePages);
  const tablePaginationStart = Math.max(1, currentTablePage - 2);
  const tablePaginationEnd = Math.min(totalTablePages, currentTablePage + 2);
  const tablePaginationPages = Array.from({ length: tablePaginationEnd - tablePaginationStart + 1 }, (_, index) => tablePaginationStart + index);

  return (
    <>
      {activeSubsection === "Tables" ? (
      <section className="admin-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Tables</p>
            <h2>{event.name}</h2>
          </div>
          <div className="event-actions">
            <button className="secondary-button compact-button" type="button" onClick={downloadTableTemplate}><Download aria-hidden="true" />Template</button>
            <button className="secondary-button compact-button" type="button" onClick={exportTables} disabled={tables.length === 0}><Download aria-hidden="true" />Export</button>
            <label className="secondary-button compact-button import-button">
              <Upload aria-hidden="true" />
              Import
              <input type="file" accept=".csv,text/csv" onChange={(formEvent) => {
                void importTables(formEvent.target.files?.[0]);
                formEvent.target.value = "";
              }} />
            </label>
            <button className="primary-button compact-button" type="button" onClick={startCreateTable}><Table2 aria-hidden="true" />Add Table</button>
          </div>
        </div>

        {error ? <p className="form-error">{error}</p> : null}

        <div className="list-toolbar">
          <label className="admin-search">
            <Search aria-hidden="true" />
            <input value={tableQuery} onChange={(formEvent) => {
              setTableQuery(formEvent.target.value);
              setTablePage(1);
            }} placeholder="Search tables" aria-label="Search tables" />
          </label>
        </div>

        {showTableForm ? (
          <form className="event-form" onSubmit={saveTable}>
            <div className="form-field-grid">
              <label>
                Name
                <input value={tableDraft.name} onChange={(formEvent) => setTableDraft((current) => ({ ...current, name: formEvent.target.value }))} placeholder="Olive Garden" />
              </label>
              <label>
                Number
                <input value={tableDraft.number} onChange={(formEvent) => setTableDraft((current) => ({ ...current, number: formEvent.target.value }))} placeholder="12" />
              </label>
              <label>
                Maximum Capacity
                <input type="number" min="1" value={tableDraft.maximumCapacity} onChange={(formEvent) => setTableDraft((current) => ({ ...current, maximumCapacity: Number(formEvent.target.value) }))} />
              </label>
              <label>
                Shape
                <select value={tableDraft.shape} onChange={(formEvent) => setTableDraft((current) => ({ ...current, shape: formEvent.target.value as AdminTable["shape"] }))}>
                  {tableShapeOptions.map((shape) => <option key={shape.value} value={shape.value}>{shape.label}</option>)}
                </select>
              </label>
              {editingTableId ? (
                <label>
                  Assigned Person Count
                  <input value={tables.find((table) => table.id === editingTableId)?.assignedGuestCount ?? 0} readOnly />
                </label>
              ) : null}
              <label className="span-2">
                Notes
                <textarea value={tableDraft.notes} onChange={(formEvent) => setTableDraft((current) => ({ ...current, notes: formEvent.target.value }))} rows={3} placeholder="VIP table, aisle access, near stage..." />
              </label>
            </div>
            <div className="form-actions">
              <button className="secondary-button compact-button" type="button" onClick={() => {
                setShowTableForm(false);
                setEditingTableId("");
                setTableDraft(emptyTableDraft());
              }}>Cancel</button>
              <button className="primary-button compact-button" type="submit" disabled={saving}>{saving ? "Saving..." : editingTableId ? "Update Table" : "Create Table"}</button>
            </div>
          </form>
        ) : null}

        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Number</th>
                <th>Shape</th>
                <th>Maximum Capacity</th>
                <th>Assigned Persons</th>
                <th>Notes</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {tables.map((table) => (
                <tr key={table.id}>
                  <td data-label="Name"><strong>{table.name}</strong></td>
                  <td data-label="Number">{table.number}</td>
                  <td data-label="Shape">{tableShapeOptions.find((shape) => shape.value === table.shape)?.label ?? table.shape}</td>
                  <td data-label="Maximum Capacity">{table.maximumCapacity}</td>
                  <td data-label="Assigned Persons">{table.assignedGuestCount}</td>
                  <td data-label="Notes">{table.notes || "-"}</td>
                  <td className="actions-cell" data-label="Actions">
                    <div className="event-actions">
                      <button className="icon-button" type="button" onClick={() => startEditTable(table)} aria-label={`Edit table ${table.number}`}><Pencil aria-hidden="true" /></button>
                      <button className="icon-button danger-button" type="button" onClick={() => void deleteTable(table)} disabled={saving} aria-label={`Delete table ${table.number}`}><Trash2 aria-hidden="true" /></button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {!loading && totalTableCount === 0 ? <p className="empty-state">No tables yet. Add tables before designing the floor plan.</p> : null}
          {totalTableCount > 0 ? (
            <div className="pagination">
              <span>Showing {(currentTablePage - 1) * tablesPerPage + 1}-{Math.min(currentTablePage * tablesPerPage, totalTableCount)} of {totalTableCount}</span>
              <div className="page-nums">
                <button type="button" onClick={() => setTablePage(Math.max(1, currentTablePage - 1))} disabled={currentTablePage === 1}>Prev</button>
                {tablePaginationStart > 1 ? <button type="button" onClick={() => setTablePage(1)}>1</button> : null}
                {tablePaginationStart > 2 ? <span>...</span> : null}
                {tablePaginationPages.map((pageNumber) => (
                  <button className={pageNumber === currentTablePage ? "active" : ""} key={pageNumber} type="button" onClick={() => setTablePage(pageNumber)}>
                    {pageNumber}
                  </button>
                ))}
                {tablePaginationEnd < totalTablePages - 1 ? <span>...</span> : null}
                {tablePaginationEnd < totalTablePages ? <button type="button" onClick={() => setTablePage(totalTablePages)}>{totalTablePages}</button> : null}
                <button type="button" onClick={() => setTablePage(Math.min(totalTablePages, currentTablePage + 1))} disabled={currentTablePage === totalTablePages}>Next</button>
              </div>
            </div>
          ) : null}
        </div>
      </section>
      ) : null}

      {activeSubsection === "Design" ? (
      <section className="admin-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Design</p>
            <h2>Floor plan</h2>
          </div>
          <div className="event-actions design-export-actions">
            <select value={designExportOrder} onChange={(formEvent) => setDesignExportOrder(formEvent.target.value as "lastName" | "firstName" | "tableNumber")} aria-label="Order guest assignment export">
              <option value="lastName">Order by last name</option>
              <option value="firstName">Order by first name</option>
              <option value="tableNumber">Order by table number</option>
            </select>
            <button className="secondary-button compact-button" type="button" onClick={exportDesignGuests} disabled={guests.length === 0}><Download aria-hidden="true" />Export guests</button>
            <button className="secondary-button compact-button" type="button" onClick={downloadFloorPlanImage} disabled={floorObjects.length === 0}><Download aria-hidden="true" />Download image</button>
            <button className="primary-button compact-button" type="button" onClick={() => void saveFloorPlan()} disabled={saving}><Map aria-hidden="true" />{saving ? "Saving..." : "Save Floor Plan"}</button>
          </div>
        </div>

        {warning ? <p className="designer-warning">{warning}</p> : null}

        <div className="designer-layout">
          <div>
            <div className="designer-section-tools" aria-label="Add floor plan section">
              {floorSectionTemplates.map((template) => (
                <button className="secondary-button compact-button" type="button" key={template.type} onClick={() => addSection(template)}><Plus aria-hidden="true" />{template.label}</button>
              ))}
              <button className="secondary-button compact-button" type="button" onClick={() => resizeSelectedObject(-0.02)} disabled={!selectedObject}><Minus aria-hidden="true" />Smaller</button>
              <button className="secondary-button compact-button" type="button" onClick={() => resizeSelectedObject(0.02)} disabled={!selectedObject}><Plus aria-hidden="true" />Bigger</button>
              {selectedObject ? <span className="designer-selection">Selected: {selectedObject.label}</span> : null}
            </div>

            <div className="floor-designer-canvas" onDragOver={(dragEvent) => dragEvent.preventDefault()} onDrop={moveObject} aria-label="Drag tables and venue sections to arrange the floor plan">
              {floorObjects.map((object) => (
                <div
                  className={`floor-designer-object ${object.type} ${object.shape}`}
                  draggable
                  key={object.id}
                  onClick={() => setSelectedObjectId(object.id)}
                  onDragStart={(dragEvent) => dragEvent.dataTransfer.setData("floor-object-id", object.id)}
                  onDragOver={object.type === "table" ? (dragEvent) => dragEvent.preventDefault() : undefined}
                  onDrop={object.type === "table" ? (dropEvent) => {
                    const guestId = dropEvent.dataTransfer.getData("guest-id");
                    if (!guestId) return;
                    dropEvent.preventDefault();
                    dropEvent.stopPropagation();
                    void assignGuestToTable(guestId, object.linkedTableId);
                  } : undefined}
                  style={{
                    left: `${object.x * 100}%`,
                    top: `${object.y * 100}%`,
                    width: `${object.width * 100}%`,
                    height: `${object.height * 100}%`,
                    zIndex: object.zIndex ?? 1,
                  }}
                >
                  <span>{object.type === "table" ? `Table ${object.tableCode ?? object.label}${object.tableName ? ` - ${object.tableName}` : ""}` : object.label}</span>
                </div>
              ))}
            </div>
          </div>

          <aside className="guest-assignment-panel" aria-label="Guests to assign">
            <div>
              <p className="eyebrow">Guests</p>
              <h3>Drag to a table</h3>
            </div>
            <label className="admin-search assignment-search">
              <Search aria-hidden="true" />
              <input value={assignmentQuery} onChange={(formEvent) => setAssignmentQuery(formEvent.target.value)} placeholder="Search guests" aria-label="Search guests to assign" />
            </label>
            <div className="guest-chip-list">
              {filteredAssignmentGuests.map((guest) => (
                <button
                  className={`guest-chip ${guest.tableId ? "assigned" : ""}`}
                  draggable
                  key={guest.id}
                  type="button"
                  onDragStart={(dragEvent) => dragEvent.dataTransfer.setData("guest-id", guest.id)}
                >
                  <strong>{guest.displayName}</strong>
                  <span>{normalizePersonCount(guest.personCount)} {normalizePersonCount(guest.personCount) === 1 ? "person" : "people"} - {guest.tableCode ? `Table ${guest.tableCode}` : "Unassigned"}</span>
                </button>
              ))}
            </div>
          </aside>
        </div>
      </section>
      ) : null}
    </>
  );
}

function PublishPage({ events, saving, onSetPublication }: {
  events: AdminEvent[];
  saving: boolean;
  onSetPublication: (eventId: string, status: "Published" | "Draft") => void;
}) {
  return (
    <section className="admin-panel">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Public links</p>
          <h2>Publish and generate QR codes</h2>
        </div>
      </div>
      <div className="publish-grid">
        {events.map((event) => {
          const published = eventStatusText(event.status).toLowerCase() === "published";
          const publicUrl = getEventPublicUrl(event);
          return (
            <article className="publish-card" key={event.id}>
              <div>
                <span className={`event-status ${published ? "published" : ""}`}>{eventStatusText(event.status)}</span>
                <h3>{event.name}</h3>
                <p>{publicUrl}</p>
              </div>
              {published ? <QrCodeImage value={publicUrl} label={`QR code for ${event.name}`} /> : <div className="qr-box unpublished-qr" aria-label={`QR code unavailable for ${event.name}`}><QrCode aria-hidden="true" /><span>Publish to generate</span></div>}
              <div className="event-actions">
                <a className="secondary-button compact-button" href={`/e/${event.slug}`}><Eye aria-hidden="true" />Preview</a>
                {published ? <button className="secondary-button compact-button" type="button" onClick={() => downloadEventQr(event)}><Download aria-hidden="true" />Download QR</button> : null}
                <button className="primary-button compact-button" type="button" disabled={saving} onClick={() => onSetPublication(event.id, published ? "Draft" : "Published")}>{published ? "Unpublish" : "Publish"}</button>
              </div>
            </article>
          );
        })}
      </div>
    </section>
  );
}

function EventSetupPage({ event, token, activeSubsection }: { event: AdminEvent; token: string; activeSubsection: string }) {
  const published = eventStatusText(event.status).toLowerCase() === "published";
  const publicUrl = getEventPublicUrl(event);
  const [messages, setMessages] = useState<AdminGuestMessage[]>([]);
  const [loadingMessages, setLoadingMessages] = useState(false);

  useEffect(() => {
    let cancelled = false;
    if (!token) return;

    async function loadMessages() {
      setLoadingMessages(true);
      try {
        const response = await fetch(apiUrl(`/api/admin/events/${event.id}/messages`), {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!response.ok) throw new Error("Could not load messages.");
        const payload = (await response.json()) as AdminGuestMessage[];
        if (!cancelled) setMessages(payload);
      } catch {
        if (!cancelled) setMessages([]);
      } finally {
        if (!cancelled) setLoadingMessages(false);
      }
    }

    if (published && activeSubsection === "Guest messages") void loadMessages();
    return () => {
      cancelled = true;
    };
  }, [activeSubsection, event.id, published, token]);

  function exportMessages() {
    const lines = ["Guest Name,Message,Created At"];
    messages.forEach((message) => {
      lines.push([message.guestName, message.message, new Date(message.createdAt).toLocaleString()].map(csvCell).join(","));
    });
    downloadTextFile(`${slugify(event.name || "event")}-guest-messages.csv`, `${lines.join("\n")}\n`, "text/csv");
  }

  return (
    <>
      {activeSubsection === "QR Code" ? (
      <section className="admin-panel setup-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Setup</p>
            <h2>Event QR code</h2>
          </div>
          <span className={`event-status ${published ? "published" : ""}`}>{eventStatusText(event.status)}</span>
        </div>
        {published ? (
          <div className="setup-qr-layout">
            <QrCodeImage value={publicUrl} label={`QR code for ${event.name}`} />
            <div className="setup-copy">
              <strong>{event.name}</strong>
              <p>{publicUrl}</p>
              <div className="event-actions">
                <a className="secondary-button compact-button" href={`/e/${event.slug}`}><Eye aria-hidden="true" />Preview public page</a>
                <button className="primary-button compact-button" type="button" onClick={() => downloadEventQr(event)}><Download aria-hidden="true" />Download QR</button>
              </div>
            </div>
          </div>
        ) : (
          <div className="setup-empty-state">
            <QrCode aria-hidden="true" />
            <div>
              <h3>Publish this event to generate its QR code</h3>
              <p className="admin-muted">The code will point scanners directly to the public event page once publishing is complete.</p>
            </div>
          </div>
        )}
      </section>
      ) : null}

      {activeSubsection === "Guest messages" ? (
        <section className="admin-panel">
          <div className="panel-heading">
            <div>
              <p className="eyebrow">Guest messages</p>
              <h2>Messages guests left</h2>
            </div>
            <button className="secondary-button compact-button" type="button" onClick={exportMessages} disabled={messages.length === 0}><Download aria-hidden="true" />Export</button>
          </div>
          {published ? (
            <>
              {loadingMessages ? <p className="admin-muted">Loading messages...</p> : null}
              <div className="message-list">
                {messages.map((message) => (
                  <article className="message-card" key={message.id}>
                    <strong>{message.guestName}</strong>
                    <p>{message.message}</p>
                    <span>{new Date(message.createdAt).toLocaleString()}</span>
                  </article>
                ))}
              </div>
              {!loadingMessages && messages.length === 0 ? <p className="empty-state">No guest messages yet.</p> : null}
            </>
          ) : (
            <p className="empty-state">Publish this event before collecting guest messages.</p>
          )}
        </section>
      ) : null}
    </>
  );
}

function QrCodeImage({ value, label }: { value: string; label: string }) {
  try {
    return <img className="qr-box qr-image" src={createQrDataUri(value)} alt={label} />;
  } catch (error) {
    return <div className="qr-box qr-error" role="alert">{error instanceof Error ? error.message : "Could not generate QR code."}</div>;
  }
}

function AnalyticsPage({ events }: { events: AdminEvent[] }) {
  const totalGuests = events.reduce((sum, event) => sum + event.guestCount, 0);
  const assignedGuests = events.reduce((sum, event) => sum + event.assignedGuests, 0);
  const successRate = totalGuests > 0 ? Math.round((assignedGuests / totalGuests) * 100) : 0;

  return (
    <>
      <section className="admin-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">MVP metrics</p>
            <h2>Guest experience health</h2>
          </div>
        </div>
        <div className="admin-card-grid">
          <InfoCard label="Search success" value={`${successRate}%`} detail="Approximation until analytics storage is connected." />
          <InfoCard label="Seat result views" value={assignedGuests} detail="Guests with confirmed seating." />
          <InfoCard label="Floor plan opens" value={Math.max(assignedGuests - 1, 0)} detail="Demo projection for the public flow." />
        </div>
      </section>
    </>
  );
}

function ContactSubmissionsPage({ token }: { token: string }) {
  const [submissions, setSubmissions] = useState<ContactSubmission[]>([]);
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const pageSize = 20;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  const loadSubmissions = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const response = await fetch(apiUrl(`/api/contact?page=${page}&pageSize=${pageSize}`), {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) throw new Error(await readError(response));

      const payload = (await response.json()) as PaginatedResponse<ContactSubmission>;
      setSubmissions(payload.items);
      setTotalCount(payload.totalCount);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Could not load contact submissions.");
    } finally {
      setLoading(false);
    }
  }, [page, token]);

  useEffect(() => {
    void loadSubmissions();
  }, [loadSubmissions]);

  return (
    <section className="admin-panel">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Landing page</p>
          <h2>Contact submissions</h2>
        </div>
        {loading ? <span className="api-status">Loading...</span> : null}
      </div>
      {error ? <p className="form-error" role="alert">{error}</p> : null}
      <div className="admin-table-wrap">
        <table className="admin-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Email</th>
              <th>Message</th>
              <th>Submitted</th>
            </tr>
          </thead>
          <tbody>
            {submissions.map((submission) => (
              <tr key={submission.id}>
                <td data-label="Name"><strong>{submission.name}</strong></td>
                <td data-label="Email"><a href={`mailto:${submission.email}`}>{submission.email}</a></td>
                <td data-label="Message">{submission.message}</td>
                <td data-label="Submitted">{new Date(submission.submittedAtUtc).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {submissions.length > 0 ? (
          <div className="pagination">
            <span>Showing {(page - 1) * pageSize + 1} to {Math.min(page * pageSize, totalCount)} of {totalCount} submissions</span>
            <div className="page-nums" aria-label="Contact submissions pagination">
              <button type="button" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page <= 1}>‹</button>
              <button className="active" type="button">{page}</button>
              <button type="button" onClick={() => setPage((current) => Math.min(totalPages, current + 1))} disabled={page >= totalPages}>›</button>
            </div>
          </div>
        ) : null}
        {!loading && submissions.length === 0 ? <p className="empty-state">No contact submissions yet.</p> : null}
      </div>
    </section>
  );
}

function SettingsPage() {
  return (
    <>
      <section className="admin-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Organization</p>
            <h2>Demo Events</h2>
          </div>
        </div>
        <div className="settings-list">
          <label><span>Default public privacy label</span><select defaultValue="group"><option value="group">Guest group only</option><option value="companion">Companion label</option><option value="initial">Last initial</option></select></label>
          <label><span>Default event status</span><select defaultValue="Draft"><option>Draft</option><option>Published</option></select></label>
          <label><span>Admin email</span><input defaultValue="admin@sassoir.com" /></label>
        </div>
      </section>
    </>
  );
}

function InfoCard({ label, value, detail }: { label: string; value: string | number; detail: string }) {
  return (
    <article className="info-card">
      <p>{label}</p>
      <strong>{value}</strong>
      <span>{detail}</span>
    </article>
  );
}

function AdminLogin({ onLogin, loading, error }: {
  onLogin: (email: string, password: string) => Promise<void>;
  loading: boolean;
  error: string;
}) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [resetToken, setResetToken] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [resetMessage, setResetMessage] = useState("");
  const [resetError, setResetError] = useState("");
  const [showReset, setShowReset] = useState(false);

  async function submitLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await onLogin(email, password);
  }

  async function requestReset() {
    setResetError("");
    setResetMessage("");

    try {
      const response = await fetch(apiUrl("/api/auth/forgot-password"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email }),
      });
      if (!response.ok) throw new Error(await readError(response));
      const payload = (await response.json()) as { message: string; resetToken?: string | null };
      setResetToken(payload.resetToken ?? "");
      setResetMessage(payload.resetToken ? "Reset token generated for this admin account." : payload.message);
      setShowReset(true);
    } catch (forgotError) {
      setResetError(forgotError instanceof Error ? forgotError.message : "Could not request password reset.");
    }
  }

  async function submitReset(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setResetError("");
    setResetMessage("");

    try {
      const response = await fetch(apiUrl("/api/auth/reset-password"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ resetToken, newPassword }),
      });
      if (!response.ok) throw new Error(await readError(response));
      setPassword(newPassword);
      setNewPassword("");
      setResetToken("");
      setShowReset(false);
      setResetMessage("Password reset. Sign in with the new password.");
    } catch (resetSubmitError) {
      setResetError(resetSubmitError instanceof Error ? resetSubmitError.message : "Could not reset password.");
    }
  }

  return (
    <main className="login-shell">
      <section className="login-card" aria-label="Admin login">
        <div className="login-brand">
          <span><Lock aria-hidden="true" /></span>
          <p className="eyebrow">Sassoir Admin</p>
          <h1>Sign in to manage events</h1>
          <p>Create events, publish public pages, and manage the guest seating experience.</p>
        </div>

        <form className="login-form" onSubmit={submitLogin}>
          <label htmlFor="admin-email">Email</label>
          <input id="admin-email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="username" />
          <label htmlFor="admin-password">Password</label>
          <input id="admin-password" type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="current-password" />
          <button className="primary-button" type="submit">{loading ? "Signing in..." : "Sign In"}</button>
          <button className="secondary-button compact-button" type="button" onClick={() => void requestReset()}>Forgot password</button>
          {error ? <p className="form-error" role="alert">{error}</p> : null}
          {resetError ? <p className="form-error" role="alert">{resetError}</p> : null}
          {resetMessage ? <p className="designer-warning" role="status">{resetMessage}</p> : null}
        </form>

        {showReset ? (
          <form className="login-form reset-form" onSubmit={submitReset}>
            <label htmlFor="reset-token">Reset token</label>
            <textarea id="reset-token" value={resetToken} onChange={(event) => setResetToken(event.target.value)} rows={4} />
            <label htmlFor="reset-new-password">New password</label>
            <input id="reset-new-password" type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} autoComplete="new-password" />
            <button className="primary-button" type="submit">Reset Password</button>
          </form>
        ) : null}
      </section>
    </main>
  );
}

function ChangePasswordDialog({ saving, error, onClose, onSubmit }: {
  saving: boolean;
  error: string;
  onClose: () => void;
  onSubmit: (currentPassword: string, newPassword: string) => void;
}) {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onSubmit(currentPassword, newPassword);
  }

  return (
    <div className="modal-backdrop" role="presentation">
      <section className="modal-panel" role="dialog" aria-modal="true" aria-label="Change password">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Security</p>
            <h2>Change password</h2>
          </div>
          <button className="icon-button" type="button" onClick={onClose} aria-label="Close password dialog"><X aria-hidden="true" /></button>
        </div>
        <form className="event-form" onSubmit={submit}>
          <div className="form-field-grid">
            <label>
              Current password
              <input type="password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} autoComplete="current-password" />
            </label>
            <label>
              New password
              <input type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} autoComplete="new-password" />
            </label>
          </div>
          {error ? <p className="form-error" role="alert">{error}</p> : null}
          <div className="form-actions">
            <button className="secondary-button compact-button" type="button" onClick={onClose}>Cancel</button>
            <button className="primary-button compact-button" type="submit" disabled={saving}>{saving ? "Saving..." : "Update password"}</button>
          </div>
        </form>
      </section>
    </div>
  );
}

function EventEditorForm({ draft, editorEvent, token, onDraftChange, onSubmit, onImageUpload, saving, editing }: {
  draft: AdminEventDraft;
  editorEvent?: AdminEvent;
  token: string;
  onDraftChange: (draft: AdminEventDraft) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onImageUpload: (file: File) => Promise<string>;
  saving: boolean;
  editing: boolean;
}) {
  const [activeSectionName, setActiveSectionName] = useState(eventFormSections[0].name);
  const activeSection = eventFormSections.find((section) => section.name === activeSectionName) ?? eventFormSections[0];
  const [activeSubsectionName, setActiveSubsectionName] = useState(activeSection.subsections[0].name);
  const activeSubsection = activeSection.subsections.find((subsection) => subsection.name === activeSubsectionName) ?? activeSection.subsections[0];
  const [uploadingImage, setUploadingImage] = useState(false);
  const [uploadError, setUploadError] = useState("");

  const update = (field: keyof AdminEventDraft, value: string) => {
    if (field === "name") {
      onDraftChange({
        ...draft,
        name: value,
        slug: slugify(value),
        welcomeTitle: draft.welcomeTitle || `Welcome to ${value}`,
      });
      return;
    }

    onDraftChange({
      ...draft,
      [field]: field === "slug" ? slugify(value) : value,
    });
  };

  function chooseSection(section: EventSectionDefinition) {
    setActiveSectionName(section.name);
    setActiveSubsectionName(section.subsections[0].name);
  }

  async function handleImageUpload(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;

    setUploadingImage(true);
    setUploadError("");

    try {
      const url = await onImageUpload(file);
      onDraftChange({ ...draft, heroImageUrl: url });
    } catch (uploadError) {
      setUploadError(uploadError instanceof Error ? uploadError.message : "Could not upload image.");
    } finally {
      setUploadingImage(false);
      event.target.value = "";
    }
  }

  const isDetailsSection = activeSection.name === "General" || activeSection.name === "Branding";

  return (
    <div className="event-form event-section-form">
      <form className="sr-only" id="event-editor-form" onSubmit={onSubmit} aria-hidden="true" />
      <nav className="section-tabs" aria-label="Event sections">
        {eventFormSections.map((section) => (
          <button
            className={section.name === activeSection.name ? "active" : ""}
            key={section.name}
            type="button"
            onClick={() => chooseSection(section)}
          >
            {section.name}
          </button>
        ))}
      </nav>

      <nav className="subsection-tabs" aria-label={`${activeSection.name} subsections`}>
        {activeSection.subsections.map((subsection) => (
          <button
            className={subsection.name === activeSubsection.name ? "active" : ""}
            key={subsection.name}
            type="button"
            onClick={() => setActiveSubsectionName(subsection.name)}
          >
            {subsection.name}
          </button>
        ))}
      </nav>

      {isDetailsSection ? (
        <>
          <div className="form-layout">
            <div className="form-field-grid">
              {activeSubsection.fields.map((field) => {
                const value = field.draftField ? draft[field.draftField] : "";
                const id = `${activeSection.name}-${activeSubsection.name}-${field.label}`.replace(/\W+/g, "-").toLowerCase();

                if (field.type === "select") {
                  return (
                    <label key={id} htmlFor={id}>
                      {field.label}
                      <select id={id} value={value} onChange={(event) => field.draftField ? update(field.draftField, event.target.value) : undefined}>
                        {(field.options ?? ["Draft", "Published", "Archived"]).map((option) => <option key={option} value={option}>{option}</option>)}
                      </select>
                    </label>
                  );
                }

                return (
                  <label key={id} htmlFor={id}>
                    {field.label}
                    <input
                      id={id}
                      type={field.type === "color" ? "color" : field.type === "url" ? "url" : "text"}
                      value={value}
                      onChange={(event) => field.draftField ? update(field.draftField, event.target.value) : undefined}
                      placeholder={field.placeholder ?? ""}
                      readOnly={!field.draftField}
                    />
                  </label>
                );
              })}
            </div>

            <aside className="cover-card" aria-label="Guest welcome image preview">
              <strong>Guest flow image</strong>
              <div
                className="cover-drop image-preview-drop"
                style={{ backgroundImage: `url(${assetUrl(draft.heroImageUrl) || guestWeddingBanner})` }}
                aria-hidden="true"
              />
              <label className="image-upload-control" htmlFor="welcome-image-upload">
                <Upload aria-hidden="true" />
                <span>{uploadingImage ? "Uploading..." : "Upload image"}</span>
                <input id="welcome-image-upload" type="file" accept="image/png,image/jpeg,image/webp,image/gif" onChange={handleImageUpload} disabled={uploadingImage} />
              </label>
              {uploadError ? <p className="form-error" role="alert">{uploadError}</p> : null}
              <p>This image appears at the top of the public guest welcome page.</p>
            </aside>
          </div>

          <div className="form-actions">
            <button className="secondary-button" type="submit" form="event-editor-form">Save as draft</button>
            <button className="primary-button" type="submit" form="event-editor-form">{saving ? "Saving..." : editing ? "Save event" : "Save & continue"}</button>
          </div>
        </>
      ) : null}

      {activeSection.name === "Guests" ? (
        editorEvent ? <GuestsPage event={editorEvent} token={token} /> : <UnsavedEventSection sectionName="Guests" />
      ) : null}

      {activeSection.name === "Floor Plan" ? (
        editorEvent ? <FloorPlanAdminPage event={editorEvent} token={token} activeSubsection={activeSubsection.name} /> : <UnsavedEventSection sectionName="Floor Plan" />
      ) : null}

      {activeSection.name === "Setup" ? (
        editorEvent ? <EventSetupPage event={editorEvent} token={token} activeSubsection={activeSubsection.name} /> : <UnsavedEventSection sectionName="Setup" />
      ) : null}
    </div>
  );
}

function UnsavedEventSection({ sectionName }: { sectionName: string }) {
  return (
    <section className="admin-panel setup-placeholder">
      <div>
        <p className="eyebrow">{sectionName}</p>
        <h2>Save the event first</h2>
      </div>
      <p className="admin-muted">This section needs an event record before guests, tables, and floor plan changes can be saved.</p>
    </section>
  );
}

function emptyEventDraft(): AdminEventDraft {
  return {
    name: "",
    slug: "",
    eventType: "Wedding",
    subtitle: "",
    dateLabel: "",
    venueName: "",
    venueAddress: "",
    status: "Draft",
    heroText: "",
    primaryColor: "#D8CFBC",
    secondaryColor: "#565449",
    backgroundColor: "#FFFBF4",
    textColor: "#11120D",
    welcomeTitle: "",
    searchInputLabel: "Search by name",
    searchPlaceholder: "Search by name",
    heroImageUrl: "/guest-wedding-banner.png",
  };
}

function slugify(value: string) {
  return value
    .toLowerCase()
    .trim()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function buildGuestDisplayName(firstName: string, lastName: string) {
  return [firstName, lastName].map((part) => part.trim()).filter(Boolean).join(" ");
}

function getEventPublicUrl(event: AdminEvent) {
  const origin = typeof window === "undefined" ? "" : window.location.origin;
  return `${origin}/e/${event.slug}`;
}

function downloadEventQr(event: AdminEvent) {
  downloadTextFile(`${slugify(event.name || "event")}-qr-code.svg`, createQrSvg(getEventPublicUrl(event)), "image/svg+xml");
}

function downloadTextFile(fileName: string, content: string, type: string) {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

async function readError(response: Response) {
  try {
    const payload = await response.json();
    if (payload.message) return payload.message;
    if (payload.errors) {
      return Object.values(payload.errors).flat().join(" ");
    }
  } catch {
    return "Could not save event.";
  }

  return "Could not save event.";
}

function MetricCard({ icon, label, value }: { icon: React.ReactNode; label: string; value: string | number }) {
  return (
    <article className="metric-card">
      <span>{icon}</span>
      <p>{label}</p>
      <strong>{value}</strong>
    </article>
  );
}

function initials(name: string) {
  return name
    .split(/\s+/)
    .map((part) => part[0])
    .join("")
    .replace(".", "")
    .slice(0, 2)
    .toUpperCase();
}
