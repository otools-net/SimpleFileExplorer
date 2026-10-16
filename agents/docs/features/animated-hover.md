# Animated images on hover

Code: `src/Sfe.Core/Library/ImageAnimation.cs`, `LibraryEndpoints.Animated` (`GET api/files/animated/{path}`); client
`library/items/animated-hover.service.ts`, `FileItemBase` (`file-tile.component.ts`), `styles/trickplay.css`.

- Large images are listed through their still thumbnail ([thumbnails.md](thumbnails.md)); hovering (touch: holding)
  one that is animated shows the original over it. Same gestures as the trickplay hover, through its `HoverPlayer`
  interface ([hover-preview.md](hover-preview.md)); one preview plays at a time across both.
- Not in the index (no column, no migration): the route reads the file's structure on demand. GIF: image descriptors up
  to the second one; PNG: `acTL` with num_frames > 1 before `IDAT`; WebP: `VP8X` animation flag (0x02); AVIF: `avis`
  brand in `ftyp`. Anything else, or an unreadable file, is still.
- The client asks only when the tile shows the thumbnail (`sources()[0]` isn't the original: not a small image, not
  the one-per-row view with `singleViewOriginals`) and the extension is gif/png/apng/webp/avif; answers are kept per
  listing (`ListingStore.generation`).
- The original is inserted `hidden` with a `.animated-hover-loading` bar until `load`; leaving earlier removes its `src`
  (stops the download). Images are sent `no-store`, so the last 4 loaded `<img>` elements are kept for re-hovers.
- Off with `Thumbnails:AnimatedHover` (user-level, `/api/config` `animatedHover`).
