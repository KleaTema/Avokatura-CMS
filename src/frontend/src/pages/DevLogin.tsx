import { Alert, Box, Button, CircularProgress, FormControl, InputLabel, MenuItem, Paper, Select, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { Navigate, useNavigate } from 'react-router'
import { useAuth } from '../app/authContext'
import { authMode, devUsers, oidcConfigured, userManager } from '../app/authConfig'
import { sq } from '../i18n/sq'

export function DevLogin() {
  const { isAuthenticated, isLoading, loginDev, loginOidc } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState(devUsers[0])

  if (isLoading) return <CircularProgress />
  if (isAuthenticated) return <Navigate to="/" replace />

  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', mt: 10 }}>
      <Paper sx={{ p: 4, width: 400 }}>
        <Typography variant="h5" gutterBottom>
          {sq.login.title}
        </Typography>
        {authMode === 'dev' ? (
          <>
            <Typography variant="body2" sx={{ mb: 2 }}>
              {sq.login.devHint}
            </Typography>
            <FormControl fullWidth sx={{ mb: 2 }}>
              <InputLabel id="dev-user-label">{sq.login.user}</InputLabel>
              <Select
                labelId="dev-user-label"
                label={sq.login.user}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              >
                {devUsers.map((u) => (
                  <MenuItem key={u} value={u}>
                    {u}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
            <Button
              variant="contained"
              fullWidth
              onClick={() => {
                loginDev(email)
                void navigate('/', { replace: true })
              }}
            >
              {sq.login.submit}
            </Button>
          </>
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
