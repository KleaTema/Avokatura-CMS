import { Alert, Autocomplete, Box, Button, CircularProgress, Paper, TextField, Typography } from '@mui/material'
import { useEffect, useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router'
import { api } from '../api/client'
import { useAuth } from '../app/authContext'
import { authMode, devUsers, oidcConfigured, userManager } from '../app/authConfig'
import { sq } from '../i18n/sq'

const normalizeEmail = (value: string) => value.trim().toLowerCase()

export function DevLogin() {
  const { isAuthenticated, isLoading, loginDev, loginOidc } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState(devUsers[0])
  const [error, setError] = useState<string | null>(null)
  const [checking, setChecking] = useState(false)

  if (isLoading) return <CircularProgress />
  if (isAuthenticated) return <Navigate to="/" replace />

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    const normalized = normalizeEmail(email)
    if (!normalized) return
    setError(null)
    setChecking(true)
    try {
      // Check the user before storing it, so an unknown or deactivated user stays on this page with a message.
      const { response } = await api.GET('/api/identity/me', { headers: { 'X-Dev-User': normalized } })
      if (response.ok) {
        loginDev(normalized)
        void navigate('/', { replace: true })
        return
      }
      setError(response.status === 401 ? sq.login.unknownUser : sq.errors.generic)
    } catch {
      setError(sq.errors.network)
    } finally {
      setChecking(false)
    }
  }

  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', mt: 10 }}>
      <Paper sx={{ p: 4, width: 400 }}>
        <Typography variant="h5" gutterBottom>
          {sq.login.title}
        </Typography>
        {authMode === 'dev' ? (
          <form onSubmit={(e) => void onSubmit(e)} noValidate>
            <Typography variant="body2" sx={{ mb: 2 }}>
              {sq.login.devHint}
            </Typography>
            {error && (
              <Alert severity="error" sx={{ mb: 2 }}>
                {error}
              </Alert>
            )}
            <Autocomplete
              freeSolo
              options={devUsers}
              inputValue={email}
              onInputChange={(_, value) => setEmail(value)}
              renderInput={(params) => (
                <TextField {...params} label={sq.login.user} type="email" autoComplete="off" autoFocus />
              )}
              sx={{ mb: 2 }}
            />
            <Button type="submit" variant="contained" fullWidth disabled={checking || !normalizeEmail(email)}>
              {sq.login.submit}
            </Button>
          </form>
        ) : oidcConfigured ? (
          <Button variant="contained" fullWidth onClick={() => void loginOidc()}>
            {sq.login.oidcSubmit}
          </Button>
        ) : (
          <Alert severity="error">{sq.login.oidcNotConfigured}</Alert>
        )}
      </Paper>
    </Box>
  )
}

export function OidcCallback() {
  const navigate = useNavigate()

  useEffect(() => {
    const finish = async () => {
      try {
        await userManager?.signinRedirectCallback()
        window.location.replace('/')
      } catch {
        void navigate('/login', { replace: true })
      }
    }
    void finish()
  }, [navigate])

  return <Typography sx={{ m: 4 }}>{sq.login.redirecting}</Typography>
}
