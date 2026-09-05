create extension if not exists pgcrypto;

alter table events add column if not exists dj_access_token text not null default '';

update events
  set dj_access_token = replace(replace(trim(trailing '=' from encode(gen_random_bytes(24), 'base64')), '+', '-'), '/', '_')
  where dj_access_token = '';
