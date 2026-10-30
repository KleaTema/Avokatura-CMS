import { createTheme } from '@mui/material'
import type {} from '@mui/x-date-pickers/themeAugmentation'
import { tokens, tones } from './tokens'

const c = tokens.color

/**
 * The MUI theme of the application, built only from the design tokens. Primary = blue; secondary = the gold of
 * the single main action of a page (contained secondary button).
 */
export const theme = createTheme({
  spacing: 8,
  shape: { borderRadius: tokens.radius.card },
  palette: {
    primary: { main: c.primary, light: c.primaryLight, contrastText: c.white },
    secondary: { main: c.goldButton, dark: c.goldButtonHover, contrastText: c.white },
    success: { main: c.success, dark: c.successText, contrastText: c.white },
    warning: { main: c.warning, dark: c.warningText, contrastText: c.textPrimary },
    error: { main: c.error, dark: c.errorText, contrastText: c.white },
    info: { main: c.primary, contrastText: c.white },
    background: { default: c.pageBg, paper: c.white },
    text: { primary: c.textPrimary, secondary: c.textSecondary },
    divider: c.cardBorder,
  },
  typography: {
    fontFamily: tokens.font.family,
    // h1 = page title, h2 = section title, h3 = card title
    h1: { fontSize: 30, fontWeight: 700, lineHeight: 1.25 },
    h2: { fontSize: 20, fontWeight: 600, lineHeight: 1.3 },
    h3: { fontSize: 17, fontWeight: 600, lineHeight: 1.35 },
    h4: { fontSize: 17, fontWeight: 600, lineHeight: 1.35 },
    h5: { fontSize: 15, fontWeight: 600, lineHeight: 1.4 },
    h6: { fontSize: 15, fontWeight: 600, lineHeight: 1.4 },
    subtitle1: { fontSize: 15, fontWeight: 500 },
    subtitle2: { fontSize: 14, fontWeight: 600 },
    body1: { fontSize: 15 },
    body2: { fontSize: 14 },
    caption: { fontSize: 13 },
    button: { fontSize: 14, fontWeight: 500, textTransform: 'none' },
  },
  components: {
    MuiCssBaseline: {
      styleOverrides: { body: { backgroundColor: c.pageBg } },
    },
    MuiPaper: {
      styleOverrides: {
        rounded: { borderRadius: tokens.radius.card },
        outlined: { borderColor: c.cardBorder, boxShadow: tokens.shadow.card },
        // the default Paper (cards, tables) looks like a card; menus and dialogs keep their higher elevations
        elevation1: { border: `1px solid ${c.cardBorder}`, boxShadow: tokens.shadow.card },
      },
    },
    MuiCard: {
      defaultProps: { variant: 'outlined' },
      styleOverrides: {
        root: { borderColor: c.cardBorder, boxShadow: tokens.shadow.card, borderRadius: tokens.radius.card },
      },
    },
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: { borderRadius: tokens.radius.control, textTransform: 'none', fontWeight: 500 },
        sizeMedium: { minHeight: 40 },
      },
    },
    MuiChip: {
      styleOverrides: {
        root: { borderRadius: tokens.radius.pill, fontWeight: 600 },
        // tinted background + strong text instead of solid fills
        colorPrimary: { backgroundColor: tones.blue.bg, color: tones.blue.fg },
        colorInfo: { backgroundColor: tones.blue.bg, color: tones.blue.fg },
        colorSuccess: { backgroundColor: tones.green.bg, color: tones.green.fg },
        colorWarning: { backgroundColor: tones.orange.bg, color: tones.orange.fg },
        colorError: { backgroundColor: tones.red.bg, color: tones.red.fg },
        colorDefault: { backgroundColor: tones.neutral.bg, color: tones.neutral.fg },
        outlined: { backgroundColor: c.white, borderColor: c.cardBorder },
      },
    },
    MuiTableHead: {
      styleOverrides: { root: { backgroundColor: c.tableHead } },
    },
    MuiTableCell: {
      styleOverrides: {
        root: { borderBottom: `1px solid ${c.cardBorder}`, fontSize: 14 },
        head: { fontWeight: 600, color: c.textPrimary, backgroundColor: c.tableHead },
      },
    },
    MuiTableRow: {
      styleOverrides: { hover: { '&:hover': { backgroundColor: c.tint.blue } } },
    },
    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          borderRadius: tokens.radius.control,
          backgroundColor: c.white,
          '& .MuiOutlinedInput-notchedOutline': { borderColor: c.cardBorder },
          '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: c.primaryLight },
        },
      },
    },
    // the date/time pickers use their own input base
    MuiPickersOutlinedInput: {
      styleOverrides: {
        root: {
          borderRadius: tokens.radius.control,
          backgroundColor: c.white,
          '& .MuiPickersOutlinedInput-notchedOutline': { borderColor: c.cardBorder },
        },
      },
    },
    MuiTextField: { defaultProps: { size: 'small' } },
    MuiAutocomplete: { defaultProps: { size: 'small' } },
    MuiFormControl: { defaultProps: { size: 'small' } },
    MuiTabs: {
      styleOverrides: {
        root: { borderBottom: `1px solid ${c.cardBorder}` },
        indicator: { backgroundColor: c.primary, height: 3, borderRadius: 3 },
      },
    },
    MuiTab: {
      styleOverrides: { root: { textTransform: 'none', fontWeight: 500, fontSize: 15, minHeight: 46 } },
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: { backgroundColor: c.navy900, fontSize: 13, borderRadius: tokens.radius.control },
        arrow: { color: c.navy900 },
      },
    },
    MuiDialog: {
      styleOverrides: { paper: { borderRadius: tokens.radius.dialog } },
    },
    MuiDialogTitle: {
      styleOverrides: { root: { fontSize: 20, fontWeight: 600 } },
    },
    MuiAlert: {
      styleOverrides: {
        // standard alerts: the tint + strong text of their severity
        root: ({ ownerState }) => {
          const tone =
            ownerState.severity === 'success'
              ? tones.green
              : ownerState.severity === 'warning'
                ? tones.orange
                : ownerState.severity === 'error'
                  ? tones.red
                  : tones.blue
          return {
            borderRadius: tokens.radius.card,
            ...((ownerState.variant ?? 'standard') === 'standard'
              ? { backgroundColor: tone.bg, color: tone.fg, '& .MuiAlert-icon': { color: tone.fg } }
              : {}),
          }
        },
      },
    },
    MuiLink: {
      defaultProps: { underline: 'hover' },
      styleOverrides: { root: { color: c.primary, fontWeight: 500 } },
    },
  },
})
