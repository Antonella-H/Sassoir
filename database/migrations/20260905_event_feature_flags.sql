alter table events add column if not exists enable_floor_plan boolean not null default true;
alter table events add column if not exists enable_table_companions boolean not null default true;
alter table events add column if not exists enable_guest_messages boolean not null default true;
alter table events add column if not exists enable_song_requests boolean not null default true;
