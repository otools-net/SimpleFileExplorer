# Folder navigation (up / previous / next)

Code: `src/Sfe.Core/Library/LibraryBrowser.cs` (`Siblings`, `GetListing`), route `api/browse/siblings`; client
`ListingStore.loadSiblings`, `src/app/library/location/`.

- Folder nav (up/prev/next by the crumbs): `loadSiblings` asks `api/browse/siblings` after the listing, with the
  current sort.
- `LibraryBrowser.Siblings` reuses the parent's cached sorted listing (`GetListing`), so stepping through a big parent
  lists it once.
- The crumbs and folder links are shared with the compact bar: [location-bar.md](location-bar.md).
