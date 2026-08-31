The ASP.NET Core Web API: the *only* write path into the system (per the PRD's API-first
principle). EF Core + PostgreSQL persistence, JWT auth, OpenAPI generation, and the
input-builder layer that turns persisted state into Engine input. References Domain and Engine;
the frontend and CLI are both just clients of this.
