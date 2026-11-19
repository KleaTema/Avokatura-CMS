import { Box, GlobalStyles, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'

/**
 * Task 053o — "Printo" prints ONLY the table with its results (and its title): the printout is rendered outside the
 * app (a node under <body>) and, on paper, the whole app (#root: menu, header, filters, buttons, pagination) is
 * hidden. On screen the printout is never shown. A4 landscape for wide tables; the table's header rows repeat on
 * every page (a <thead>).
 */
export function PrintPortal({ children, landscape }: { children: ReactNode; landscape: boolean }) {
  return createPortal(
    <Box id="report-print" sx={{ color: 'common.black', '@media screen': { display: 'none' } }}>
      <GlobalStyles
        styles={{
          '@media print': {
            '#root': { display: 'none !important' },
            '#report-print': { display: 'block' },
            body: { background: '#fff' },
            // the whole table on paper: no scroll box, no minimum width, no sticky cells
            '#report-print .MuiTableContainer-root': { overflow: 'visible !important', maxHeight: 'none !important', boxShadow: 'none' },
            '#report-print .MuiTable-root': { minWidth: '0 !important' },
            '#report-print .MuiTableCell-root': { position: 'static !important', fontSize: '8pt', padding: '2px 4px' },
            '#report-print thead': { display: 'table-header-group' },
            '#report-print tfoot': { display: 'table-row-group' },
            '#report-print tr': { breakInside: 'avoid' },
          },
        }}
      />
      <style>{`@page { size: A4 ${landscape ? 'landscape' : 'portrait'}; margin: 12mm 10mm; }`}</style>
      {children}
    </Box>,
    document.body,
  )
}

/** The printout's only text besides the table: its title. */
export function PrintTitle({ title }: { title: string }) {
  return (
    <Typography variant="h1" sx={{ fontSize: '14pt', fontWeight: 700, mb: 1.5 }}>
      {title}
    </Typography>
  )
}
