import { Box, GlobalStyles, Typography } from '@mui/material'
import { useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'

const t = sq.reports.builder

/**
 * Task 053o — "Printo" prints ONLY the report / statistics table: the printout is rendered outside the app (a node under
 * <body>) and, on paper, the whole app (#root: menu, header, filters, buttons, other sections) is hidden. On screen
 * the printout is never shown. A4 landscape for wide tables; the table's header rows repeat on every page (a <thead>);
 * every page carries "Faqja n nga m".
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
      {/* the @page rule: a template string (MUI's style objects cannot hold the page's margin boxes) */}
      <style>{`@page { size: A4 ${landscape ? 'landscape' : 'portrait'}; margin: 12mm 10mm 14mm; @bottom-right { content: "${t.page} " counter(page) " ${t.of} " counter(pages); font-size: 8pt; } }`}</style>
      {children}
    </Box>,
    document.body,
  )
}

/** The printout's header: the table's title, the filters in words (or "Pa filtra"), when and by whom. */
export function PrintHeader({ title, filters, generatedBy }: { title: string; filters: string[]; generatedBy: string }) {
  // the moment this printout was made (once, not on every render)
  const [printedAt] = useState(() => new Date().toISOString())
  return (
    <Box sx={{ mb: 1.5 }}>
      <Typography variant="h1" sx={{ fontSize: '14pt', fontWeight: 700 }}>
        {title}
      </Typography>
      <Typography sx={{ fontSize: '9pt' }}>{filters.length > 0 ? filters.join(' · ') : t.noFilters}</Typography>
      <Typography sx={{ fontSize: '8pt', fontStyle: 'italic' }}>{t.printedBy(formatDateTime(printedAt), generatedBy)}</Typography>
    </Box>
  )
}
