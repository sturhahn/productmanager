Four test projects, one per `src/` project, same names with `.Tests` appended. Rigor is
deliberately not uniform: strict/invariant-focused on Domain and Engine, conventional
integration coverage on Api, and contract-drift + unit coverage on Cli — matching the PRD's
testing posture. Open the matching subdirectory's own files rather than reading this trio to
find a specific test.
