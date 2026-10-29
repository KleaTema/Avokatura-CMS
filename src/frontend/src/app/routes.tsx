import { createBrowserRouter, Navigate } from 'react-router'
import { DevLogin, OidcCallback } from '../pages/DevLogin'
import { ForbiddenPage, NotFoundPage } from '../pages/ErrorPages'
import { AdminIndex, AdminLayout } from '../pages/admin/AdminLayout'
import { CodeFormatPage } from '../pages/admin/CodeFormat'
import { FileTypesPage } from '../pages/admin/FileTypes'
import { InstitutionsPage } from '../pages/admin/Institutions'
import { OfficesPage } from '../pages/admin/Offices'
import { ListsPage } from '../pages/admin/Lists'
import { UsersPage } from '../pages/admin/UsersPage'
import { PlaceholderPage } from '../pages/PlaceholderPage'
import { Layout } from './Layout'
import { RequirePermission } from './RequirePermission'

// Menu codes without a real page yet; each shows the "coming soon" placeholder.
const placeholderMenuCodes = ['dashboard', 'cases', 'chat', 'notifications', 'profile', 'agenda', 'reports']

export const router = createBrowserRouter([
  { path: '/login', element: <DevLogin /> },
  { path: '/auth/callback', element: <OidcCallback /> },
  {
    path: '/',
    element: <Layout />,
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      ...placeholderMenuCodes.map((code) => ({ path: code, element: <PlaceholderPage menu={code} /> })),
      {
        path: 'admin',
        element: <AdminLayout />,
        children: [
          { index: true, element: <AdminIndex /> },
          {
            path: 'users',
            element: (
              <RequirePermission code="admin.users">
                <UsersPage />
              </RequirePermission>
            ),
          },
          {
            path: 'offices',
            element: (
              <RequirePermission code="admin.org">
                <OfficesPage />
              </RequirePermission>
            ),
          },
          {
            path: 'institutions',
            element: (
              <RequirePermission code="admin.org">
                <InstitutionsPage />
              </RequirePermission>
            ),
          },
          {
            path: 'lists',
            element: (
              <RequirePermission code="admin.lookups">
                <ListsPage />
              </RequirePermission>
            ),
          },
          {
            path: 'file-types',
            element: (
              <RequirePermission code="admin.lookups">
                <FileTypesPage />
              </RequirePermission>
            ),
          },
          {
            path: 'code-format',
            element: (
              <RequirePermission code="admin.lookups">
                <CodeFormatPage />
              </RequirePermission>
            ),
          },
        ],
      },
      { path: 'forbidden', element: <ForbiddenPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
