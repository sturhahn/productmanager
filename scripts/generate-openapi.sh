#!/usr/bin/env bash
# Regenerates openapi/productmanager.json from the API's own /swagger/v1/swagger.json route.
#
# The generator lives in the integration test suite (OpenApiDocumentTests): it boots the real
# pipeline with WebApplicationFactory, fetches the document, asserts the Phase 1 routes are there
# and writes the file. That means `dotnet test` alone already keeps the contract current; this
# script is the shortcut when you only want the file.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet test tests/ProductManager.Api.Tests/ProductManager.Api.Tests.csproj \
  --filter "FullyQualifiedName~OpenApiDocumentTests"

echo "Wrote $repo_root/openapi/productmanager.json"
