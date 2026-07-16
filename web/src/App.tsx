import { ChangeEvent, Component, FormEvent, ReactNode, useCallback, useEffect, useMemo, useState } from "react";
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
  Globe2,
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

type FloorObject = {
  id: string;
  type: string;
  label: string;
  linkedTableId?: string | null;
  tableCode?: string | null;
  x: number;
  y: number;
  width: number;
  height: number;
  shape: "round" | "rect";
  zIndex?: number;
};

type AdminGuest = {
  id: string;
  firstName: string;
  lastName: string;
  displayName: string;
  notes: string;
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
};

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
  tableId: string;
  status: AdminGuest["status"];
};

type ImportPreviewRow = {
  rowNumber: number;
  firstName: string;
  lastName: string;
  displayName: string;
  notes: string;
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
type AdminPage = "dashboard" | "events" | "guests" | "floor-plan" | "publish" | "analytics" | "settings";

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
        name: "Tables & design",
        fields: [],
      },
    ],
  },
  {
    name: "Setup",
    subsections: [
      {
        name: "QR code",
        fields: [],
      },
    ],
  },
];

function getRoute() {
  const path = window.location.pathname || "/";

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
    x: Number(item.x),
    y: Number(item.y),
    width: Number(item.width),
    height: Number(item.height),
    shape: item.shape === "round" ? "round" : "rect",
    zIndex: Number(item.zIndex ?? 0),
  }));
}

function withTableFloorObjects(objects: FloorObject[], tables: AdminTable[]) {
  const merged = [...objects];

  tables.forEach((table, index) => {
    const existing = merged.some((object) => object.linkedTableId === table.id || (object.type === "table" && object.tableCode === table.number));
    if (existing) return;

    merged.push({
      id: `table-${table.id}`,
      type: "table",
      label: table.name || `Table ${table.number}`,
      linkedTableId: table.id,
      tableCode: table.number,
      x: 0.12 + (index % 4) * 0.18,
      y: 0.24 + Math.floor(index / 4) * 0.18,
      width: 0.14,
      height: 0.14,
      shape: "round",
      zIndex: 5 + index,
    });
  });

  return merged;
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

  return route.area === "admin" ? <AdminDashboard page={route.adminPage} /> : <PublicGuestExperience eventSlug={route.eventSlug} />;
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
              safeRemoveStorage("sassoir_admin_token");
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

  useEffect(() => {
    let cancelled = false;

    async function loadEvent() {
      setLoadState("loading");
      setSelectedGuest(null);
      setMode("search");
      setQuery("");
      setRemoteResults(null);

      try {
        const [eventResponse, floorPlanResponse] = await Promise.all([
          fetch(apiUrl(`/api/public/events/${eventSlug}`)),
          fetch(apiUrl(`/api/public/events/${eventSlug}/floor-plan`)),
        ]);

        if (eventResponse.status === 404) {
          if (!cancelled) setLoadState("notFound");
          return;
        }

        if (!eventResponse.ok) throw new Error("API unavailable");

        const publicEvent = (await eventResponse.json()) as PublicEvent;
        const floorPlan = floorPlanResponse.ok ? await floorPlanResponse.json() : null;
        if (cancelled) return;

        setEvent(publicEvent);
        setFloorObjects(toFloorObjects(floorPlan?.objects));
        setApiOnline(true);
        setLoadState("ready");
      } catch {
        if (cancelled) return;

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
    };
  }, [eventSlug]);

  const localResults = useMemo(() => {
    if (normalizeSearch(query).length < 2) return [];

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

      if (normalizedQuery.length < 2) {
        setRemoteResults(null);
        setLoading(false);
        return;
      }

      setLoading(true);

      try {
        const response = await fetch(apiUrl(`/api/public/events/${eventSlug}/guests/search`), {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ query: rawQuery }),
        });
        if (!response.ok) throw new Error("Search failed");
        const payload = await response.json();
        setRemoteResults(payload.results ?? []);
        setApiOnline(true);
      } catch {
        setRemoteResults(null);
        setApiOnline(false);
      } finally {
        setLoading(false);
      }
    },
    [eventSlug],
  );

  useEffect(() => {
    const normalizedQuery = normalizeSearch(query);
    setRemoteResults(null);

    if (normalizedQuery.length < 2) {
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
    try {
      const response = await fetch(apiUrl(`/api/public/events/${eventSlug}/guests/${searchResult.publicToken}`));
      if (!response.ok) throw new Error("Lookup failed");
      const payload = await response.json();
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
      setApiOnline(true);
    } catch {
      const fallbackGuest = findFallbackGuest(searchResult.publicToken);
      if (!fallbackGuest) return;
      setSelectedGuest(fallbackGuest);
      setApiOnline(false);
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
      >
        <button className="language-button" type="button" aria-label="Change language">
          <Globe2 aria-hidden="true" />
        </button>
      </header>

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
          <p>You are on the table <strong>{guest.tableCode}</strong></p>
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
  const hasDynamicTable = objects.some((object) => object.type === "table" && (object.tableCode === tableCode || object.id === `table-${tableCode}`));

  if (!hasDynamicTable) return <MinimalFloorPlan tableCode={tableCode} />;

  return (
    <section className="minimal-floor-plan" aria-label={`Floor plan highlighting table ${tableCode}`}>
      <div className="guest-plan-route" aria-hidden="true" />
      {objects.map((object) => {
        const highlighted = object.type === "table" && (object.tableCode === tableCode || object.id === `table-${tableCode}`);
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
            {object.type === "table" ? object.tableCode ?? object.label.replace(/^Table\s+/i, "") : object.label}
          </div>
        );
      })}
    </section>
  );
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
  const [user, setUser] = useState<AdminUser | null>(null);
  const [apiOnline, setApiOnline] = useState(false);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [editingEventId, setEditingEventId] = useState<string | null>(null);
  const [draft, setDraft] = useState<AdminEventDraft>(emptyEventDraft());

  const resetDraft = () => {
    setEditingEventId(null);
    setDraft(emptyEventDraft());
    setError("");
  };

  const loadEvents = useCallback(async (authToken: string) => {
    setLoading(true);
    setError("");

    try {
      const response = await fetch(apiUrl("/api/admin/events"), {
        headers: { Authorization: `Bearer ${authToken}` },
      });
      if (response.status === 401) throw new Error("Your admin session expired. Please sign in again.");
      if (!response.ok) throw new Error("Could not load events.");

      const payload = (await response.json()) as AdminEvent[];
      setEvents(payload);
      setApiOnline(true);
    } catch (loadError) {
      setApiOnline(false);
      setEvents(fallbackAdminEvents);
      setError(loadError instanceof Error ? loadError.message : "Could not load events.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function restoreSession() {
      if (!token) return;

      try {
        const response = await fetch(apiUrl("/api/auth/me"), {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!response.ok) throw new Error("Session unavailable");
        const payload = (await response.json()) as AdminUser;
        if (cancelled) return;
        setUser(payload);
        await loadEvents(token);
      } catch {
        if (!cancelled) {
          setUser({ email: "admin@sassoir.com", displayName: "Sassoir Admin", roles: ["Admin"] });
          setEvents(fallbackAdminEvents);
          setApiOnline(false);
        }
      }
    }

    void restoreSession();
    return () => {
      cancelled = true;
    };
  }, [loadEvents, token]);

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

      const payload = (await response.json()) as { token: string; email: string; displayName: string; roles: string[] };
      safeSetStorage("sassoir_admin_token", payload.token);
      setToken(payload.token);
      setUser({ email: payload.email, displayName: payload.displayName, roles: payload.roles });
      await loadEvents(payload.token);
    } catch (loginError) {
      setError(loginError instanceof Error ? loginError.message : "Could not sign in.");
    } finally {
      setLoading(false);
    }
  }

  function handleLogout() {
    safeRemoveStorage("sassoir_admin_token");
    setToken("");
    setUser(null);
    setEvents([]);
    resetDraft();
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
          </nav>
        </div>
        <nav className="sidebar-secondary">
          <button type="button"><ClipboardList aria-hidden="true" />Notifications</button>
          <button className={page === "settings" ? "active" : ""} type="button" onClick={() => navigateAdmin("/admin/settings")}><Settings aria-hidden="true" />Setup</button>
          <button type="button"><Users aria-hidden="true" />Profile</button>
          <button type="button"><Lock aria-hidden="true" />Change password</button>
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
        {page === "floor-plan" ? <FloorPlanAdminPage event={selectedEvent} token={token} /> : null}
        {page === "publish" ? <PublishPage events={events} saving={saving} onSetPublication={(eventId, status) => void setEventPublication(eventId, status)} /> : null}
        {page === "analytics" ? <AnalyticsPage events={events} /> : null}
        {page === "settings" ? <SettingsPage /> : null}
      </section>
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

  const loadGuests = useCallback(async () => {
    if (!token) return;

    setLoading(true);
    setError("");

    try {
      const [guestResponse, tableResponse] = await Promise.all([
        fetch(apiUrl(`/api/admin/events/${event.id}/guests`), { headers: { Authorization: `Bearer ${token}` } }),
        fetch(apiUrl(`/api/admin/events/${event.id}/tables`), { headers: { Authorization: `Bearer ${token}` } }),
      ]);

      if (!guestResponse.ok) throw new Error(await readError(guestResponse));
      if (!tableResponse.ok) throw new Error(await readError(tableResponse));

      setGuests((await guestResponse.json()) as AdminGuest[]);
      setTables((await tableResponse.json()) as AdminTable[]);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Could not load guests.");
    } finally {
      setLoading(false);
    }
  }, [event.id, token]);

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
      "First Name,Last Name,Display Name,Notes\nAntonella,Hitti,,Vegetarian meal\nKarim,Saab,Karim Saab,Needs aisle access\n",
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
      await loadGuests();
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
      await loadGuests();
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
      await loadGuests();
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
      await loadGuests();
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

  const activeGuests = guests.filter((guest) => guest.status === "Active");
  const seatingGuests = guests.filter((guest) => guest.status === "Active" || guest.status === "CheckedIn");
  const normalizedQuery = normalizeSearch(query);
  const visibleGuests = guests.filter((guest) => {
    const text = normalizeSearch(`${guest.firstName} ${guest.lastName} ${guest.displayName} ${guest.notes} ${guest.tableCode} ${guest.tableName}`);
    const matchesQuery = !normalizedQuery || text.includes(normalizedQuery);
    const matchesStatus = statusFilter === "All" || guest.status === statusFilter || (statusFilter === "Unassigned" && !guest.tableId && guest.status !== "Archived");
    const matchesTable = tableFilter === "All" || (tableFilter === "Unassigned" ? !guest.tableId : guest.tableId === tableFilter);
    return matchesQuery && matchesStatus && matchesTable;
  });
  const assignedGuests = seatingGuests.filter((guest) => guest.tableId).length;
  const unassignedGuests = seatingGuests.length - assignedGuests;
  const duplicateGuests = guests.filter((guest) => guest.isDuplicate).length;
  const selectedGuest = guests.find((guest) => guest.id === selectedGuestId);
  const formTitle = formMode === "create" ? "Create new guest" : formMode === "edit" ? "Edit guest" : "Guest details";
  const canSaveImport = importPreview ? importPreview.rows.some((row) => row.errors.length === 0 && !row.isDuplicate) : false;

  return (
    <section className="guest-manager">
      <div className="list-toolbar guest-toolbar">
        <label className="admin-search">
          <Search aria-hidden="true" />
          <input value={query} onChange={(formEvent) => setQuery(formEvent.target.value)} placeholder="Search guests" aria-label="Search guests" />
        </label>
        <select value={statusFilter} onChange={(formEvent) => setStatusFilter(formEvent.target.value)} aria-label="Filter guests by status">
          <option>Active</option>
          <option>All</option>
          <option>Unassigned</option>
          <option>Cancelled</option>
          <option>CheckedIn</option>
          <option>Archived</option>
        </select>
        <select value={tableFilter} onChange={(formEvent) => setTableFilter(formEvent.target.value)} aria-label="Filter guests by table">
          <option value="All">All tables</option>
          <option value="Unassigned">Unassigned</option>
          {tables.map((table) => <option key={table.id} value={table.id}>Table {table.number}</option>)}
        </select>
        <button className="secondary-button compact-button" type="button" onClick={() => void exportGuests()} disabled={saving || guests.length === 0}><Download aria-hidden="true" />Export</button>
        <button className="secondary-button compact-button" type="button" onClick={() => setShowImportDialog(true)} disabled={saving}>
          <Upload aria-hidden="true" />
          Import
        </button>
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
                    <th>Notes</th>
                    <th>Review</th>
                  </tr>
                </thead>
                <tbody>
                  {importRows.map((row) => (
                    <tr key={`${row.rowNumber}-${row.displayName}`}>
                      <td data-label="Row">{row.rowNumber}</td>
                      <td data-label="Guest"><strong>{row.displayName || "Missing name"}</strong><span>{[row.firstName, row.lastName].filter(Boolean).join(" ") || "No legal name set"}</span></td>
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
                <th>Guest</th>
                <th>Status</th>
                <th>Assigned Table</th>
                <th>Notes</th>
                <th>Flags</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {visibleGuests.map((guest) => {
                const table = tables.find((item) => item.id === guest.tableId);
                return (
                  <tr key={guest.id}>
                    <td data-label="Guest">
                      <button className="guest-name-button" type="button" onClick={() => selectGuest(guest, "details")}>
                        <strong>{guest.displayName}</strong>
                        <span>{[guest.firstName, guest.lastName].filter(Boolean).join(" ") || "No legal name set"}</span>
                      </button>
                    </td>
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
          {!loading && visibleGuests.length === 0 ? <p className="empty-state">No guests match this view. Create a guest or adjust the filters.</p> : null}
        </div>
        {loading ? <p className="admin-muted">Loading guests...</p> : null}
      </section>

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
    </section>
  );
}

function emptyGuestDraft(): AdminGuestDraft {
  return {
    firstName: "",
    lastName: "",
    displayName: "",
    notes: "",
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
    tableId: guest.tableId ?? "",
    status: guest.status ?? "Active",
  };
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

function FloorPlanAdminPage({ event, token }: { event: AdminEvent; token: string }) {
  const [tables, setTables] = useState<AdminTable[]>([]);
  const [guests, setGuests] = useState<AdminGuest[]>([]);
  const [floorObjects, setFloorObjects] = useState<FloorObject[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [showTableForm, setShowTableForm] = useState(false);
  const [error, setError] = useState("");
  const [warning, setWarning] = useState("");
  const [tableDraft, setTableDraft] = useState({ name: "", number: "", maximumCapacity: 10 });

  const loadFloorPlan = useCallback(async () => {
    if (!token) return;

    setLoading(true);
    setError("");

    try {
      const [tableResponse, guestResponse, floorPlanResponse] = await Promise.all([
        fetch(apiUrl(`/api/admin/events/${event.id}/tables`), { headers: { Authorization: `Bearer ${token}` } }),
        fetch(apiUrl(`/api/admin/events/${event.id}/guests`), { headers: { Authorization: `Bearer ${token}` } }),
        fetch(apiUrl(`/api/admin/events/${event.id}/floor-plan`), { headers: { Authorization: `Bearer ${token}` } }),
      ]);

      if (!tableResponse.ok) throw new Error(await readError(tableResponse));
      if (!guestResponse.ok) throw new Error(await readError(guestResponse));
      if (!floorPlanResponse.ok) throw new Error(await readError(floorPlanResponse));

      const tablePayload = (await tableResponse.json()) as AdminTable[];
      const guestPayload = (await guestResponse.json()) as AdminGuest[];
      const floorPlanPayload = await floorPlanResponse.json();

      setTables(tablePayload);
      setGuests(guestPayload);
      setFloorObjects(withTableFloorObjects(toFloorObjects(floorPlanPayload?.objects), tablePayload));
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Could not load floor plan.");
    } finally {
      setLoading(false);
    }
  }, [event.id, token]);

  useEffect(() => {
    void loadFloorPlan();
  }, [loadFloorPlan]);

  async function createTable(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    if (!token) return;

    setSaving(true);
    setError("");

    try {
      const response = await fetch(apiUrl(`/api/admin/events/${event.id}/tables`), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(tableDraft),
      });

      if (!response.ok) throw new Error(await readError(response));

      setTableDraft({ name: "", number: "", maximumCapacity: 10 });
      setShowTableForm(false);
      await loadFloorPlan();
    } catch (createError) {
      setError(createError instanceof Error ? createError.message : "Could not create table.");
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

  async function assignGuestToTable(guestId: string, tableId: string | null | undefined) {
    if (!token || !tableId) {
      setWarning("This table needs to be saved before guests can be assigned to it.");
      return;
    }

    const guest = guests.find((item) => item.id === guestId);
    const table = tables.find((item) => item.id === tableId);
    if (guest?.tableId !== tableId && table && table.assignedGuestCount >= table.maximumCapacity) {
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
      await loadFloorPlan();
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
      setFloorObjects(withTableFloorObjects(toFloorObjects(payload?.objects), tables));
      setWarning("Floor plan saved.");
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Could not save floor plan.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <section className="admin-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Tables</p>
            <h2>{event.name}</h2>
          </div>
          <button className="primary-button compact-button" type="button" onClick={() => setShowTableForm((current) => !current)}><Table2 aria-hidden="true" />Add Table</button>
        </div>

        {error ? <p className="form-error">{error}</p> : null}

        {showTableForm ? (
          <form className="event-form" onSubmit={createTable}>
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
                Assigned Guest Count
                <input value="0" readOnly />
              </label>
            </div>
            <div className="form-actions">
              <button className="primary-button compact-button" type="submit" disabled={saving}>{saving ? "Saving..." : "Create Table"}</button>
            </div>
          </form>
        ) : null}

        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Number</th>
                <th>Maximum Capacity</th>
                <th>Assigned Guests</th>
              </tr>
            </thead>
            <tbody>
              {tables.map((table) => (
                <tr key={table.id}>
                  <td data-label="Name"><strong>{table.name}</strong></td>
                  <td data-label="Number">{table.number}</td>
                  <td data-label="Maximum Capacity">{table.maximumCapacity}</td>
                  <td data-label="Assigned Guests">{table.assignedGuestCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {!loading && tables.length === 0 ? <p className="empty-state">No tables yet. Add tables before designing the floor plan.</p> : null}
        </div>
      </section>

      <section className="admin-panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">Design</p>
            <h2>Floor plan designer</h2>
          </div>
          <button className="primary-button compact-button" type="button" onClick={() => void saveFloorPlan()} disabled={saving}><Map aria-hidden="true" />{saving ? "Saving..." : "Save Floor Plan"}</button>
        </div>

        {warning ? <p className="designer-warning">{warning}</p> : null}

        <div className="designer-layout">
          <div>
            <div className="designer-section-tools" aria-label="Add floor plan section">
              {floorSectionTemplates.map((template) => (
                <button className="secondary-button compact-button" type="button" key={template.type} onClick={() => addSection(template)}><Plus aria-hidden="true" />{template.label}</button>
              ))}
            </div>

            <div className="floor-designer-canvas" onDragOver={(dragEvent) => dragEvent.preventDefault()} onDrop={moveObject} aria-label="Drag tables and venue sections to arrange the floor plan">
              {floorObjects.map((object) => (
                <div
                  className={`floor-designer-object ${object.type} ${object.shape}`}
                  draggable
                  key={object.id}
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
                  <span>{object.type === "table" ? `Table ${object.tableCode ?? object.label}` : object.label}</span>
                </div>
              ))}
            </div>
          </div>

          <aside className="guest-assignment-panel" aria-label="Guests to assign">
            <div>
              <p className="eyebrow">Guests</p>
              <h3>Drag to a table</h3>
            </div>
            <div className="guest-chip-list">
              {guests.map((guest) => (
                <button
                  className={`guest-chip ${guest.tableId ? "assigned" : ""}`}
                  draggable
                  key={guest.id}
                  type="button"
                  onDragStart={(dragEvent) => dragEvent.dataTransfer.setData("guest-id", guest.id)}
                >
                  <strong>{guest.displayName}</strong>
                  <span>{guest.tableCode ? `Table ${guest.tableCode}` : "Unassigned"}</span>
                </button>
              ))}
            </div>
          </aside>
        </div>
      </section>
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

function EventSetupPage({ event }: { event: AdminEvent }) {
  const published = eventStatusText(event.status).toLowerCase() === "published";
  const publicUrl = getEventPublicUrl(event);

  return (
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
  const [email, setEmail] = useState("admin@sassoir.com");
  const [password, setPassword] = useState("P@$$w0rd");

  async function submitLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await onLogin(email, password);
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
          {error ? <p className="form-error" role="alert">{error}</p> : null}
          <p className="login-hint">Testing account: admin@sassoir.com</p>
        </form>
      </section>
    </main>
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
        editorEvent ? <FloorPlanAdminPage event={editorEvent} token={token} /> : <UnsavedEventSection sectionName="Floor Plan" />
      ) : null}

      {activeSection.name === "Setup" ? (
        editorEvent ? <EventSetupPage event={editorEvent} /> : <UnsavedEventSection sectionName="Setup" />
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
