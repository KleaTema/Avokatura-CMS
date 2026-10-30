import { CssBaseline, ThemeProvider, createTheme } from '@mui/material'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { AuthProvider } from './app/AuthProvider'
import { DatesProvider } from './app/DatesProvider'
import { router } from './app/routes'

const queryClient = new QueryClient()
const theme = createTheme()

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
