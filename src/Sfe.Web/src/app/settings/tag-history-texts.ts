/**
 * The texts of Clear tag history, naming only the sidebar's history lists shown: "Last viewed", and "Last searched"
 * when `searched` (its setting, `tags.sidebarSearched`) is on.
 */
export function tagHistoryTexts(searched: boolean): { question: string; title: string } {
    return searched
        ? {
              question: $localize`Forget the searched and viewed tags? The sidebar's “Last searched” and “Last viewed” \
lists start empty. This cannot be undone.`,
              title: $localize`Empty the sidebar's “Last searched” and “Last viewed” lists (asks first)`,
          }
        : {
              question: $localize`Forget the searched and viewed tags? The sidebar's “Last viewed” list starts empty. \
This cannot be undone.`,
              title: $localize`Empty the sidebar's “Last viewed” list (asks first)`,
          };
}
