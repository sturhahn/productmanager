The `pm` CLI: a pure HTTP client of the API (no project reference to Api or Domain) for seed
data loading, entity CRUD, and simulation/calibration run triggering. Built on
System.CommandLine. Its DTOs and enums are hand-mirrored copies of the API's, so they can drift
if the API changes without a matching CLI update — see `CLAUDE.md`.
