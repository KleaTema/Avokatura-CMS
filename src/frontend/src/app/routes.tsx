import { createBrowserRouter, Navigate } from 'react-router'
import { DevLogin, OidcCallback } from '../pages/DevLogin'
import { ForbiddenPage, NotFoundPage } from '../pages/ErrorPages'
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
      { path: 'admin', element: <Navigate to="/admin/users" replace /> },
      {
        path: 'admin/users',
        element: (
          <RequirePermission code="admin.users">
            <UsersPage />
          </RequirePermission>
        ),
      },
      { path: 'forbidden', element: <ForbiddenPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
