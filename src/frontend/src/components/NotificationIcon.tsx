import {
  ArrowLeftRight,
  Bell,
  FilePlus,
  FileWarning,
  Gavel,
  Landmark,
  Share2,
  TriangleAlert,
  UserCheck,
  UserMinus,
  Users,
  type LucideIcon,
} from 'lucide-react'

/** One outline icon per notification type (NotificationTypes in the API); decorative — the title says what it is. */
const icons: Record<string, LucideIcon> = {
  CASE_REGISTERED: FilePlus,
  LAWYER_ASSIGNED: UserCheck,
  INSTITUTION_CASE_ASSIGNED: UserCheck,
  LAWYER_REMOVED: ArrowLeftRight,
  ASSISTANT_ASSIGNED: Users,
  ASSISTANT_REMOVED: UserMinus,
  JURIST_ASSIGNED: UserCheck,
  JURIST_REPLACED: ArrowLeftRight,
  INSTITUTION_LINKED: Landmark,
  CASE_DELEGATED: Share2,
  CASE_UNDELEGATED: Share2,
  PREVIOUS_LEVEL_NOT_CLOSED: TriangleAlert,
  CASE_CLOSED: Gavel,
  DOCUMENT_INFECTED: FileWarning,
  DOCUMENT_SCAN_FAILED: FileWarning,
}

export function NotificationIcon({ type, size = 20 }: { type: string; size?: number }) {
  const Icon = icons[type] ?? Bell
  return <Icon size={size} aria-hidden />
}
