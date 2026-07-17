create extension if not exists pgcrypto;
create extension if not exists pg_trgm;

create table organizations (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  slug text not null unique,
  status text not null default 'Active',
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table events (
  id uuid primary key default gen_random_uuid(),
  organization_id uuid not null references organizations(id) on delete cascade,
  name text not null,
  slug text not null unique,
  event_type text not null default 'Wedding',
  subtitle text not null default '',
  description text not null default '',
  date_label text not null default '',
  venue_name text not null default '',
  venue_address text not null default '',
  status text not null default 'Draft',
  is_public boolean not null default false,
  published_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table event_themes (
  id uuid primary key default gen_random_uuid(),
  event_id uuid not null unique references events(id) on delete cascade,
  logo_text text not null default '',
  hero_text text not null default '',
  primary_color text not null default '#D8CFBC',
  secondary_color text not null default '#565449',
  background_color text not null default '#FFFBF4',
  text_color text not null default '#11120D',
  welcome_title text not null default '',
  search_input_label text not null default 'Search by name',
  search_placeholder text not null default 'Search by name',
  hero_image_url text,
  logo_url text,
  updated_at timestamptz not null default now()
);

create table guest_groups (
  id uuid primary key default gen_random_uuid(),
  event_id uuid not null references events(id) on delete cascade,
  name text not null,
  description text
);

create table event_tables (
  id uuid primary key default gen_random_uuid(),
  event_id uuid not null references events(id) on delete cascade,
  name text not null,
  code text not null,
  shape text not null default 'Round',
  capacity integer not null check (capacity > 0),
  notes text,
  zone_name text,
  floor_plan_x numeric(8, 6),
  floor_plan_y numeric(8, 6),
  floor_plan_width numeric(8, 6),
  floor_plan_height numeric(8, 6),
  rotation numeric(8, 3) not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique(event_id, code)
);

create table guests (
  id uuid primary key default gen_random_uuid(),
  event_id uuid not null references events(id) on delete cascade,
  guest_group_id uuid references guest_groups(id) on delete set null,
  table_id uuid references event_tables(id) on delete set null,
  first_name text not null default '',
  last_name text not null default '',
  display_name text not null,
  normalized_search_name text not null,
  public_token text not null unique,
  group_label text not null default '',
  seat_number text,
  directions text not null default '',
  email text,
  phone text,
  notes text,
  person_count integer not null default 1,
  status text not null default 'Active',
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table guest_search_aliases (
  id uuid primary key default gen_random_uuid(),
  guest_id uuid not null references guests(id) on delete cascade,
  alias text not null,
  normalized_alias text not null
);

create table floor_plans (
  id uuid primary key default gen_random_uuid(),
  event_id uuid not null references events(id) on delete cascade,
  name text not null,
  canvas_aspect_ratio numeric(8, 4) not null default 1.14,
  version integer not null default 1,
  is_active boolean not null default true,
  created_at timestamptz not null default now()
);

create table floor_plan_objects (
  id text primary key,
  floor_plan_id uuid not null references floor_plans(id) on delete cascade,
  linked_table_id uuid references event_tables(id) on delete set null,
  object_type text not null,
  label text not null,
  x numeric(8, 6) not null check (x >= 0 and x <= 1),
  y numeric(8, 6) not null check (y >= 0 and y <= 1),
  width numeric(8, 6) not null check (width > 0 and width <= 1),
  height numeric(8, 6) not null check (height > 0 and height <= 1),
  rotation numeric(8, 3) not null default 0,
  shape text not null default 'rect',
  z_index integer not null default 0,
  is_visible boolean not null default true
);

create table guest_messages (
  id uuid primary key default gen_random_uuid(),
  event_id uuid not null references events(id) on delete cascade,
  guest_id uuid not null references guests(id) on delete cascade,
  message text not null,
  created_at timestamptz not null default now()
);

create table search_metrics (
  id uuid primary key default gen_random_uuid(),
  event_id uuid not null references events(id) on delete cascade,
  normalized_query text not null,
  successful boolean not null,
  created_at timestamptz not null default now()
);

create table contact_submissions (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  email text not null,
  message text not null,
  submitted_at_utc timestamptz not null default now()
);

create table app_users (
  id uuid primary key default gen_random_uuid(),
  organization_id uuid references organizations(id) on delete set null,
  first_name text not null,
  last_name text not null,
  email text not null unique,
  password_hash text not null,
  status text not null default 'Active',
  is_super_admin boolean not null default false,
  last_login_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table roles (
  id uuid primary key default gen_random_uuid(),
  name text not null unique
);

create table user_roles (
  user_id uuid not null references app_users(id) on delete cascade,
  role_id uuid not null references roles(id) on delete cascade,
  primary key (user_id, role_id)
);

create index ix_events_organization_id on events(organization_id);
create index ix_events_slug_status on events(slug, status);
create index ix_guests_event_search on guests(event_id, normalized_search_name);
create index ix_guests_normalized_search_name_trgm on guests using gin (normalized_search_name gin_trgm_ops);
create index ix_guests_event_status_table on guests(event_id, status, table_id);
create index ix_guests_event_public_token on guests(event_id, public_token);
create index ix_guest_aliases_guest_alias on guest_search_aliases(guest_id, normalized_alias);
create index ix_guest_aliases_normalized_alias_trgm on guest_search_aliases using gin (normalized_alias gin_trgm_ops);
create index ix_event_tables_event on event_tables(event_id);
create index ix_floor_plans_event_active on floor_plans(event_id, is_active);
create index ix_floor_plan_objects_floor_plan_table on floor_plan_objects(floor_plan_id, linked_table_id);
create index ix_floor_plan_objects_floor_plan_visible_z on floor_plan_objects(floor_plan_id, is_visible, z_index);
create index ix_guest_messages_event_created on guest_messages(event_id, created_at desc);
create index ix_search_metrics_event_created on search_metrics(event_id, created_at);
create index ix_contact_submissions_submitted_at_utc on contact_submissions(submitted_at_utc desc);
create index ix_app_users_email on app_users(email);
