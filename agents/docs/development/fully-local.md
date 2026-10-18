# Fully local (no external URLs)

- Neither the client's sources nor the built `wwwroot` reference an external URL (`ApiTests.WebClientHasNoExternalResources`;
  the bundle may only name `angular.dev/` in framework error texts, and a few whole hosts that are never fetched:
  dummy link bases, XML namespaces, docs links in error messages; the list and why each is there are in the test).
- No npm package that loads anything from the network at run time. Swagger UI's validator is off.
- `WebClientHasNoExternalResources` scans the specs too: use dotless host names (`http://nas:8080`) in tests.
