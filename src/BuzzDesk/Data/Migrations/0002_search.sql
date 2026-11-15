-- BuzzDesk · migration 0002 · global search (Ctrl+F)
--
-- One FTS5 index over tasks, topics, notes and links. rowid = id * 8 + kind (1 task, 2 topic, 3 note, 4 link), so
-- every trigger finds its row by rowid (no scan of the UNINDEXED columns). unicode61 with remove_diacritics 2:
-- "revision" finds "revisión"; prefix indexes of 2 and 3 characters make "mig*" cheap.
-- Columns: title (weight 10), body (notes, description, URL; 1), tags (5) — see Data/Search.cs.
-- Soft-deleted rows leave the index; tag changes reindex their owner.

CREATE VIRTUAL TABLE search_fts USING fts5(
  kind UNINDEXED, ref_id UNINDEXED, title, body, tags,
  tokenize = 'unicode61 remove_diacritics 2',
  prefix = '2 3'
);


-- task

CREATE TRIGGER search_task_ai AFTER INSERT ON task BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 1;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT t.id * 8 + 1, 'task', t.id, t.title, coalesce(t.notes, ''), coalesce((SELECT group_concat(g.name, ' ') FROM task_tag x JOIN tag g ON g.id = x.tag_id WHERE x.task_id = t.id), '') FROM task t WHERE t.id = new.id AND t.deleted_utc IS NULL;
END;

CREATE TRIGGER search_task_au AFTER UPDATE ON task BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 1;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT t.id * 8 + 1, 'task', t.id, t.title, coalesce(t.notes, ''), coalesce((SELECT group_concat(g.name, ' ') FROM task_tag x JOIN tag g ON g.id = x.tag_id WHERE x.task_id = t.id), '') FROM task t WHERE t.id = new.id AND t.deleted_utc IS NULL;
END;

CREATE TRIGGER search_task_ad AFTER DELETE ON task BEGIN
  DELETE FROM search_fts WHERE rowid = old.id * 8 + 1;
END;

-- topic

CREATE TRIGGER search_topic_ai AFTER INSERT ON topic BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 2;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT tp.id * 8 + 2, 'topic', tp.id, tp.title, coalesce(tp.description, '') || ' ' || coalesce((SELECT p.name FROM person p WHERE p.id = tp.waiting_on_person_id), ''), coalesce((SELECT group_concat(g.name, ' ') FROM topic_tag x JOIN tag g ON g.id = x.tag_id WHERE x.topic_id = tp.id), '') FROM topic tp WHERE tp.id = new.id AND tp.deleted_utc IS NULL;
END;

CREATE TRIGGER search_topic_au AFTER UPDATE ON topic BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 2;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT tp.id * 8 + 2, 'topic', tp.id, tp.title, coalesce(tp.description, '') || ' ' || coalesce((SELECT p.name FROM person p WHERE p.id = tp.waiting_on_person_id), ''), coalesce((SELECT group_concat(g.name, ' ') FROM topic_tag x JOIN tag g ON g.id = x.tag_id WHERE x.topic_id = tp.id), '') FROM topic tp WHERE tp.id = new.id AND tp.deleted_utc IS NULL;
END;

CREATE TRIGGER search_topic_ad AFTER DELETE ON topic BEGIN
  DELETE FROM search_fts WHERE rowid = old.id * 8 + 2;
END;

-- note

CREATE TRIGGER search_note_ai AFTER INSERT ON note BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 3;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT n.id * 8 + 3, 'note', n.id, '', n.body, '' FROM note n WHERE n.id = new.id AND n.deleted_utc IS NULL AND n.kind = 'note';
END;

CREATE TRIGGER search_note_au AFTER UPDATE ON note BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 3;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT n.id * 8 + 3, 'note', n.id, '', n.body, '' FROM note n WHERE n.id = new.id AND n.deleted_utc IS NULL AND n.kind = 'note';
END;

CREATE TRIGGER search_note_ad AFTER DELETE ON note BEGIN
  DELETE FROM search_fts WHERE rowid = old.id * 8 + 3;
END;

-- link

CREATE TRIGGER search_link_ai AFTER INSERT ON link BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 4;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT l.id * 8 + 4, 'link', l.id, l.title, l.url || ' ' || coalesce(l.description, ''), coalesce((SELECT group_concat(g.name, ' ') FROM link_tag x JOIN tag g ON g.id = x.tag_id WHERE x.link_id = l.id), '') FROM link l WHERE l.id = new.id AND l.deleted_utc IS NULL;
END;

CREATE TRIGGER search_link_au AFTER UPDATE ON link BEGIN
  DELETE FROM search_fts WHERE rowid = new.id * 8 + 4;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT l.id * 8 + 4, 'link', l.id, l.title, l.url || ' ' || coalesce(l.description, ''), coalesce((SELECT group_concat(g.name, ' ') FROM link_tag x JOIN tag g ON g.id = x.tag_id WHERE x.link_id = l.id), '') FROM link l WHERE l.id = new.id AND l.deleted_utc IS NULL;
END;

CREATE TRIGGER search_link_ad AFTER DELETE ON link BEGIN
  DELETE FROM search_fts WHERE rowid = old.id * 8 + 4;
END;

-- tags of a task

CREATE TRIGGER search_task_tag_ai AFTER INSERT ON task_tag BEGIN
  DELETE FROM search_fts WHERE rowid = new.task_id * 8 + 1;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT t.id * 8 + 1, 'task', t.id, t.title, coalesce(t.notes, ''), coalesce((SELECT group_concat(g.name, ' ') FROM task_tag x JOIN tag g ON g.id = x.tag_id WHERE x.task_id = t.id), '') FROM task t WHERE t.id = new.task_id AND t.deleted_utc IS NULL;
END;

CREATE TRIGGER search_task_tag_ad AFTER DELETE ON task_tag BEGIN
  DELETE FROM search_fts WHERE rowid = old.task_id * 8 + 1;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT t.id * 8 + 1, 'task', t.id, t.title, coalesce(t.notes, ''), coalesce((SELECT group_concat(g.name, ' ') FROM task_tag x JOIN tag g ON g.id = x.tag_id WHERE x.task_id = t.id), '') FROM task t WHERE t.id = old.task_id AND t.deleted_utc IS NULL;
END;

-- tags of a topic

CREATE TRIGGER search_topic_tag_ai AFTER INSERT ON topic_tag BEGIN
  DELETE FROM search_fts WHERE rowid = new.topic_id * 8 + 2;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT tp.id * 8 + 2, 'topic', tp.id, tp.title, coalesce(tp.description, '') || ' ' || coalesce((SELECT p.name FROM person p WHERE p.id = tp.waiting_on_person_id), ''), coalesce((SELECT group_concat(g.name, ' ') FROM topic_tag x JOIN tag g ON g.id = x.tag_id WHERE x.topic_id = tp.id), '') FROM topic tp WHERE tp.id = new.topic_id AND tp.deleted_utc IS NULL;
END;

CREATE TRIGGER search_topic_tag_ad AFTER DELETE ON topic_tag BEGIN
  DELETE FROM search_fts WHERE rowid = old.topic_id * 8 + 2;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT tp.id * 8 + 2, 'topic', tp.id, tp.title, coalesce(tp.description, '') || ' ' || coalesce((SELECT p.name FROM person p WHERE p.id = tp.waiting_on_person_id), ''), coalesce((SELECT group_concat(g.name, ' ') FROM topic_tag x JOIN tag g ON g.id = x.tag_id WHERE x.topic_id = tp.id), '') FROM topic tp WHERE tp.id = old.topic_id AND tp.deleted_utc IS NULL;
END;

-- tags of a link

CREATE TRIGGER search_link_tag_ai AFTER INSERT ON link_tag BEGIN
  DELETE FROM search_fts WHERE rowid = new.link_id * 8 + 4;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT l.id * 8 + 4, 'link', l.id, l.title, l.url || ' ' || coalesce(l.description, ''), coalesce((SELECT group_concat(g.name, ' ') FROM link_tag x JOIN tag g ON g.id = x.tag_id WHERE x.link_id = l.id), '') FROM link l WHERE l.id = new.link_id AND l.deleted_utc IS NULL;
END;

CREATE TRIGGER search_link_tag_ad AFTER DELETE ON link_tag BEGIN
  DELETE FROM search_fts WHERE rowid = old.link_id * 8 + 4;
  INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT l.id * 8 + 4, 'link', l.id, l.title, l.url || ' ' || coalesce(l.description, ''), coalesce((SELECT group_concat(g.name, ' ') FROM link_tag x JOIN tag g ON g.id = x.tag_id WHERE x.link_id = l.id), '') FROM link l WHERE l.id = old.link_id AND l.deleted_utc IS NULL;
END;

-- the person a topic waits on can be renamed

CREATE TRIGGER search_person_au AFTER UPDATE OF name ON person BEGIN
  UPDATE topic SET waiting_on_person_id = waiting_on_person_id WHERE waiting_on_person_id = new.id;
END;

-- backfill what already exists

INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT t.id * 8 + 1, 'task', t.id, t.title, coalesce(t.notes, ''), coalesce((SELECT group_concat(g.name, ' ') FROM task_tag x JOIN tag g ON g.id = x.tag_id WHERE x.task_id = t.id), '') FROM task t WHERE t.deleted_utc IS NULL;

INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT tp.id * 8 + 2, 'topic', tp.id, tp.title, coalesce(tp.description, '') || ' ' || coalesce((SELECT p.name FROM person p WHERE p.id = tp.waiting_on_person_id), ''), coalesce((SELECT group_concat(g.name, ' ') FROM topic_tag x JOIN tag g ON g.id = x.tag_id WHERE x.topic_id = tp.id), '') FROM topic tp WHERE tp.deleted_utc IS NULL;

INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT n.id * 8 + 3, 'note', n.id, '', n.body, '' FROM note n WHERE n.deleted_utc IS NULL AND n.kind = 'note';

INSERT INTO search_fts(rowid, kind, ref_id, title, body, tags) SELECT l.id * 8 + 4, 'link', l.id, l.title, l.url || ' ' || coalesce(l.description, ''), coalesce((SELECT group_concat(g.name, ' ') FROM link_tag x JOIN tag g ON g.id = x.tag_id WHERE x.link_id = l.id), '') FROM link l WHERE l.deleted_utc IS NULL;
