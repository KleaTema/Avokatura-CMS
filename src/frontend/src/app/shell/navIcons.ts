import {
  Bell,
  CalendarDays,
  ChartColumn,
  FolderOpen,
  LayoutDashboard,
  MessagesSquare,
  Settings,
  UserRound,
  type LucideIcon,
} from 'lucide-react'

/** Outline icon (lucide) per menu code from me.menus; unknown codes get the folder. */
const icons: Record<string, LucideIcon> = {
  dashboard: LayoutDashboard,
  cases: FolderOpen,
  chat: MessagesSquare,
  notifications: Bell,
  profile: UserRound,
  agenda: CalendarDays,
  reports: ChartColumn,
  admin: Settings,
}

export const navIcon = (code: string): LucideIcon => icons[code] ?? FolderOpen
