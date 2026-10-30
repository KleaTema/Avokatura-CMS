import { Alert, Autocomplete, Box, Button, CircularProgress, Paper, Stack, TextField, Typography } from '@mui/material'
import { useEffect, useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router'
import { api } from '../api/client'
import { useAuth } from '../app/authContext'
import { authMode, devUsers, oidcConfigured, userManager } from '../app/authConfig'
import { BrandImage } from '../app/shell/BrandImage'
import { sq } from '../i18n/sq'
import { tokens } from '../theme/tokens'

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
    <Box
      component="main"
      sx={{
        minHeight: '100vh',
        display: 'grid',
        placeItems: 'center',
        p: 2,
        background: `linear-gradient(160deg, ${tokens.color.navy800} 0%, ${tokens.color.navy900} 100%)`,
      }}
    >
      {/* large, subtle white emblem behind the card (decorative; hidden if the file cannot be loaded) */}
      <BrandImage
        src="/brand/emblem-white.webp"
        alt=""
        decorative
        sx={{
          position: 'fixed',
          top: '50%',
          left: '50%',
          transform: 'translate(-50%, -50%)',
          height: { xs: '90vh', sm: '110vh' },
          width: 'auto',
          maxWidth: 'none',
          opacity: tokens.effect.loginEmblemOpacity,
          mixBlendMode: tokens.effect.loginEmblemBlend,
          filter: `blur(${tokens.effect.loginEmblemBlur})`,
          pointerEvents: 'none',
          userSelect: 'none',
          zIndex: 0,
        }}
      />
      <Paper variant="outlined" sx={{ position: 'relative', zIndex: 1, p: { xs: 3, sm: 4 }, width: '100%', maxWidth: 420 }}>
        <Stack spacing={1} sx={{ alignItems: 'center', textAlign: 'center', mb: 3 }}>
          <BrandImage
            src="/brand/emblem-navy.webp"
            alt={sq.brand.emblemAlt}
            sx={{ height: tokens.layout.emblemSize, width: 'auto' }}
          />
          <Typography variant="h3" component="p" sx={{ textTransform: 'uppercase', letterSpacing: '0.06em' }}>
            {sq.brand.title}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {sq.brand.subtitle}
          </Typography>
        </Stack>
        <Typography variant="h2" component="h1" gutterBottom>
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
            <Button
              type="submit"
              variant="contained"
              color="secondary"
              size="large"
              fullWidth
              disabled={checking || !normalizeEmail(email)}
            >
              {sq.login.submit}
            </Button>
          </form>
        ) : oidcConfigured ? (
          <Button variant="contained" color="secondary" size="large" fullWidth onClick={() => void loginOidc()}>
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
