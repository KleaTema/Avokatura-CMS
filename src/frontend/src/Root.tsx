import { CssBaseline, ThemeProvider } from '@mui/material'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { AuthProvider } from './app/AuthProvider'
import { DatesProvider } from './app/DatesProvider'
import { router } from './app/routes'
import { theme } from './theme/theme'

const queryClient = new QueryClient()

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <DatesProvider>
          <AuthProvider>
            <RouterProvider router={router} />
          </AuthProvider>
        </DatesProvider>
      </ThemeProvider>
    </QueryClientProvider>
  )
}
