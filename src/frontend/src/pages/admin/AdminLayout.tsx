import { Box, Tab, Tabs } from '@mui/material'
import { Navigate, NavLink, Outlet, useLocation } from 'react-router'
import { useMe } from '../../features/identity/api'
import { sq } from '../../i18n/sq'
import { ForbiddenPage } from '../ErrorPages'

const t = sq.adminNav

const adminItems = [
  { path: '/admin/users', label: t.users, permission: 'admin.users' },
  { path: '/admin/offices', label: t.offices, permission: 'admin.org' },
  { path: '/admin/institutions', label: t.institutions, permission: 'admin.org' },
  { path: '/admin/lists', label: t.lists, permission: 'admin.lookups' },
  { path: '/admin/file-types', label: t.fileTypes, permission: 'admin.lookups' },
  { path: '/admin/code-format', label: t.codeFormat, permission: 'admin.lookups' },
]

function useVisibleAdminItems() {
  const me = useMe(true)
  return me.data ? adminItems.filter((i) => me.data.permissions.includes(i.permission)) : null
}

/** Sub-menu of the admin area: one tab per page the user has the permission for. */
export function AdminLayout() {
  const items = useVisibleAdminItems()
  const { pathname } = useLocation()
  if (!items) return null

  const current = items.find((i) => pathname.startsWith(i.path))?.path ?? false
  return (
    <Box>
      <Tabs value={current} aria-label={sq.menu.admin} sx={{ mb: 3, borderBottom: 1, borderColor: 'divider' }}>
        {items.map((i) => (
          <Tab key={i.path} value={i.path} label={i.label} component={NavLink} to={i.path} />
        ))}
      </Tabs>
      <Outlet />
    </Box>
  )
}

/** /admin goes to the first admin page the user may open. */
export function AdminIndex() {
  const items = useVisibleAdminItems()
  if (!items) return null
  return items[0] ? <Navigate to={items[0].path} replace /> : <ForbiddenPage />
}
