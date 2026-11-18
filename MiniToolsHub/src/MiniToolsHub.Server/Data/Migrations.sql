CREATE TABLE IF NOT EXISTS releases (
  id INTEGER PRIMARY KEY, app_id TEXT NOT NULL, version TEXT NOT NULL, channel TEXT NOT NULL,
  file_name TEXT NOT NULL, size_bytes INTEGER NOT NULL, sha256 TEXT NOT NULL,
  manifest_json TEXT NOT NULL, published_at TEXT NOT NULL, is_yanked INTEGER NOT NULL DEFAULT 0,
  UNIQUE(app_id, version));
CREATE TABLE IF NOT EXISTS download_counts (
  app_id TEXT NOT NULL, version TEXT NOT NULL, day TEXT NOT NULL, count INTEGER NOT NULL,
  PRIMARY KEY(app_id, version, day));
