/**
 * Design tokens — the only place colours, sizes and fonts are defined. Pages and components use the MUI theme
 * (built from these) or these tokens, never their own values.
 *
 * Contrast (WCAG 2.x, normal text ≥ 4.5:1), checked for every pair used:
 * - textPrimary on white / pageBg / tableHead: 14.5 / 13.4 / 12.9; textSecondary: 5.4 / 5.0 / 4.8
 * - white on primary 6.0, navy800 14.1, navy700 12.6, navActive 6.8, goldButton 4.7; primary on white 6.0, on blueTint 5.5
 * Adjusted because the given value was too light for text:
 * - goldButton #8F6F3E: gold #B8935A darkened (same hue 36° and saturation, lightness 54% → 40%) for white text
 * - successText #257E49 (#2E9E5B on its tint: 3.1), warningText #9F620E (#C77A12 on white: 3.4), errorText #D12828
 *   (#D93A3A on its tint: 4.1) — the strong text of tinted chips and alerts
 * - white on warning #F5A623 is 2.0: text on a warning fill uses textPrimary instead
 * - gold #B8935A is for lines and icons on navy only (5.3 on navy900; 2.9 on white — not used there)
 */
export const tokens = {
  color: {
    navy900: '#0E2646',
    navy800: '#102B52',
    navy700: '#15335E',
    /** end of the header gradient */
    navyHeaderEnd: '#16365F',
    navActive: '#1E5AA8',
    primary: '#1F5FC2',
    primaryLight: '#7FB2EE',
    success: '#2E9E5B',
    successText: '#257E49',
    warning: '#F5A623',
    warningText: '#9F620E',
    error: '#D93A3A',
    errorText: '#D12828',
    gold: '#B8935A',
    goldButton: '#8F6F3E',
    goldButtonHover: '#7A5F35',
    pageBg: '#F3F6FA',
    cardBorder: '#E6EBF2',
    tableHead: '#EEF2F7',
    textPrimary: '#13294B',
    textSecondary: '#5B6B82',
    white: '#FFFFFF',
    tint: {
      blue: '#EEF4FC',
      green: '#EEF7F1',
      orange: '#FDF6EC',
      red: '#FCEFEF',
      purple: '#F3EFFC',
    },
    /** translucent white on navy: the header search field and dividers */
    onNavy: {
      field: 'rgba(255, 255, 255, 0.10)',
      fieldHover: 'rgba(255, 255, 255, 0.16)',
      border: 'rgba(255, 255, 255, 0.22)',
      muted: 'rgba(255, 255, 255, 0.78)',
      hover: 'rgba(255, 255, 255, 0.08)',
    },
  },
  font: {
    family: '"Inter", system-ui, -apple-system, "Segoe UI", Roboto, Arial, sans-serif',
    mono: 'ui-monospace, "Cascadia Mono", Consolas, "Liberation Mono", monospace',
  },
  radius: {
    card: 8,
    control: 6,
    dialog: 10,
    pill: 999,
  },
  shadow: {
    card: '0 2px 8px rgba(15, 40, 80, 0.06)',
  },
  effect: {
    /** the large white emblem behind the login card (tune here only) */
    loginEmblemOpacity: 0.4,
    /**
     * 'normal': compared with 'soft-light' on emblem-white.webp — soft-light shows the image's solid navy rectangle
     * clearly and flattens the eagle, normal keeps the rectangle edge faint
     */
    loginEmblemBlend: 'normal',
    /** the 640×704 emblem is upscaled ~1.6× behind the card: a slight blur hides the rough edges (an SVG would be sharp) */
    loginEmblemBlur: '1px',
  },
  layout: {
    headerHeight: 86,
    sidebarWidth: 230,
    sidebarCollapsedWidth: 72,
    sidebarRowHeight: 54,
    activeBarWidth: 4,
    emblemSize: 58,
    controlHeight: 42,
    pagePadding: { xs: 2, md: 3.25 },
  },
} as const

/** Tinted colour sets (background + strong text) for chips, KPI cards and alerts. */
export const tones = {
  blue: { bg: tokens.color.tint.blue, fg: tokens.color.primary },
  green: { bg: tokens.color.tint.green, fg: tokens.color.successText },
  orange: { bg: tokens.color.tint.orange, fg: tokens.color.warningText },
  red: { bg: tokens.color.tint.red, fg: tokens.color.errorText },
  purple: { bg: tokens.color.tint.purple, fg: tokens.color.textSecondary },
  neutral: { bg: tokens.color.tableHead, fg: tokens.color.textSecondary },
} as const

export type Tone = keyof typeof tones
