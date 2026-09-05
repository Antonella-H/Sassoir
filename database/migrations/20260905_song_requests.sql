create table if not exists song_requests (
  id uuid primary key default gen_random_uuid(),
  event_id uuid not null references events(id) on delete cascade,
  guest_id uuid not null references guests(id) on delete cascade,
  song_title varchar(200) not null,
  created_at timestamptz not null default now()
);

create index if not exists ix_song_requests_event_created
  on song_requests(event_id, created_at desc);
