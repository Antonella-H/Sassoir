insert into organizations (id, name, slug)
values ('1f895c73-7f41-4d75-bf37-8bbbdbe062d4', 'Demo Events', 'demo-events')
on conflict do nothing;

insert into events (id, organization_id, name, slug, event_type, subtitle, date_label, venue_name, venue_address, status, is_public, published_at)
values (
  '2eb2f4b0-67c8-4d99-a91f-caa1007084e8',
  '1f895c73-7f41-4d75-bf37-8bbbdbe062d4',
  'Licha & Roula''s Wedding',
  'lichaa-and-roula',
  'Wedding',
  'Together with their families, they welcome you to an evening of love, dinner, and dancing.',
  'Saturday, August 22',
  'The Olive Garden Venue',
  'Beirut, Lebanon',
  'Published',
  true,
  now()
)
on conflict do nothing;

insert into event_themes (event_id, logo_text, hero_text, primary_color, secondary_color, background_color, text_color, welcome_title, search_input_label, search_placeholder, hero_image_url)
values (
  '2eb2f4b0-67c8-4d99-a91f-caa1007084e8',
  'L & R',
  'An elegant garden celebration under soft summer lights.',
  '#D8CFBC',
  '#565449',
  '#FFFBF4',
  '#11120D',
  'Welcome to Licha & Roula''s wedding',
  'Search by name',
  'Search by name',
  '/sassoir-logo-sentence.png'
)
on conflict do nothing;

insert into event_tables (id, event_id, name, code, shape, capacity)
values
  ('350399c8-0d12-4ef2-9dc5-6c283e8ef8bb', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', 'Cedar Grove', '8', 'Round', 8),
  ('47dd3101-877c-469c-bd61-1e052516d3f9', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', 'Jasmine Court', '10', 'Round', 8),
  ('499c0708-1101-417f-b95a-0cdac1990506', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', 'The Olive Garden', '12', 'Round', 10),
  ('9d6783ba-7f99-4826-b9f4-43498795b7f2', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', 'Terrace', '14', 'Round', 8)
on conflict do nothing;

insert into guests (id, event_id, table_id, first_name, last_name, display_name, normalized_search_name, public_token, group_label, seat_number, directions)
values
  ('29a84b1f-0ae4-4f31-9df6-0918f26f3d78', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', '499c0708-1101-417f-b95a-0cdac1990506', 'Sarah', 'Lichaa', 'Sarah Lichaa', 'sarah lichaa', 'guest-sarah-lichaa', 'Lichaa Family', '4', 'Near the dance floor, with a clear view of the stage.'),
  ('c67681f8-82e6-4142-b204-64e26a0e63e4', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', '499c0708-1101-417f-b95a-0cdac1990506', 'Roula', 'Lichaa', 'Roula Lichaa', 'roula lichaa', 'guest-roula-lichaa', 'Couple''s Table', '1', 'Near the dance floor, with a clear view of the stage.'),
  ('a4f451b7-37d8-498d-926c-6a5b8ffbbbd7', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', '499c0708-1101-417f-b95a-0cdac1990506', 'Maya', 'K.', 'Maya K.', 'maya k', 'guest-maya-k', 'Friends of Roula', '5', 'Near the dance floor, with a clear view of the stage.'),
  ('7816fa19-2877-4de8-bdde-769739e5f9e9', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', '350399c8-0d12-4ef2-9dc5-6c283e8ef8bb', 'Antonella', 'Hitti', 'Antonella Hitti', 'antonella hitti', 'guest-antonella-hitti', 'Hitti Family', '2', 'Close to the garden entrance.'),
  ('6904d89b-6182-4dbb-9b4c-3e7aa1ec2ff7', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', '47dd3101-877c-469c-bd61-1e052516d3f9', 'Antonella', 'H.', 'Antonella H.', 'antonella h', 'guest-antonella-h', 'Guest of Roula', null, 'Beside the left garden aisle.'),
  ('16f35526-7734-4794-8ad1-f78db0874368', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', '9d6783ba-7f99-4826-b9f4-43498795b7f2', 'Karim', 'Haddad', 'Karim Haddad', 'karim haddad', 'guest-karim-h', 'Friends of Lichaa', null, 'Near the lower terrace aisle.')
on conflict do nothing;

insert into guest_search_aliases (guest_id, alias, normalized_alias)
select guest_id, alias, normalized_alias
from (values
  ('29a84b1f-0ae4-4f31-9df6-0918f26f3d78'::uuid, 'sarah', 'sarah'),
  ('29a84b1f-0ae4-4f31-9df6-0918f26f3d78'::uuid, 'سارة لحاء', 'ساره لحاء'),
  ('c67681f8-82e6-4142-b204-64e26a0e63e4'::uuid, 'roula', 'roula'),
  ('c67681f8-82e6-4142-b204-64e26a0e63e4'::uuid, 'رولا', 'رولا'),
  ('a4f451b7-37d8-498d-926c-6a5b8ffbbbd7'::uuid, 'maya', 'maya'),
  ('a4f451b7-37d8-498d-926c-6a5b8ffbbbd7'::uuid, 'مايا', 'مايا'),
  ('7816fa19-2877-4de8-bdde-769739e5f9e9'::uuid, 'antonella', 'antonella'),
  ('7816fa19-2877-4de8-bdde-769739e5f9e9'::uuid, 'انطونيلا', 'انطونيلا'),
  ('6904d89b-6182-4dbb-9b4c-3e7aa1ec2ff7'::uuid, 'antonella guest of roula', 'antonella guest of roula'),
  ('16f35526-7734-4794-8ad1-f78db0874368'::uuid, 'karim', 'karim')
) as aliases(guest_id, alias, normalized_alias);

insert into floor_plans (id, event_id, name, canvas_aspect_ratio, is_active)
values ('62bf61df-786b-4b8d-8855-5d5af6fb3647', '2eb2f4b0-67c8-4d99-a91f-caa1007084e8', 'Garden Ballroom', 1.14, true)
on conflict do nothing;

insert into floor_plan_objects (id, floor_plan_id, linked_table_id, object_type, label, x, y, width, height, shape, z_index)
values
  ('stage', '62bf61df-786b-4b8d-8855-5d5af6fb3647', null, 'stage', 'Stage', 0.35, 0.06, 0.38, 0.11, 'rect', 1),
  ('table-8', '62bf61df-786b-4b8d-8855-5d5af6fb3647', '350399c8-0d12-4ef2-9dc5-6c283e8ef8bb', 'table', 'Table 8', 0.13, 0.25, 0.15, 0.15, 'round', 2),
  ('table-10', '62bf61df-786b-4b8d-8855-5d5af6fb3647', '47dd3101-877c-469c-bd61-1e052516d3f9', 'table', 'Table 10', 0.13, 0.53, 0.16, 0.16, 'round', 2),
  ('dance', '62bf61df-786b-4b8d-8855-5d5af6fb3647', null, 'dance', 'Dance Floor', 0.42, 0.40, 0.28, 0.25, 'rect', 1),
  ('bar', '62bf61df-786b-4b8d-8855-5d5af6fb3647', null, 'bar', 'Bar', 0.82, 0.27, 0.13, 0.25, 'rect', 1),
  ('table-12', '62bf61df-786b-4b8d-8855-5d5af6fb3647', '499c0708-1101-417f-b95a-0cdac1990506', 'table', 'Table 12', 0.76, 0.56, 0.15, 0.15, 'round', 2),
  ('restroom', '62bf61df-786b-4b8d-8855-5d5af6fb3647', null, 'restroom', 'Toilets', 0.83, 0.69, 0.13, 0.12, 'rect', 1),
  ('table-14', '62bf61df-786b-4b8d-8855-5d5af6fb3647', '9d6783ba-7f99-4826-b9f4-43498795b7f2', 'table', 'Table 14', 0.75, 0.82, 0.16, 0.16, 'round', 2),
  ('entrance', '62bf61df-786b-4b8d-8855-5d5af6fb3647', null, 'entrance', 'Entrance', 0.10, 0.83, 0.15, 0.09, 'rect', 1)
on conflict do nothing;
