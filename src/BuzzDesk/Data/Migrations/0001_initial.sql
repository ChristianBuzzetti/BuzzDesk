-- BuzzDesk · migration 0001 · initial schema
--
-- Conventions
--   * STRICT tables (type checking), INTEGER PRIMARY KEY (rowid alias).
--   * Instants: *_utc TEXT 'YYYY-MM-DDTHH:MM:SSZ' (UTC), checked with GLOB.
--   * Calendar dates: *_date TEXT 'YYYY-MM-DD' (a day in the user's time zone, no zone attached: a due date is a day,
--     not an instant). Times of day: *_time TEXT 'HH:MM'.
--   * Soft delete with deleted_utc (trash and undo); child rows of a soft-deleted row stay.
--   * Tags use one junction table per entity, with real foreign keys (no polymorphic tagging).
--   * Every foreign key column is indexed (SQLite does not do it by itself).
-- The migrator runs this file inside one transaction and records it in schema_version.

CREATE TABLE app_meta (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
) STRICT, WITHOUT ROWID;

CREATE TABLE section (                       -- areas (Work, Personal) and projects (child of an area)
  id           INTEGER PRIMARY KEY,
  parent_id    INTEGER REFERENCES section(id) ON DELETE RESTRICT,
  name         TEXT NOT NULL CHECK (length(trim(name)) > 0),
  kind         TEXT NOT NULL CHECK (kind IN ('area','project')),
  color        TEXT CHECK (color IS NULL OR color GLOB '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'),
  sort_order   INTEGER NOT NULL DEFAULT 0,
  archived_utc TEXT CHECK (archived_utc IS NULL OR archived_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  created_utc  TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  CHECK ((kind = 'area') = (parent_id IS NULL))
) STRICT;
CREATE UNIQUE INDEX ux_section_name ON section(coalesce(parent_id, 0), name COLLATE NOCASE);
CREATE INDEX ix_section_parent ON section(parent_id);

CREATE TABLE tag (
  id    INTEGER PRIMARY KEY,
  name  TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK (length(trim(name)) > 0),
  color TEXT CHECK (color IS NULL OR color GLOB '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]')
) STRICT;

CREATE TABLE person (                         -- "waiting on…"
  id          INTEGER PRIMARY KEY,
  name        TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK (length(trim(name)) > 0),
  email       TEXT,
  notes       TEXT,
  created_utc TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z')
) STRICT;

CREATE TABLE recurrence (                     -- one series; every instance is a task row pointing here
  id            INTEGER PRIMARY KEY,
  freq          TEXT NOT NULL CHECK (freq IN ('daily','weekly','monthly','last_business_day')),
  interval      INTEGER NOT NULL DEFAULT 1 CHECK (interval BETWEEN 1 AND 365),
  weekdays_mask INTEGER CHECK (weekdays_mask BETWEEN 1 AND 127),       -- weekly: bit0 = Monday … bit6 = Sunday
  month_day     INTEGER CHECK (month_day BETWEEN 1 AND 31),           -- monthly (31 = last day of the month)
  until_date    TEXT CHECK (until_date IS NULL OR until_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
  created_utc   TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  CHECK (freq = 'weekly'  OR weekdays_mask IS NULL),
  CHECK (freq = 'monthly' OR month_day IS NULL)
) STRICT;

CREATE TABLE topic (                          -- follow-up of a matter over time
  id                   INTEGER PRIMARY KEY,
  section_id           INTEGER REFERENCES section(id) ON DELETE SET NULL,
  title                TEXT NOT NULL CHECK (length(trim(title)) > 0),
  description          TEXT,
  status               TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active','waiting','paused','closed')),
  waiting_on_person_id INTEGER REFERENCES person(id) ON DELETE SET NULL,
  waiting_since_utc    TEXT CHECK (waiting_since_utc IS NULL OR waiting_since_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  next_review_date     TEXT CHECK (next_review_date IS NULL OR next_review_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
  created_utc          TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  updated_utc          TEXT NOT NULL CHECK (updated_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  closed_utc           TEXT CHECK (closed_utc IS NULL OR closed_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  deleted_utc          TEXT CHECK (deleted_utc IS NULL OR deleted_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  CHECK (status <> 'waiting' OR waiting_on_person_id IS NOT NULL),
  CHECK ((status = 'closed') = (closed_utc IS NOT NULL))
) STRICT;
CREATE INDEX ix_topic_section ON topic(section_id);
CREATE INDEX ix_topic_person  ON topic(waiting_on_person_id);
CREATE INDEX ix_topic_review  ON topic(next_review_date) WHERE status <> 'closed' AND deleted_utc IS NULL;
CREATE INDEX ix_topic_status  ON topic(status, updated_utc) WHERE deleted_utc IS NULL;

CREATE TABLE task (
  id             INTEGER PRIMARY KEY,
  section_id     INTEGER REFERENCES section(id) ON DELETE SET NULL,
  parent_task_id INTEGER REFERENCES task(id) ON DELETE CASCADE,          -- subtasks (one level in the UI)
  topic_id       INTEGER REFERENCES topic(id) ON DELETE SET NULL,
  recurrence_id  INTEGER REFERENCES recurrence(id) ON DELETE SET NULL,
  title          TEXT NOT NULL CHECK (length(trim(title)) > 0),
  notes          TEXT,
  priority       INTEGER NOT NULL DEFAULT 0 CHECK (priority BETWEEN 0 AND 3),   -- 0 none, 1 low, 2 medium, 3 high
  due_date       TEXT CHECK (due_date IS NULL OR due_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
  due_time       TEXT CHECK (due_time IS NULL OR due_time GLOB '[0-2][0-9]:[0-5][0-9]'),
  status         TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open','done','cancelled')),
  completed_utc  TEXT CHECK (completed_utc IS NULL OR completed_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  postpone_count INTEGER NOT NULL DEFAULT 0 CHECK (postpone_count >= 0),
  sort_order     INTEGER NOT NULL DEFAULT 0,
  created_utc    TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  updated_utc    TEXT NOT NULL CHECK (updated_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  deleted_utc    TEXT CHECK (deleted_utc IS NULL OR deleted_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  CHECK ((status = 'done') = (completed_utc IS NOT NULL)),
  CHECK (due_time IS NULL OR due_date IS NOT NULL),
  CHECK (parent_task_id IS NULL OR parent_task_id <> id)
) STRICT;
-- Today / overdue / upcoming: open, not deleted, by date then priority.
CREATE INDEX ix_task_open_due   ON task(due_date, priority DESC) WHERE status = 'open' AND deleted_utc IS NULL;
CREATE INDEX ix_task_done       ON task(completed_utc DESC) WHERE status = 'done';
CREATE INDEX ix_task_parent     ON task(parent_task_id) WHERE parent_task_id IS NOT NULL;
CREATE INDEX ix_task_section    ON task(section_id);
CREATE INDEX ix_task_topic      ON task(topic_id);
CREATE INDEX ix_task_recurrence ON task(recurrence_id);

CREATE TABLE note (                           -- topic timeline; topic_id NULL = loose note (inbox)
  id          INTEGER PRIMARY KEY,
  topic_id    INTEGER REFERENCES topic(id) ON DELETE CASCADE,
  kind        TEXT NOT NULL DEFAULT 'note' CHECK (kind IN ('note','status','system')),
  body        TEXT NOT NULL CHECK (length(trim(body)) > 0),
  created_utc TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  deleted_utc TEXT CHECK (deleted_utc IS NULL OR deleted_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z')
) STRICT;
CREATE INDEX ix_note_topic ON note(topic_id, created_utc);

CREATE TABLE reminder (
  id                INTEGER PRIMARY KEY,
  task_id           INTEGER REFERENCES task(id)  ON DELETE CASCADE,
  topic_id          INTEGER REFERENCES topic(id) ON DELETE CASCADE,
  title             TEXT,                                               -- loose reminder (no task, no topic)
  remind_at_utc     TEXT NOT NULL CHECK (remind_at_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  snoozed_until_utc TEXT CHECK (snoozed_until_utc IS NULL OR snoozed_until_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  fired_utc         TEXT CHECK (fired_utc IS NULL OR fired_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  dismissed_utc     TEXT CHECK (dismissed_utc IS NULL OR dismissed_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  created_utc       TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  CHECK ((task_id IS NOT NULL) + (topic_id IS NOT NULL) <= 1),
  CHECK (task_id IS NOT NULL OR topic_id IS NOT NULL OR length(trim(title)) > 0)
) STRICT;
-- The scheduler asks "what is due by now?": pending reminders by their effective time.
CREATE INDEX ix_reminder_pending ON reminder(coalesce(snoozed_until_utc, remind_at_utc)) WHERE dismissed_utc IS NULL;
CREATE INDEX ix_reminder_task    ON reminder(task_id);
CREATE INDEX ix_reminder_topic   ON reminder(topic_id);

CREATE TABLE link (
  id              INTEGER PRIMARY KEY,
  url             TEXT NOT NULL CHECK (url LIKE 'http://%' OR url LIKE 'https://%' OR url LIKE 'file:%' OR url LIKE 'mailto:%'),
  title           TEXT NOT NULL CHECK (length(trim(title)) > 0),
  description     TEXT,
  favicon         BLOB CHECK (favicon IS NULL OR length(favicon) <= 262144),
  favicon_utc     TEXT CHECK (favicon_utc IS NULL OR favicon_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  pinned          INTEGER NOT NULL DEFAULT 0 CHECK (pinned IN (0,1)),
  open_count      INTEGER NOT NULL DEFAULT 0 CHECK (open_count >= 0),
  last_opened_utc TEXT CHECK (last_opened_utc IS NULL OR last_opened_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  created_utc     TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  updated_utc     TEXT NOT NULL CHECK (updated_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  deleted_utc     TEXT CHECK (deleted_utc IS NULL OR deleted_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z')
) STRICT;
-- One live row per URL; a deleted link can be saved again.
CREATE UNIQUE INDEX ux_link_url  ON link(url) WHERE deleted_utc IS NULL;
CREATE INDEX ix_link_pinned      ON link(pinned DESC, title) WHERE deleted_utc IS NULL;

CREATE TABLE task_tag (
  task_id INTEGER NOT NULL REFERENCES task(id) ON DELETE CASCADE,
  tag_id  INTEGER NOT NULL REFERENCES tag(id)  ON DELETE CASCADE,
  PRIMARY KEY (task_id, tag_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_task_tag_tag ON task_tag(tag_id);

CREATE TABLE topic_tag (
  topic_id INTEGER NOT NULL REFERENCES topic(id) ON DELETE CASCADE,
  tag_id   INTEGER NOT NULL REFERENCES tag(id)   ON DELETE CASCADE,
  PRIMARY KEY (topic_id, tag_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_topic_tag_tag ON topic_tag(tag_id);

CREATE TABLE link_tag (
  link_id INTEGER NOT NULL REFERENCES link(id) ON DELETE CASCADE,
  tag_id  INTEGER NOT NULL REFERENCES tag(id)  ON DELETE CASCADE,
  PRIMARY KEY (link_id, tag_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_link_tag_tag ON link_tag(tag_id);

CREATE TABLE topic_link (
  topic_id INTEGER NOT NULL REFERENCES topic(id) ON DELETE CASCADE,
  link_id  INTEGER NOT NULL REFERENCES link(id)  ON DELETE CASCADE,
  PRIMARY KEY (topic_id, link_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_topic_link_link ON topic_link(link_id);
