CREATE TABLE IF NOT EXISTS charts (
  chart_id TEXT PRIMARY KEY,
  owner_id TEXT NOT NULL,
  title TEXT NOT NULL,
  creator TEXT NOT NULL,
  description TEXT NOT NULL,
  difficulty TEXT NOT NULL,
  tags_json TEXT NOT NULL,
  version_id TEXT NOT NULL,
  package_path TEXT NOT NULL,
  size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
  sha256 TEXT NOT NULL,
  format_version TEXT NOT NULL,
  chart_data_path TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  downloads INTEGER NOT NULL DEFAULT 0 CHECK (downloads >= 0),
  status TEXT NOT NULL DEFAULT 'public',
  ranked_state TEXT NOT NULL DEFAULT 'unranked'
);

CREATE TABLE IF NOT EXISTS uploads (
  upload_id TEXT PRIMARY KEY,
  owner_id TEXT NOT NULL,
  stage_path TEXT NOT NULL,
  status TEXT NOT NULL,
  error TEXT,
  chart_id TEXT
);

CREATE TABLE IF NOT EXISTS nominations (
  nomination_id TEXT PRIMARY KEY,
  chart_id TEXT NOT NULL REFERENCES charts(chart_id),
  player_id TEXT NOT NULL,
  rationale TEXT NOT NULL,
  status TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS review_audit (
  audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
  nomination_id TEXT NOT NULL REFERENCES nominations(nomination_id),
  reviewer_id TEXT NOT NULL,
  decision TEXT NOT NULL,
  rationale TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS chart_reports (
  report_id TEXT PRIMARY KEY,
  chart_id TEXT NOT NULL REFERENCES charts(chart_id),
  reporter_id TEXT NOT NULL,
  reason TEXT NOT NULL,
  status TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS moderation_audit (
  audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
  chart_id TEXT NOT NULL REFERENCES charts(chart_id),
  moderator_id TEXT NOT NULL,
  action TEXT NOT NULL,
  rationale TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ranked_results (
  result_id TEXT PRIMARY KEY,
  player_id TEXT NOT NULL,
  chart_id TEXT NOT NULL REFERENCES charts(chart_id),
  version_id TEXT NOT NULL,
  points REAL NOT NULL CHECK (points >= 0),
  algorithm_version TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_charts_public_updated
  ON charts(status, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_charts_public_popularity
  ON charts(status, downloads DESC, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_charts_owner
  ON charts(owner_id);
CREATE INDEX IF NOT EXISTS ix_uploads_owner_status
  ON uploads(owner_id, status);
CREATE INDEX IF NOT EXISTS ix_nominations_status_created
  ON nominations(status, created_at);
CREATE INDEX IF NOT EXISTS ix_chart_reports_status_created
  ON chart_reports(status, created_at);
CREATE INDEX IF NOT EXISTS ix_ranked_results_player
  ON ranked_results(player_id, points DESC);
