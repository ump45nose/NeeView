-- 首版状态结构；版本升级必须新增脚本，不能只修改本地数据库。
CREATE TABLE IF NOT EXISTS schema_version(version INTEGER PRIMARY KEY);
INSERT OR IGNORE INTO schema_version VALUES(1);
CREATE TABLE IF NOT EXISTS identities(kind TEXT NOT NULL, locator TEXT NOT NULL, id TEXT NOT NULL, PRIMARY KEY(kind,locator));
CREATE TABLE IF NOT EXISTS reading_states(book_id TEXT PRIMARY KEY, data TEXT NOT NULL, accessed TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS bookmarks(id TEXT PRIMARY KEY, parent_id TEXT, sort_order INTEGER NOT NULL, data TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS file_operations(id TEXT PRIMARY KEY, data TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS import_batches(id TEXT PRIMARY KEY, source TEXT NOT NULL, imported TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_bookmark_parent ON bookmarks(parent_id,sort_order);
