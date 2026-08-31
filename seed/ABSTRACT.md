Three layered JSON seed files (phase1/2/3) loaded through the `pm` CLI's `seed` command — not
hand-inserted, since the API is the only write path. Each file matches entities by natural key
(name), so loading is idempotent: re-running the same file against an already-seeded workspace
does not duplicate anything.
