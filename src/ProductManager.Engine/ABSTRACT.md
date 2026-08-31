The Monte Carlo simulation and calibration engine: pure C# library, zero project or package
references, `(WorldSnapshot-ish input) → Trajectories`. Two simulation modes (aggregate stock-
and-flow v0, firm-level choice v1) sharing one result shape, plus a deterministic calibration
engine that fits parameters from the facts ledger. Read this before touching anything that
affects run output — this is the "wrong numbers silently corrupt decisions" code.
