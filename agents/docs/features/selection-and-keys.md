# Selection and listing keys

Code: `ListingStore` (`selection`, `select`, `selectAll`, `selectedItems`, `selectedFiles`), `selection-bar.component.ts`,
`items/item-parts.ts` (`SelectBoxComponent`), `items/item-actions.service.ts` (`selectInstead`),
`items/folder-item.component.ts`, `items/listing-keys.ts`, `library-page.component.ts` (`onListingKey`),
`styles/selection.css`. Tests: `listing.store.spec.ts`, `items.spec.ts`, `listing-keys.spec.ts`, `files.spec.ts`.

- The selection holds paths of files **and folders** (1.56.0). `selectedFiles()` is what tagging acts on;
  `selectedItems()` the rest (quick links, Rename, later delete/move). Ranges use `items()` order (folders first).
- A click on a folder while something is selected toggles it: a **capture** listener on the folder host, because
  `RouterLink` navigates whatever `preventDefault` says; files go through `ItemActions.openFile` → `selectInstead`.
- Folder select boxes: over a card's pictures like a tile's; over a line's icon (hidden while the box shows).
- Keys work only once an item (`sfe-listing [data-path]`) has the focus, so arrows still scroll the page otherwise;
  never inside inputs or dialogs (the viewer and player are dialogs). Up/down pick the nearest item of the next row
  by `getBoundingClientRect`; with no layout (jsdom) they step like left/right.
- Shift+arrows select the item left and the one reached (the anchor moves, as with shift-clicks): going back doesn't
  deselect.
- Phones (1.62.0): a long press (`contextmenu` whose `pointerType` is `touch`, or on a touch-only screen) toggles the
  item's selection instead of opening the menu (`fromTouch` in `menus/context-menu.ts`, the item target's `touch`); the item menu is a bottom sheet
  (`max-width: 640px`, `.item-menu-title`, `.item-menu-backdrop`); `quick-links.component.ts` cancels drags on
  touch-only screens (Move to… instead).
- The bar (`selection-bar.component.ts`) is an overflow row ([overflow-menus](overflow-menus.md)):
  the host `.selection-dock` is a full-width fixed strip (clicks pass through) measured with `overflowIn`, the pill
  `.selection-bar` centred in it; the long count (`.selection-count`) leaves first for the short one, then Download,
  Copy, Move, Delete, Rename, Quick links go into its "⋯" (`more` entries); Clear, count, Select all, Tags stay. The
  count is read out by a `.visually-hidden` live region (the two visible ones are `aria-hidden`).
- Its buttons are 30 px with a mouse, 36 px on touch screens (`pointer: coarse`, `selection.css`; e2e
  `selection.spec.ts`).
