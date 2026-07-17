create index if not exists ix_floor_plan_objects_floor_plan_visible_z
  on floor_plan_objects(floor_plan_id, is_visible, z_index);
