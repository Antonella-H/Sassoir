alter table events add column if not exists seating_assignment_mode text not null default 'table';
