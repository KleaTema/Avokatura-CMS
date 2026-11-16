import { Alert, Autocomplete, Box, Button, CircularProgress, Paper, Stack, TextField, Typography } from '@mui/material'
import { useEffect, useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router'
import { api } from '../api/client'
import { useAuth } from '../app/authContext'
import { authConfigError, authMode, authProviders, cspMismatch, devUsers, isOidcSignedIn } from '../app/authConfig'
import { BrandImage } from '../app/shell/BrandImage'
import { sq } from '../i18n/sq'
import { tokens } from '../theme/tokens'

const normalizeEmail = (value: string) => value.trim().toLowerCase()

export function DevLogin() {
  const { isAuthenticated, isLoading, loginDev, loginWith } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState(devUsers[0])
  const [error, setError] = useState<string | null>(null)
  const [checking, setChecking] = useState(false)

  // task 051: with a single provider there is nothing to choose — go to its sign-in page at once
  const single = authMode === 'oidc' && !isAuthenticated && authProviders.length === 1 ? authProviders[0].name : null
  useEffect(() => {
    if (single) void loginWith(single)
  }, [single, loginWith])

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
        // one flat colour over the whole viewport; the page grows with the card, never scrolls because of the emblem
        position: 'relative',
        minHeight: '100dvh',
        display: 'grid',
        placeItems: 'center',
        p: 2,
        bgcolor: tokens.color.loginBg,
        overflow: 'hidden',
      }}
    >
      {/*
        The watermark layer: fills the page minus a margin, and the emblem is fitted INSIDE it (object-fit: contain),
        so the whole eagle — crest, wings, lower feathers — is always visible, never cropped or stretched; it shrinks
        on small screens and stops growing at the max size on wide ones. Opacity and blend only on the image.
        Decorative (alt="", aria-hidden); nothing is shown if the file cannot be loaded.
      */}
      <Box
        aria-hidden
        sx={{
          position: 'absolute',
          inset: 0,
          p: { xs: 2, sm: 4, md: 6 },
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          pointerEvents: 'none',
          userSelect: 'none',
          zIndex: 0,
        }}
      >
        <BrandImage
          src="/brand/emblem-white.webp"
          alt=""
          decorative
          sx={{
            display: 'block',
            width: '100%',
            height: '100%',
            maxWidth: tokens.effect.loginEmblemMaxWidth,
            maxHeight: tokens.effect.loginEmblemMaxHeight,
            objectFit: 'contain',
            opacity: tokens.effect.loginEmblemOpacity,
            mixBlendMode: tokens.effect.loginEmblemBlend,
          }}
        />
      </Box>
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
        ) : authConfigError ? (
          <Alert severity="error">{sq.errors.network}</Alert>
        ) : cspMismatch.length > 0 ? (
          // task 052: the providers the API names are not allowed by the frontend's CSP — tell the administrator
          <Alert severity="error">
            <strong>{sq.login.cspMismatch}</strong>
            <br />
            {sq.login.cspMismatchHosts(cspMismatch.join(', '))}
          </Alert>
        ) : authProviders.length === 0 ? (
          <Alert severity="error">{sq.login.oidcNotConfigured}</Alert>
        ) : authProviders.length === 1 ? (
          <Typography>{sq.login.redirecting}</Typography>
        ) : (
          // task 051: one button per enabled provider, the first one gold (the only gold button)
          <Stack spacing={1.5}>
            {authProviders.map((p, i) => (
              <Button
                key={p.name}
                variant={i === 0 ? 'contained' : 'outlined'}
                color={i === 0 ? 'secondary' : 'primary'}
                size="large"
                fullWidth
                onClick={() => void loginWith(p.name)}
              >
                {p.displayName}
              </Button>
            ))}
          </Stack>
        )}
      </Paper>
    </Box>
  )
}

/**
 * The providers' redirect target (/auth/callback): initAuth already completed the sign-in before the first render, so
 * this only goes on — into the app when signed in, back to the login otherwise. A full reload starts the session clean.
 */
export function OidcCallback() {
  useEffect(() => {
    window.location.replace(isOidcSignedIn() ? '/' : '/login')
  }, [])

  return <Typography sx={{ m: 4 }}>{sq.login.redirecting}</Typography>
}
