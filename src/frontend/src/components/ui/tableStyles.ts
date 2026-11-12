/** The style of a cell in a sticky column (header, body or summary): pinned left, opaque, above the scrolled cells. */
export const stickyCellSx = {
  position: 'sticky',
  left: 0,
  zIndex: 2,
  bgcolor: 'background.paper',
  borderRight: 1,
  borderRightColor: 'divider',
} as const
