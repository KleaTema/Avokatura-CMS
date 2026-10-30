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
import { CaseListPage } from '../pages/cases/CaseList'
import { CasePage } from '../pages/cases/CasePage'
import { ContinueCasePage } from '../pages/cases/ContinueCase'
import { RegisterCasePage } from '../pages/cases/RegisterCase'
import { PlaceholderPage } from '../pages/PlaceholderPage'
import { DashboardPage } from '../pages/Dashboard'
import { Layout } from './Layout'
import { RequirePermission } from './RequirePermission'

// Menu codes without a real page yet; each shows the "coming soon" placeholder.
const placeholderMenuCodes = ['chat', 'notifications', 'profile', 'agenda', 'reports']

export const router = createBrowserRouter([
  { path: '/login', element: <DevLogin /> },
  { path: '/auth/callback', element: <OidcCallback /> },
  {
    path: '/',
    element: <Layout />,
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      { path: 'dashboard', element: <DashboardPage /> },
      ...placeholderMenuCodes.map((code) => ({ path: code, element: <PlaceholderPage menu={code} /> })),
      { path: 'cases', element: <CaseListPage /> },
      // static paths (register, continue) rank above these parameters
      { path: 'cases/:publicId', element: <CasePage /> },
      { path: 'cases/:publicId/:tab', element: <CasePage /> },
      {
        path: 'cases/register',
        element: (
          <RequirePermission code="case.create">
            <RegisterCasePage />
          </RequirePermission>
        ),
      },
      {
        path: 'cases/continue',
        element: (
          <RequirePermission code="case.link_previous">
            <ContinueCasePage />
          </RequirePermission>
        ),
      },
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
