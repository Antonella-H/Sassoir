create table if not exists contact_submissions (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  email text not null,
  message text not null,
  submitted_at_utc timestamptz not null default now()
);

create index if not exists ix_contact_submissions_submitted_at_utc
  on contact_submissions(submitted_at_utc desc);
