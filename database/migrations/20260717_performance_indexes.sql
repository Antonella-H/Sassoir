create extension if not exists pg_trgm;

create index if not exists ix_events_slug_status on events(slug, status);
create index if not exists ix_guests_event_search on guests(event_id, normalized_search_name);
create index if not exists ix_guests_normalized_search_name_trgm on guests using gin (normalized_search_name gin_trgm_ops);
create index if not exists ix_guests_event_status_table on guests(event_id, status, table_id);
create index if not exists ix_guests_event_public_token on guests(event_id, public_token);
create index if not exists ix_guest_aliases_guest_alias on guest_search_aliases(guest_id, normalized_alias);
create index if not exists ix_guest_aliases_normalized_alias_trgm on guest_search_aliases using gin (normalized_alias gin_trgm_ops);
create index if not exists ix_event_tables_event on event_tables(event_id);
create index if not exists ix_floor_plans_event_active on floor_plans(event_id, is_active);
create index if not exists ix_floor_plan_objects_floor_plan_table on floor_plan_objects(floor_plan_id, linked_table_id);
create index if not exists ix_guest_messages_event_created on guest_messages(event_id, created_at desc);
create index if not exists ix_search_metrics_event_created on search_metrics(event_id, created_at);
