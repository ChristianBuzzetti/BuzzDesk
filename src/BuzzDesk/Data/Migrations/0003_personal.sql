-- BuzzDesk · migration 0003 · personal: habits, checklists, bills, important dates, day plan
--
-- Same conventions as 0001 (STRICT, *_utc instants, *_date / day local calendar days, every FK indexed).
-- Money is INTEGER cents with an ISO-4217 currency per row, never REAL. Yearly repeats reuse the recurrence table as
-- monthly with interval 12, so recurrence's CHECK on freq does not need a table rebuild.

CREATE TABLE habit (
  id             INTEGER PRIMARY KEY,
  name           TEXT NOT NULL CHECK (length(trim(name)) > 0),
  icon           TEXT,
  color          TEXT CHECK (color IS NULL OR color GLOB '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'),
  target_per_day INTEGER NOT NULL DEFAULT 1 CHECK (target_per_day BETWEEN 1 AND 50),
  weekdays_mask  INTEGER NOT NULL DEFAULT 127 CHECK (weekdays_mask BETWEEN 1 AND 127),   -- bit0 = Monday: days it applies
  sort_order     INTEGER NOT NULL DEFAULT 0,
  archived_utc TEXT CHECK (archived_utc IS NULL OR archived_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  created_utc TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z')
) STRICT;

-- One row per habit and day it was done (count for "8 glasses of water"). Streaks are computed, never stored.
CREATE TABLE habit_check (
  habit_id INTEGER NOT NULL REFERENCES habit(id) ON DELETE CASCADE,
  day TEXT NOT NULL CHECK (day GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
  count    INTEGER NOT NULL DEFAULT 1 CHECK (count BETWEEN 1 AND 50),
  PRIMARY KEY (habit_id, day)
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_habit_check_day ON habit_check(day);

CREATE TABLE checklist (
  id          INTEGER PRIMARY KEY,
  name        TEXT NOT NULL CHECK (length(trim(name)) > 0),
  icon        TEXT,
  sort_order  INTEGER NOT NULL DEFAULT 0,
  created_utc TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  updated_utc TEXT NOT NULL CHECK (updated_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  deleted_utc TEXT CHECK (deleted_utc IS NULL OR deleted_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z')
) STRICT;
CREATE UNIQUE INDEX ux_checklist_name ON checklist(name COLLATE NOCASE) WHERE deleted_utc IS NULL;

CREATE TABLE checklist_item (
  id           INTEGER PRIMARY KEY,
  checklist_id INTEGER NOT NULL REFERENCES checklist(id) ON DELETE CASCADE,
  text         TEXT NOT NULL CHECK (length(trim(text)) > 0),
  done_utc TEXT CHECK (done_utc IS NULL OR done_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  sort_order   INTEGER NOT NULL DEFAULT 0,
  created_utc TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z')
) STRICT;
CREATE INDEX ix_checklist_item_list ON checklist_item(checklist_id, sort_order);

CREATE TABLE bill (
  id                 INTEGER PRIMARY KEY,
  name               TEXT NOT NULL CHECK (length(trim(name)) > 0),
  category           TEXT,
  kind               TEXT NOT NULL DEFAULT 'payment' CHECK (kind IN ('payment','renewal')),   -- renewal: DNI, passport, VTV
  amount_cents       INTEGER CHECK (amount_cents IS NULL OR amount_cents >= 0),
  currency           TEXT NOT NULL CHECK (currency GLOB '[A-Z][A-Z][A-Z]'),
  recurrence_id      INTEGER REFERENCES recurrence(id) ON DELETE SET NULL,
  next_due_date TEXT CHECK (next_due_date IS NULL OR next_due_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
  remind_days_before INTEGER NOT NULL DEFAULT 3 CHECK (remind_days_before BETWEEN 0 AND 60),
  notes              TEXT,
  archived_utc TEXT CHECK (archived_utc IS NULL OR archived_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  created_utc TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  updated_utc TEXT NOT NULL CHECK (updated_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z')
) STRICT;
CREATE INDEX ix_bill_due        ON bill(next_due_date) WHERE archived_utc IS NULL;
CREATE INDEX ix_bill_recurrence ON bill(recurrence_id);

CREATE TABLE bill_payment (
  id           INTEGER PRIMARY KEY,
  bill_id      INTEGER NOT NULL REFERENCES bill(id) ON DELETE CASCADE,
  due_date TEXT NOT NULL CHECK (due_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
  paid_utc TEXT NOT NULL CHECK (paid_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  amount_cents INTEGER CHECK (amount_cents IS NULL OR amount_cents >= 0),
  currency     TEXT NOT NULL CHECK (currency GLOB '[A-Z][A-Z][A-Z]'),
  note         TEXT,
  UNIQUE (bill_id, due_date)                     -- one payment per due date (also the index for bill_id)
) STRICT;
CREATE INDEX ix_bill_payment_paid ON bill_payment(paid_utc);

-- Birthdays, anniversaries: every year on month/day (29/2 is remembered on 28/2 in common years).
CREATE TABLE important_date (
  id                 INTEGER PRIMARY KEY,
  person_id          INTEGER REFERENCES person(id) ON DELETE CASCADE,
  label              TEXT NOT NULL CHECK (length(trim(label)) > 0),
  kind               TEXT NOT NULL CHECK (kind IN ('birthday','anniversary','other')),
  month              INTEGER NOT NULL CHECK (month BETWEEN 1 AND 12),
  day                INTEGER NOT NULL CHECK (day BETWEEN 1 AND 31),
  year               INTEGER CHECK (year IS NULL OR year BETWEEN 1900 AND 2200),
  remind_days_before INTEGER NOT NULL DEFAULT 3 CHECK (remind_days_before BETWEEN 0 AND 60),
  created_utc TEXT NOT NULL CHECK (created_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  CHECK (day <= CASE month WHEN 2 THEN 29 WHEN 4 THEN 30 WHEN 6 THEN 30 WHEN 9 THEN 30 WHEN 11 THEN 30 ELSE 31 END)
) STRICT;
CREATE INDEX ix_important_date_md     ON important_date(month, day);
CREATE INDEX ix_important_date_person ON important_date(person_id);

-- The day plan: up to three tasks that matter most that day.
CREATE TABLE day_focus (
  day TEXT NOT NULL CHECK (day GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
  position INTEGER NOT NULL CHECK (position BETWEEN 1 AND 3),
  task_id  INTEGER NOT NULL REFERENCES task(id) ON DELETE CASCADE,
  PRIMARY KEY (day, position),
  UNIQUE (day, task_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_day_focus_task ON day_focus(task_id);

-- One-a-day notices (a bill due soon, a birthday coming) so each rings once.
CREATE TABLE notice_log (
  kind     TEXT NOT NULL CHECK (kind IN ('bill','date')),
  ref_id   INTEGER NOT NULL,
  for_date TEXT NOT NULL CHECK (for_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
  fired_utc TEXT NOT NULL CHECK (fired_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
  PRIMARY KEY (kind, ref_id, for_date)
) STRICT, WITHOUT ROWID;


-- search: checklists (with their items), bills, important dates (rowid kinds 5, 6, 7)

CREATE TRIGGER search_checklist_ai AFTER INSERT ON checklist BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 5;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT c.id * 8 + 5, 'checklist', c.id, c.name, coalesce((SELECT group_concat(i.text, ' · ') FROM checklist_item i WHERE i.checklist_id = c.id), ''), '' FROM checklist c WHERE c.id = new.id AND c.deleted_utc IS NULL;
END;

CREATE TRIGGER search_checklist_au AFTER UPDATE ON checklist BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 5;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT c.id * 8 + 5, 'checklist', c.id, c.name, coalesce((SELECT group_concat(i.text, ' · ') FROM checklist_item i WHERE i.checklist_id = c.id), ''), '' FROM checklist c WHERE c.id = new.id AND c.deleted_utc IS NULL;
END;

CREATE TRIGGER search_checklist_ad AFTER DELETE ON checklist BEGIN
  DELETE FROM search_fts WHERE rowid = old.id * 8 + 5;
END;

CREATE TRIGGER search_bill_ai AFTER INSERT ON bill BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 6;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT b.id * 8 + 6, 'bill', b.id, b.name, coalesce(b.category, '') || ' ' || coalesce(b.notes, ''), '' FROM bill b WHERE b.id = new.id AND b.archived_utc IS NULL;
END;

CREATE TRIGGER search_bill_au AFTER UPDATE ON bill BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 6;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT b.id * 8 + 6, 'bill', b.id, b.name, coalesce(b.category, '') || ' ' || coalesce(b.notes, ''), '' FROM bill b WHERE b.id = new.id AND b.archived_utc IS NULL;
END;

CREATE TRIGGER search_bill_ad AFTER DELETE ON bill BEGIN
  DELETE FROM search_fts WHERE rowid = old.id * 8 + 6;
END;

CREATE TRIGGER search_date_ai AFTER INSERT ON important_date BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 7;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT x.id * 8 + 7, 'date', x.id, x.label, coalesce((SELECT p.name FROM person p WHERE p.id = x.person_id), ''), '' FROM important_date x WHERE x.id = new.id;
END;

CREATE TRIGGER search_date_au AFTER UPDATE ON important_date BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 7;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT x.id * 8 + 7, 'date', x.id, x.label, coalesce((SELECT p.name FROM person p WHERE p.id = x.person_id), ''), '' FROM important_date x WHERE x.id = new.id;
END;

CREATE TRIGGER search_date_ad AFTER DELETE ON important_date BEGIN
  DELETE FROM search_fts WHERE rowid = old.id * 8 + 7;
END;

CREATE TRIGGER search_checklist_item_ai AFTER INSERT ON checklist_item BEGIN
  DELETE FROM search_fts WHERE rowid = new.checklist_id * 8 + 5;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT c.id * 8 + 5, 'checklist', c.id, c.name, coalesce((SELECT group_concat(i.text, ' · ') FROM checklist_item i WHERE i.checklist_id = c.id), ''), '' FROM checklist c WHERE c.id = new.checklist_id AND c.deleted_utc IS NULL;
END;

CREATE TRIGGER search_checklist_item_ad AFTER DELETE ON checklist_item BEGIN
  DELETE FROM search_fts WHERE rowid = old.checklist_id * 8 + 5;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT c.id * 8 + 5, 'checklist', c.id, c.name, coalesce((SELECT group_concat(i.text, ' · ') FROM checklist_item i WHERE i.checklist_id = c.id), ''), '' FROM checklist c WHERE c.id = old.checklist_id AND c.deleted_utc IS NULL;
END;

CREATE TRIGGER search_checklist_item_au AFTER UPDATE OF text, checklist_id ON checklist_item BEGIN
  DELETE FROM search_fts WHERE rowid = old.checklist_id * 8 + 5;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT c.id * 8 + 5, 'checklist', c.id, c.name, coalesce((SELECT group_concat(i.text, ' · ') FROM checklist_item i WHERE i.checklist_id = c.id), ''), '' FROM checklist c WHERE c.id = old.checklist_id AND c.deleted_utc IS NULL;
  DELETE FROM search_fts WHERE rowid = new.checklist_id * 8 + 5;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT c.id * 8 + 5, 'checklist', c.id, c.name, coalesce((SELECT group_concat(i.text, ' · ') FROM checklist_item i WHERE i.checklist_id = c.id), ''), '' FROM checklist c WHERE c.id = new.checklist_id AND c.deleted_utc IS NULL;
END;

-- a person's new name reaches their important dates

CREATE TRIGGER search_person_dates_au AFTER UPDATE OF name ON person BEGIN
  UPDATE important_date SET person_id = person_id WHERE person_id = new.id;
END;
