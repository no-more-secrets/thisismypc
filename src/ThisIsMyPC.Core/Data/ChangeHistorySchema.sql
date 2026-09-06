-- Reference schema for change_history database (v3)
-- Actual SQL is embedded as constants in ChangeHistoryRepository.cs for NativeAOT compatibility
-- v2: enforcement_json (SettingEnforcement as camelCase JSON) so history undo/redo can
--     route through the enforcement executor

CREATE TABLE IF NOT EXISTS change_history (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  module_id TEXT NOT NULL,
  setting_id TEXT NOT NULL,
  display_name TEXT NOT NULL,
  system_location TEXT NOT NULL,
  before_value TEXT,
  after_value TEXT,
  before_display TEXT,
  after_display TEXT,
  value_type TEXT NOT NULL,
  category TEXT NOT NULL,
  group_id TEXT,
  applied_at TEXT NOT NULL,
  reverted_at TEXT,
  reverted_by_entry_id INTEGER,
  redo_of_entry_id INTEGER,
  enforcement_json TEXT,
  owner_attempt_id TEXT,
  target_user_sid TEXT,
  journal_outcome TEXT,
  journal_detail TEXT
);

CREATE TABLE IF NOT EXISTS schema_version (
  version INTEGER PRIMARY KEY,
  applied_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_change_history_applied_at ON change_history(applied_at DESC);
CREATE INDEX IF NOT EXISTS ix_change_history_module_id ON change_history(module_id);

-- v3 receipt ledger survives clearing visible history.

CREATE UNIQUE INDEX ix_history_owner_attempt ON change_history(owner_attempt_id) WHERE owner_attempt_id IS NOT NULL;
CREATE TABLE owner_journal_imports (
    attempt_id TEXT PRIMARY KEY NOT NULL,
    payload_json TEXT NOT NULL,
    transaction_id TEXT NOT NULL,
    history_entry_id INTEGER NOT NULL,
    imported_at TEXT NOT NULL
);
