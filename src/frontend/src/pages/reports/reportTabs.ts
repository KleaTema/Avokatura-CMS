/**
 * Task 053o — the tabs of "Raporte & Statistika" as the URL holds them (?tab=). The user-facing numbers changed
 * (Tabela 1, 2 reports; Tabela 3, 4 statistics); the API keeps its table numbers (1, 3, 5, 6). Old links (?tab=tabela1 …)
 * still open the same table.
 */
export const MAIN_TABS = ['raportet', 'statistikat'] as const
export type MainTab = (typeof MAIN_TABS)[number]

export const REPORT_TABS = ['aktive', 'mbyllura', 'mine', 'statistika', 'institucionale'] as const
export type ReportTab = (typeof REPORT_TABS)[number]

export const isReportTab = (value: string | null): value is ReportTab => REPORT_TABS.some((x) => x === value)

export const mainOf = (tab: ReportTab): MainTab => (tab === 'statistika' || tab === 'institucionale' ? 'statistikat' : 'raportet')

/** The API's table of each tab (the internal numbers stay). */
export const TABLE_OF = { aktive: 1, mbyllura: 3, statistika: 5, institucionale: 6 } as const

/** Links from before task 053o (?tab=tabela1 / tabela3 / tabela5 / tabela6). */
const LEGACY: Record<string, ReportTab> = { tabela1: 'aktive', tabela3: 'mbyllura', tabela5: 'statistika', tabela6: 'institucionale' }

export function tabFromParam(value: string | null): ReportTab {
  if (isReportTab(value)) return value
  return (value && LEGACY[value]) || 'aktive'
}

/** The tab of an API table number (a saved report). */
export function tabOfTable(table: number): ReportTab {
  switch (table) {
    case 3:
      return 'mbyllura'
    case 5:
      return 'statistika'
    case 6:
      return 'institucionale'
    default:
      return 'aktive'
  }
}
