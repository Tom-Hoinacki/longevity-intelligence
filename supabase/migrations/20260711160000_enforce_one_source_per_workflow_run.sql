-- Workflow version 1 has exactly one normalized source record per run.

create unique index workflow_source_records_workflow_run_unique_idx
on workflow.source_records (workflow_run_id);

comment on index workflow.workflow_source_records_workflow_run_unique_idx is
  'Workflow version 1 permits exactly one normalized source record per workflow run.';
