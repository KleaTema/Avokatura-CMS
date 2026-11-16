import { Tooltip } from '@mui/material'
import type { ReactNode } from 'react'
import { CaseCode } from '../../components/ui/CaseCode'
import { decimalText, type ReportColumn } from '../../features/reports/api'
import { sq } from '../../i18n/sq'
import { formatDate, formatDateTime, formatTiraneDate } from '../../lib/dates'
import { formatDecimal, formatMoney } from '../../lib/money'

/** ReportColumnKind of the API (Ash.Modules.Reports.Application.ReportColumnKind), by value. */
export const ColumnKind = { Text: 0, LongText: 1, Date: 2, DateTime: 3, Decimal: 4, YesNo: 5, Integer: 6 } as const

/** m² columns end in "M2" (claimAreaM2, wonAreaM2…): a number with " m²", no currency. */
const isArea = (key: string) => key.endsWith('M2')

export const isNumeric = (c: ReportColumn) => Number(c.kind) === ColumnKind.Decimal || Number(c.kind) === ColumnKind.Integer

/**
 * One cell of a report row, written as the column's kind says (the API's metadata, never guessed from the header):
 * the case code links to the case; dates in Tirane; money with the row's currency (lib/money.ts); m² separate.
 */
export function reportCell(
  column: ReportColumn,
  row: Record<string, unknown>,
  currencyKey: string,
): ReactNode {
  const value = row[column.key]
  if (column.key === 'caseCode') {
    // task 053h: a link only when the user can open the case; otherwise the code alone
    const linked = row.canOpen !== false && typeof row.casePublicId === 'string'
    return <CaseCode code={String(value)} publicId={linked ? (row.casePublicId as string) : null} />
  }

  if (value === null || value === undefined || value === '') return ''
  switch (Number(column.kind)) {
    case ColumnKind.Date:
      // a decision date is a calendar date (yyyy-MM-dd); a closing time an instant (Tirane date)
      return typeof value === 'string' && value.length === 10 ? formatDate(value) : formatTiraneDate(String(value))
    case ColumnKind.DateTime:
      return formatDateTime(String(value))
    case ColumnKind.YesNo:
      return value === true ? sq.reports.yes : sq.reports.no
    case ColumnKind.Decimal: {
      const text = decimalText(value as number | string)
      if (text === null) return ''
      if (isArea(column.key)) return `${formatDecimal(text)} m²`
      const currency = typeof row[currencyKey] === 'string' ? (row[currencyKey] as string) : null
      return formatMoney(text, currency)
    }
    case ColumnKind.LongText: {
      const text = String(value)
      // long text (object, parties) shortened in the table; the full text in a tooltip (and the export)
      return text.length > 90 ? (
        <Tooltip title={text}>
          <span>{`${text.slice(0, 88)}…`}</span>
        </Tooltip>
      ) : (
        text
      )
    }
    default:
      return String(value)
  }
}
