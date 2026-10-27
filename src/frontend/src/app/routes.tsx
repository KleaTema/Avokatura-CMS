import { createBrowserRouter, Navigate } from 'react-router'
import { DevLogin, OidcCallback } from '../pages/DevLogin'
import { ForbiddenPage, NotFoundPage } from '../pages/ErrorPages'
import { PlaceholderPage } from '../pages/PlaceholderPage'
import { Layout } from './Layout'

const menuCodes = ['dashboard', 'cases', 'chat', 'notifications', 'profile', 'agenda', 'reports', 'admin']

export const router = createBrowserRouter([
  { path: '/login', element: <DevLogin /> },
  { path: '/auth/callback', element: <OidcCallback /> },
  {
    path: '/',
    element: <Layout />,
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      ...menuCodes.map((code) => ({ path: code, element: <PlaceholderPage menu={code} /> })),
      { path: 'forbidden', element: <ForbiddenPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
